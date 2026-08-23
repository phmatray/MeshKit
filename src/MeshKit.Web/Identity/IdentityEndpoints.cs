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

        // Email confirmation link target. Informational only: nothing is gated on a confirmed email.
        app.MapGet("/account/confirm-email", async (UserManager<ApplicationUser> users, string? userId, string? code) =>
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            {
                return Results.Redirect("/account/login");
            }

            var user = await users.FindByIdAsync(userId);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            string token;
            try
            {
                token = System.Text.Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return Results.Redirect("/account/login");
            }

            var result = await users.ConfirmEmailAsync(user, token);
            return Results.Redirect(result.Succeeded ? "/account/login?confirmed=1" : "/account/login?confirmed=0");
        });

        return app;
    }

    /// <summary>Identity tokens contain characters that break URLs; Base64Url them.</summary>
    public static string EncodeToken(string token) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

    public static string? DecodeToken(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
