using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SpecFleet.Api.Features.Health;

/// <summary>
/// Liveness transport used by deployment readiness checks and integration harnesses.
/// Route definitions live in the feature slice, not in the host.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        return endpoints;
    }
}
