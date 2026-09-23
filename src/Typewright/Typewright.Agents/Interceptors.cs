using System.Diagnostics;

namespace Typewright.Agents;

/// <summary>
/// Opens one <see cref="Activity"/> per message on <see cref="AgentDiagnostics.ActivitySourceName"/>,
/// parented to the sender's trace when the envelope carries a <c>traceparent</c> header,
/// and marks it as an error when handling throws. Costs nothing without a listener.
/// </summary>
public sealed class TracingInterceptor<TMessage> : IMessageInterceptor<TMessage>
{
    /// <inheritdoc />
    public ValueTask InvokeAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next)
        => AgentDiagnostics.ActivitySource.HasListeners() ? TraceAsync(context, next) : next(context);

    private static async ValueTask TraceAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next)
    {
        var headers = context.Envelope.Headers;
        ActivityContext.TryParse(headers.TraceParent, traceState: null, out var parent);
        var messageType = context.Envelope.Message!.GetType().Name;

        using var activity = AgentDiagnostics.ActivitySource.StartActivity(
            $"process {messageType}",
            ActivityKind.Consumer,
            parent);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("messaging.system", "typewright");
            activity.SetTag("messaging.operation.type", "process");
            activity.SetTag("messaging.destination.name", context.AgentName);
            activity.SetTag("typewright.message.type", messageType);
            if (headers.CorrelationId is { } correlation)
            {
                activity.SetTag("messaging.message.conversation_id", correlation.ToString());
            }

            if (headers.Sender is { } sender)
            {
                activity.SetTag("typewright.sender", sender);
            }
        }

        try
        {
            await next(context);
        }
        catch (Exception exception) when (activity is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
            throw;
        }
    }
}

/// <summary>
/// Records <c>typewright.agent.messages.processed</c>, <c>typewright.agent.messages.failed</c>,
/// and <c>typewright.agent.message.duration</c> on <see cref="AgentDiagnostics.MeterName"/>,
/// tagged with the agent name and message type. Costs nothing without a listener.
/// </summary>
public sealed class MetricsInterceptor<TMessage> : IMessageInterceptor<TMessage>
{
    /// <inheritdoc />
    public ValueTask InvokeAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next)
        => AgentDiagnostics.Processed.Enabled || AgentDiagnostics.Failed.Enabled || AgentDiagnostics.Duration.Enabled
            ? MeasureAsync(context, next)
            : next(context);

    private static async ValueTask MeasureAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next)
    {
        var tags = new TagList
        {
            { "typewright.agent", context.AgentName },
            { "typewright.message.type", context.Envelope.Message!.GetType().Name },
        };
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
            AgentDiagnostics.Processed.Add(1, tags);
        }
        catch
        {
            AgentDiagnostics.Failed.Add(1, tags);
            throw;
        }
        finally
        {
            AgentDiagnostics.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
        }
    }
}

/// <summary>
/// Bounds each message's handling time: the token passed down the pipeline also fires
/// after <c>timeout</c>, and a handler that stops because of it surfaces as a
/// <see cref="TimeoutException"/> (a failure, handled by the failure policy). Cooperative:
/// only handlers that observe their token can be timed out.
/// </summary>
public sealed class TimeoutInterceptor<TMessage> : IMessageInterceptor<TMessage>
{
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;

    /// <summary>Creates the interceptor.</summary>
    public TimeoutInterceptor(TimeSpan timeout, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask InvokeAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next)
    {
        using var timeout = new CancellationTokenSource(_timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, timeout.Token);
        try
        {
            await next(context with { CancellationToken = linked.Token });
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Handling {context.Envelope.Message!.GetType().Name} in agent '{context.AgentName}' exceeded {_timeout}.");
        }
    }
}
