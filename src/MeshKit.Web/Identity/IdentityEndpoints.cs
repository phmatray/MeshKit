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

        // One-click opt-out from release emails: the token is a data-protected user id, so no login is needed
        // and a forged link can do nothing but fail.
        app.MapGet("/account/notifications/unsubscribe", async (Notifications.ReleaseAnnouncer announcer, UserManager<ApplicationUser> users, string? token) =>
        {
            var userId = announcer.UserIdFromUnsubscribeToken(token);
            var user = userId is null ? null : await users.FindByIdAsync(userId);
            if (user is null)
            {
                return Results.Redirect("/account/unsubscribed?ok=0");
            }

            user.NewReleaseOptIn = false;
            user.NewReleaseOptInAt = null;
            await users.UpdateAsync(user);
            return Results.Redirect("/account/unsubscribed?ok=1");
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
