using Microsoft.AspNetCore.Diagnostics;

namespace MeshKit.Web;

public static class NotFoundPageMiddleware
{
    /// <summary>
    /// Renders <paramref name="path"/> for 404 responses to GET/HEAD requests only. The stock
    /// <c>UseStatusCodePagesWithReExecute</c> replays every error status, so a 401 from a POST endpoint
    /// came back as a POST to the not-found page (antiforgery 400) and a 401 from a GET came back as 404.
    /// </summary>
    public static IApplicationBuilder UseNotFoundPage(this IApplicationBuilder app, string path = "/not-found")
    {
        return app.UseStatusCodePages(async statusContext =>
        {
            var http = statusContext.HttpContext;
            var safeMethod = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
            if (http.Response.StatusCode != StatusCodes.Status404NotFound || !safeMethod)
            {
                return;
            }

            var originalPath = http.Request.Path;
            var originalQuery = http.Request.QueryString;
            var originalServices = http.RequestServices;
            http.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
            {
                OriginalPath = originalPath.Value ?? string.Empty,
                OriginalQueryString = originalQuery.Value,
            });
            http.Request.Path = path;
            http.Request.QueryString = QueryString.Empty;

            // Blazor's scoped services (auth state, DbContext) must not leak from the failed request.
            await using var scope = originalServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            http.RequestServices = scope.ServiceProvider;
            try
            {
                await statusContext.Next(http);
            }
            finally
            {
                http.RequestServices = originalServices;
                http.Request.Path = originalPath;
                http.Request.QueryString = originalQuery;
                http.Features.Set<IStatusCodeReExecuteFeature?>(null);
            }
        });
    }
}
