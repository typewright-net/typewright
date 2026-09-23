using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Typewright.Actors;
using Typewright.Agents;
using Xunit;

namespace Typewright.Actors.Tests;

public class HostingTests
{
    public sealed class Greeter
    {
        public string Greet(string name) => $"Hello, {name}";
    }

    public sealed record Greet(string Name) : IRequest<string>;

    /// <summary>A handler built from the container, also taking its actor context.</summary>
    public sealed class GreeterHandler(Greeter greeter, ActorContext<Greet> context, StopLog stops)
        : IMessageHandler<int, Greet>, IAgentLifecycle<int>
    {
        public ValueTask<int> HandleAsync(int state, Envelope<Greet> envelope, CancellationToken cancellationToken)
        {
            envelope.ReplyTo.TryReply($"{greeter.Greet(envelope.Message.Name)} from {context.Path}");
            return new(state + 1);
        }

        public ValueTask OnStoppedAsync(int finalState, CancellationToken cancellationToken)
        {
            stops.Stopped.TrySetResult(finalState);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class StopLog
    {
        public TaskCompletionSource<int> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Fact]
    public async Task Actors_resolve_services_and_stop_with_the_host()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Greeter>();
        builder.Services.AddSingleton<StopLog>();
        builder.Services.AddActorSystem(options => options.EnableMetrics = false);
        using var host = builder.Build();
        await host.StartAsync();

        var system = host.Services.GetRequiredService<IActorSystem>();
        Assert.Same(host.Services.GetRequiredService<ActorSystem>(), system);
        var greeter = system.Spawn(
            ActorPath.Create("/greeter"),
            ActorDefinition.FromServices<int, Greet, GreeterHandler>(_ => 0));

        Assert.Equal("Hello, Ada from /greeter", await greeter.AskAsync(new Greet("Ada")).Within());

        await host.StopAsync();

        Assert.Equal(1, await host.Services.GetRequiredService<StopLog>().Stopped.Task.Within());
        Assert.Empty(system.Actors);
    }
}
