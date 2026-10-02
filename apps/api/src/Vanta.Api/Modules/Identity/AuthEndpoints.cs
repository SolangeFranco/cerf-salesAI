using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Modules.Tenants;

namespace Vanta.Api.Modules.Identity;

public sealed record RegisterRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string TenantName,
    [property: Required, EmailAddress, StringLength(200)] string Email,
    [property: Required, MinLength(8), StringLength(128)] string Password);

public sealed record LoginRequest(
    [property: Required, EmailAddress, StringLength(200)] string Email,
    [property: Required, StringLength(128)] string Password);

public sealed record AuthResponse(string AccessToken, Guid TenantId);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        return group;
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem, Conflict<string>>> RegisterAsync(
        RegisterRequest request,
        UserManager<VantaUser> userManager,
        Data.VantaDbContext db,
        JwtTokenService tokenService,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var slug = TenantSlugGenerator.FromName(request.TenantName);

        if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            return TypedResults.Conflict("A tenant with a similar name already exists.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.TenantName,
            Slug = slug,
            AiResponsesEnabled = environment.EnvironmentName == "Testing",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Tenants.Add(tenant);

        var user = new VantaUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenant.Id,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.ToDictionary(e => e.Code, e => new[] { e.Description });
            return TypedResults.ValidationProblem(errors);
        }

        await db.SaveChangesAsync(cancellationToken);

        var token = tokenService.CreateToken(user, tenant.Id);
        return TypedResults.Ok(new AuthResponse(token, tenant.Id));
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<VantaUser> userManager,
        JwtTokenService tokenService)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return TypedResults.Unauthorized();
        }

        var token = tokenService.CreateToken(user, user.TenantId);
        return TypedResults.Ok(new AuthResponse(token, user.TenantId));
    }
}
