using Microsoft.AspNetCore.Components;

namespace MeshKit.Web.Identity;

/// <summary>
/// Redirect helper for static-SSR Identity pages. With <c>BlazorDisableThrowNavigationException</c>
/// on, <see cref="NavigationManager.NavigateTo(string, bool)"/> returns normally and the response becomes
/// a 302, so callers must <c>return</c> right after calling these.
/// </summary>
public sealed class IdentityRedirectManager(NavigationManager navigationManager, IHttpContextAccessor httpContextAccessor)
{
    public const string StatusCookieName = "meshkit.status";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    /// <summary>Redirects to a relative URI; absolute URIs are reduced to their base-relative path (no open redirect).</summary>
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        navigationManager.NavigateTo(uri);
    }

    public void RedirectToWithStatus(string uri, string message)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }
}
