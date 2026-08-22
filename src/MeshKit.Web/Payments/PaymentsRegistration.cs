using MeshKit.Web.Payments;
using Microsoft.Extensions.Options;
using Stripe;

namespace MeshKit.Web;

public static partial class MeshKitServiceCollectionExtensions
{
    public static IServiceCollection AddMeshKitPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MeshKitOptions>(configuration.GetSection(MeshKitOptions.Section));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.Section));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
            // An instance, never the static StripeConfiguration.ApiKey. An empty key still builds: the
            // checkout endpoint answers 503 before any call when Stripe is not configured.
            return new StripeClient(string.IsNullOrWhiteSpace(options.SecretKey) ? "sk_unconfigured" : options.SecretKey);
        });
        services.AddScoped<ICheckoutSessionGateway, StripeCheckoutGateway>();
        services.AddScoped<PackCheckoutService>();
        services.AddScoped<FulfillmentService>();
        return services;
    }
}
