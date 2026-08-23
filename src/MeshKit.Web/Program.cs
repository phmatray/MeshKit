using MeshKit.Web;
using MeshKit.Web.Admin;
using MeshKit.Web.Catalog;
using MeshKit.Web.Components;
using MeshKit.Web.Data;
using MeshKit.Web.Downloads;
using MeshKit.Web.Identity;
using MeshKit.Web.Ingest;
using MeshKit.Web.Payments;
using MeshKit.Web.Search;
using MeshKit.Web.Seo;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<MeshKit.Web.Samples.SampleFollowUpService>();
builder.Services.AddHostedService<MeshKit.Web.Samples.SampleFollowUpWorker>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AppDb") ?? "Data Source=meshkit.db"));

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(MeshKitPolicies.Owner, policy => policy.RequireAuthenticatedUser().AddRequirements(new OwnerRequirement())));
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, OwnerHandler>();
builder.Services.AddScoped<AdminStatsReader>();
builder.Services.AddScoped<MeshKit.Web.Notifications.ReleaseAnnouncer>();
builder.Services.AddScoped<MeshKit.Web.Notifications.AccountDeleter>();
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        // Length over composition rules (NIST 800-63B); the Register page promises exactly this.
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/login";
    options.Cookie.Name = "meshkit.auth";
});
builder.Services.AddScoped<IdentityRedirectManager>();

builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection(CatalogOptions.Section));
builder.Services.AddSingleton<ICatalogService, CatalogService>();
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddScoped<IEntitlementReader, EntitlementReader>();

builder.Services.AddMeshKitPayments(builder.Configuration);
builder.Services.AddMeshKitIngest(builder.Configuration);
builder.Services.AddMeshKitEmail(builder.Configuration);

// Persist cookie-signing keys when a path is configured (the container mounts /app/data for this);
// without it every restart would log every buyer out.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseNotFoundPage();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapCatalogEndpoints();
app.MapIdentityEndpoints();
app.MapPaymentEndpoints();
app.MapDownloadEndpoints();
app.MapIngestEndpoints();
app.MapSearchEndpoints();
app.MapSeoEndpoints();
app.MapRazorComponents<App>();

app.Run();

/// <summary>Exposed so integration tests can host the app with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
