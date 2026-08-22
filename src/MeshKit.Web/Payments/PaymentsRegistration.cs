namespace MeshKit.Web;

public static partial class MeshKitServiceCollectionExtensions
{
    public static IServiceCollection AddMeshKitPayments(this IServiceCollection services, IConfiguration configuration) => services;
}

public static partial class MeshKitEndpointExtensions
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app) => app;

    public static IEndpointRouteBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app) => app;
}
