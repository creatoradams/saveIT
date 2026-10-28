using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;

namespace SaveIT.Api.Security;

public sealed class TenantFilter(SaveItDb db, TenantScope scope) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (!Guid.TryParse(user.FindFirstValue("tenant"), out var tenant) || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var membership))
            return Results.Unauthorized();
        scope.TenantId = tenant;
        scope.MembershipId = membership;
        scope.Subject = user.FindFirstValue("subject") ?? "";
        await using var transaction = await db.Database.BeginTransactionAsync(context.HttpContext.RequestAborted);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {tenant.ToString()}, true)");
        if (!await db.Memberships.AnyAsync(x => x.Id == membership && x.Subject == scope.Subject && x.Active))
            return Results.Unauthorized();
        var result = await next(context);
        if (result is not IStatusCodeHttpResult status || status.StatusCode is null or < 400)
            await transaction.CommitAsync(context.HttpContext.RequestAborted);
        return result;
    }
}

public static class Permissions
{
    public static async Task<string?> RoleAsync(SaveItDb db, TenantScope scope, Guid organizationId) =>
        await db.OrganizationGrants.Where(x => x.MembershipId == scope.MembershipId && x.OrganizationId == organizationId)
            .Select(x => x.Role).SingleOrDefaultAsync();

    public static IResult Hidden() => Results.NotFound();
    public static IResult ReadOnly() => Results.Problem(statusCode: 403, title: "This organization is read only for your account.");
}
