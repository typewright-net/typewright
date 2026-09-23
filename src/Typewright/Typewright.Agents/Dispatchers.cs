using System.Collections.Concurrent;

namespace Typewright.Agents;

/// <summary>
/// Decides where an agent's processing loop runs. The dispatcher owns the entire loop and
/// supplies its context: the loop awaits without <c>ConfigureAwait(false)</c>, so every
/// continuation returns to the context the dispatcher established. (Handler code that
/// itself uses <c>ConfigureAwait(false)</c> leaves that context for the rest of that
/// handler, though the loop returns to it when the handler completes.)
/// </summary>
public interface IDispatcher
{
    /// <summary>Starts <paramref name="loop"/> in the dispatcher's context and returns its lifetime.</summary>
    Task Run(Func<Task> loop);
}

/// <summary>Runs loops on the thread pool. The default.</summary>
public sealed class ThreadPoolDispatcher : IDispatcher
{
    /// <summary>The shared instance.</summary>
    public static ThreadPoolDispatcher Instance { get; } = new();

    /// <inheritdoc />
    public Task Run(Func<Task> loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        return Task.Run(loop);
    }
}

/// <summary>
/// Runs loops on any <see cref="TaskScheduler"/>; continuations return to it. With
/// <see cref="ConcurrentExclusiveSchedulerPair.ExclusiveScheduler"/>, agents sharing the
/// scheduler never execute simultaneously — exclusion holds between awaits, so while one
/// agent's handler awaits, another agent's work may run.
/// </summary>
public sealed class TaskSchedulerDispatcher : IDispatcher
{
    private readonly TaskScheduler _scheduler;

    /// <summary>Creates a dispatcher over <paramref name="scheduler"/>.</summary>
    public TaskSchedulerDispatcher(TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public Task Run(Func<Task> loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        return Task.Factory.StartNew(loop, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _scheduler).Unwrap();
    }
}

/// <summary>
/// Runs each loop on its own dedicated thread with a single-threaded
/// <see cref="SynchronizationContext"/>, so the loop and its handlers (up to any
/// <c>ConfigureAwait(false)</c> inside a handler) always execute on that thread. For
/// thread-affine resources and latency-sensitive agents that must not queue behind the
/// thread pool.
/// </summary>
public sealed class DedicatedThreadDispatcher : IDispatcher
{
    private readonly string? _threadName;

    /// <summary>Creates the dispatcher; each loop gets a background thread named <paramref name="threadName"/>.</summary>
    public DedicatedThreadDispatcher(string? threadName = null) => _threadName = threadName;

    /// <inheritdoc />
    public Task Run(Func<Task> loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunOnThread(loop, completion))
        {
            IsBackground = true,
            Name = _threadName ?? "Typewright agent",
        };
        thread.Start();
        return completion.Task;
    }

    private static void RunOnThread(Func<Task> loop, TaskCompletionSource completion)
    {
        using var context = new SingleThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);

        Task task;
        try
        {
            task = loop();
        }
        catch (Exception exception)
        {
            task = Task.FromException(exception);
        }

        task.ContinueWith(
            static (_, state) => ((SingleThreadSynchronizationContext)state!).Complete(),
            context,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        context.RunOnCurrentThread();

        if (task.IsFaulted)
        {
            completion.TrySetException(task.Exception!.InnerExceptions);
        }
        else if (task.IsCanceled)
        {
            completion.TrySetCanceled();
        }
        else
        {
            completion.TrySetResult();
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The loop has ended; late continuations (e.g. from fire-and-forget work a
                // handler started) run on the thread pool instead of being lost.
                ThreadPool.QueueUserWorkItem(static item => item.Callback(item.State), (Callback: d, State: state), preferLocal: false);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
            => throw new NotSupportedException("Synchronous sends to an agent thread are not supported.");

        public override SynchronizationContext CreateCopy() => this;

        public void RunOnCurrentThread()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }

        public void Complete() => _queue.CompleteAdding();

        public void Dispose() => _queue.Dispose();
    }
}
