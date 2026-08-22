using MeshKit.Web.Ingest;

namespace MeshKit.Web;

public static partial class MeshKitServiceCollectionExtensions
{
    public static IServiceCollection AddMeshKitIngest(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IngestOptions>(configuration.GetSection(IngestOptions.Section));
        services.AddScoped<PackImporter>();
        return services;
    }
}
