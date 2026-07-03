using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fogos.Worker.Queue;

/// <summary>
/// Wires the queue consumer side: one <see cref="StreamConsumerService"/> per configured stream, the
/// delayed-dispatch pump, and DI registration of every <c>IEventHandler&lt;TEvent&gt;</c> in the
/// Worker assembly (so Wave-2/3 handlers register just by existing).
/// </summary>
public static class QueueWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddQueueWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        var streams = configuration.GetSection($"{QueueOptions.SectionName}:Streams").Get<string[]>()
                      ?? ["default", "icnf"];

        foreach (var stream in streams)
        {
            var captured = stream;
            services.AddSingleton<IHostedService>(sp =>
                ActivatorUtilities.CreateInstance<StreamConsumerService>(sp, captured));
        }

        services.AddHostedService<DelayedDispatchPump>();
        return services;
    }

    /// <summary>Scans the Worker assembly for <c>IEventHandler&lt;TEvent&gt;</c> implementations and registers each.</summary>
    public static IServiceCollection AddEventHandlers(this IServiceCollection services)
    {
        var assembly = typeof(QueueWorkerServiceCollectionExtensions).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsClass: true, IsAbstract: false })
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                        services.AddScoped(iface, type);
                }
            }
        }

        return services;
    }
}
