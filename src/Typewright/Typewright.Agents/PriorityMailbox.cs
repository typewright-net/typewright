namespace Typewright.Agents;

/// <summary>
/// A mailbox that dequeues by priority (lowest first under the comparer; FIFO among
/// equal priorities), for agents that must let urgent messages overtake a backlog. A
/// second <see cref="IMailbox{TMessage}"/> implementation, and the proof that the loop
/// depends only on the abstraction. Bounded capacity supports
/// <see cref="MailboxFullMode.Wait"/> and <see cref="MailboxFullMode.Reject"/>.
/// </summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
/// <typeparam name="TPriority">The priority key.</typeparam>
public sealed class PriorityMailbox<TMessage, TPriority> : IMailbox<TMessage>
{
    private readonly PriorityQueue<Envelope<TMessage>, (TPriority Priority, long Sequence)> _queue;
    private readonly Func<Envelope<TMessage>, TPriority> _priority;
    private readonly int? _capacity;
    private readonly bool _reject;
    private readonly Lock _lock = new();
    private long _sequence;
    private bool _completed;
    private TaskCompletionSource? _readable;
    private TaskCompletionSource? _writable;

    /// <summary>Creates the mailbox.</summary>
    /// <param name="priority">Selects each envelope's priority.</param>
    /// <param name="comparer">Orders priorities; lowest dequeues first. Default: <see cref="Comparer{T}.Default"/>.</param>
    /// <param name="options">Capacity and overflow behavior; drop modes are not supported.</param>
    public PriorityMailbox(
        Func<Envelope<TMessage>, TPriority> priority,
        IComparer<TPriority>? comparer = null,
        MailboxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(priority);
        options ??= MailboxOptions.Unbounded;
        if (options.FullMode is MailboxFullMode.DropOldest or MailboxFullMode.DropNewest)
        {
            throw new NotSupportedException("The priority mailbox supports only the Wait and Reject full modes.");
        }

        if (options.Capacity is { } capacity)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1, nameof(options));
        }

        _priority = priority;
        _capacity = options.Capacity;
        _reject = options.FullMode == MailboxFullMode.Reject;
        var priorities = comparer ?? Comparer<TPriority>.Default;
        _queue = new PriorityQueue<Envelope<TMessage>, (TPriority, long)>(
            Comparer<(TPriority Priority, long Sequence)>.Create((left, right) =>
            {
                var byPriority = priorities.Compare(left.Priority, right.Priority);
                return byPriority != 0 ? byPriority : left.Sequence.CompareTo(right.Sequence);
            }));
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <inheritdoc />
    public PostResult TryPost(Envelope<TMessage> envelope)
    {
        var priority = _priority(envelope);
        TaskCompletionSource? readable;
        lock (_lock)
        {
            if (_completed)
            {
                return PostResult.Closed;
            }

            if (_queue.Count >= _capacity)
            {
                return PostResult.Full;
            }

            _queue.Enqueue(envelope, (priority, _sequence++));
            readable = _readable;
            _readable = null;
        }

        readable?.TrySetResult();
        return PostResult.Posted;
    }

    /// <inheritdoc />
    public ValueTask<PostResult> PostAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        var result = TryPost(envelope);
        return result != PostResult.Full || _reject
            ? new ValueTask<PostResult>(result)
            : WaitAndPostAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource readable;
        lock (_lock)
        {
            if (_queue.Count > 0)
            {
                return new ValueTask<bool>(true);
            }

            if (_completed)
            {
                return new ValueTask<bool>(false);
            }

            readable = _readable ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return WaitAsync(readable, cancellationToken);
    }

    /// <inheritdoc />
    public bool TryTake(out Envelope<TMessage> envelope)
    {
        TaskCompletionSource? writable = null;
        lock (_lock)
        {
            if (!_queue.TryDequeue(out envelope, out _))
            {
                return false;
            }

            if (_capacity is not null)
            {
                writable = _writable;
                _writable = null;
            }
        }

        writable?.TrySetResult();
        return true;
    }

    /// <inheritdoc />
    public void Complete()
    {
        TaskCompletionSource? readable;
        TaskCompletionSource? writable;
        lock (_lock)
        {
            _completed = true;
            readable = _readable;
            writable = _writable;
            _readable = null;
            _writable = null;
        }

        readable?.TrySetResult();
        writable?.TrySetResult();
    }

    private async ValueTask<bool> WaitAsync(TaskCompletionSource readable, CancellationToken cancellationToken)
    {
        await readable.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await WaitToReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PostResult> WaitAndPostAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource? writable = null;
            lock (_lock)
            {
                if (!_completed && _queue.Count >= _capacity)
                {
                    writable = _writable ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            if (writable is not null)
            {
                await writable.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var result = TryPost(envelope);
            if (result != PostResult.Full)
            {
                return result;
            }
        }
    }
}
