namespace MeshKit.Web;

public static partial class MeshKitServiceCollectionExtensions
{
    public static IServiceCollection AddMeshKitIngest(this IServiceCollection services, IConfiguration configuration) => services;
}

public static partial class MeshKitEndpointExtensions
{
    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app) => app;
}
