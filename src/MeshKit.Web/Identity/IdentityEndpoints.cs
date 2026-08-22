using MeshKit.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace MeshKit.Web.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        // Logout is a POST (antiforgery-protected form) so a crafted link cannot sign someone out.
        app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signIn, [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : $"/{returnUrl.TrimStart('/')}");
        }).RequireAuthorization();

        return app;
    }
}
