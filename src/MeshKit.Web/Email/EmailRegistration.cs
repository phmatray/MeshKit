using MeshKit.Web.Email;
using Microsoft.Extensions.Options;

namespace MeshKit.Web;

public static partial class MeshKitServiceCollectionExtensions
{
    public static IServiceCollection AddMeshKitEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueue>());
        services.AddSingleton<IEmailSender>(sp =>
            sp.GetRequiredService<IOptions<SmtpOptions>>().Value.IsConfigured
                ? new SmtpEmailSender(sp.GetRequiredService<IOptions<SmtpOptions>>())
                : new LoggingEmailSender(sp.GetRequiredService<ILogger<LoggingEmailSender>>()));
        services.AddHostedService<EmailWorker>();
        return services;
    }
}
