using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Typewright.Agents;

/// <summary>One message moving through an agent's interceptor pipeline.</summary>
/// <param name="Envelope">The message and its delivery metadata.</param>
/// <param name="AgentName">The processing agent's name.</param>
/// <param name="CancellationToken">
/// Cancelled when the agent aborts. Interceptors may pass a derived token to
/// <c>next</c> (for example, one that also fires on a timeout).
/// </param>
public readonly record struct MessageContext<TMessage>(
    Envelope<TMessage> Envelope,
    string AgentName,
    CancellationToken CancellationToken);

/// <summary>The rest of the pipeline, ending in the handler.</summary>
public delegate ValueTask MessageDelegate<TMessage>(MessageContext<TMessage> context);

/// <summary>
/// Wraps message handling, ASP.NET-middleware style: logging, metrics, tracing, timeouts,
/// validation. Composed once per agent; an interceptor that does not call
/// <c>next</c> short-circuits the message, and the agent's state is unchanged (it should
/// then settle a request's reply channel, for example with <c>TryReject</c>, or the caller
/// waits until it cancels). Interceptors run on the agent loop: do not use
/// <c>ConfigureAwait(false)</c>, or the rest of the pipeline leaves the dispatcher's context.
/// </summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public interface IMessageInterceptor<TMessage>
{
    /// <summary>Processes <paramref name="context"/>, calling <paramref name="next"/> to continue the pipeline.</summary>
    ValueTask InvokeAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next);
}

/// <summary>
/// Names of the diagnostics sources agents publish to. Subscribe with an
/// <c>ActivityListener</c>/<c>MeterListener</c>, or OpenTelemetry's
/// <c>AddSource(AgentDiagnostics.ActivitySourceName)</c> and <c>AddMeter(AgentDiagnostics.MeterName)</c>.
/// </summary>
public static class AgentDiagnostics
{
    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name used by <c>TracingInterceptor</c>.</summary>
    public const string ActivitySourceName = "Typewright.Agents";

    /// <summary>The <see cref="System.Diagnostics.Metrics.Meter"/> name for agent metrics.</summary>
    public const string MeterName = "Typewright.Agents";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> Skipped = Meter.CreateCounter<long>(
        "typewright.agent.messages.skipped",
        unit: "{message}",
        description: "Requests skipped at dequeue because their caller had already cancelled.");

    internal static readonly Counter<long> Processed = Meter.CreateCounter<long>(
        "typewright.agent.messages.processed",
        unit: "{message}",
        description: "Messages handled successfully.");

    internal static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "typewright.agent.messages.failed",
        unit: "{message}",
        description: "Messages whose handling threw.");

    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "typewright.agent.message.duration",
        unit: "ms",
        description: "Time spent handling a message.");

    internal static void RecordSkipped(string agentName)
    {
        if (Skipped.Enabled)
        {
            Skipped.Add(1, new KeyValuePair<string, object?>("typewright.agent", agentName));
        }
    }
}
