using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Typewright.Actors;

/// <summary>Registration of the actor system with dependency injection and the host.</summary>
public static class ActorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ActorSystem"/> (also as <see cref="IActorSystem"/>)
    /// whose actors resolve services from the application's container, and a hosted
    /// service that stops every actor gracefully when the host shuts down.
    /// </summary>
    public static IServiceCollection AddActorSystem(this IServiceCollection services, Action<ActorSystemOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new ActorSystemOptions();
        configure?.Invoke(options);

        services.AddSingleton(provider => new ActorSystem(options, provider, provider.GetService<ILogger<ActorSystem>>()));
        services.AddSingleton<IActorSystem>(provider => provider.GetRequiredService<ActorSystem>());
        services.AddHostedService<ActorSystemHostedService>();
        return services;
    }
}

/// <summary>Stops the actor system when the host stops; the host's shutdown token bounds the drain.</summary>
internal sealed class ActorSystemHostedService(ActorSystem system) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => system.StopAsync(cancellationToken);
}
