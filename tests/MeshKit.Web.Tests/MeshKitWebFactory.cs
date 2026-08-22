using System.Security.Claims;
using System.Text.Encodings.Web;
using MeshKit.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Tests;

/// <summary>
/// Hosts the real app against a temp catalog directory and a temp SQLite file. Requests carrying
/// <c>X-Test-User: &lt;id&gt;</c> are authenticated as that user (test scheme replaces the cookie scheme).
/// </summary>
public sealed class MeshKitWebFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "whsec_test_secret";
    public const string IngestToken = "ingest-token-for-tests";

    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("meshkit-web");

    public string CatalogPath => Path.Combine(Root.FullName, "catalog");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(CatalogPath);
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:AppDb", $"Data Source={Path.Combine(Root.FullName, "test.db")}");
        builder.UseSetting("MeshKit:Catalog:Path", CatalogPath);
        builder.UseSetting("MeshKit:PublicBaseUrl", "http://localhost");
        builder.UseSetting("MeshKit:Ingest:Token", IngestToken);
        builder.UseSetting("Stripe:SecretKey", "rk_test_unused");
        builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateClientAs(string? userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, userId);
        }

        return client;
    }

    public T WithDb<T>(Func<ApplicationDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Root.Delete(recursive: true);
            }
            catch (IOException)
            {
                // SQLite may still hold the file for a moment; a leaked temp dir is not worth failing a test.
            }
        }
    }
}

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var user) || string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user!), new Claim(ClaimTypes.Name, $"{user}@example.com")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
