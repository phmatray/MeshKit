using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Admin;

public static class MeshKitPolicies
{
    /// <summary>The store owner(s): <c>/admin</c> and nothing else. Matched by email against <see cref="MeshKitOptions.OwnerEmails"/>.</summary>
    public const string Owner = "Owner";
}

public sealed class OwnerRequirement : IAuthorizationRequirement;

/// <summary>Email in the configured owner list. An empty list means nobody, deliberately — there is no default owner.</summary>
public sealed class OwnerHandler(IOptions<MeshKitOptions> options) : AuthorizationHandler<OwnerRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OwnerRequirement requirement)
    {
        var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;
        if (email is not null && options.Value.OwnerEmailSet.Contains(email))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
