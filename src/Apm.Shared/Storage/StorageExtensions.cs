using Microsoft.Extensions.DependencyInjection;

namespace Apm.Shared.Storage;

public static class StorageExtensions
{
    private static readonly InMemoryTelemetryStore SharedInstance = new();

    public static IServiceCollection AddSharedTelemetryStore(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetryStore>(SharedInstance);
        return services;
    }
}
