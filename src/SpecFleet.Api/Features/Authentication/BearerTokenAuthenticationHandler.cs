using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpecFleet.Api.Features.Authentication;

public sealed class BearerTokenAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "SpecFleetBearer";

    /// <summary>
    /// Maps opaque bearer tokens to the authenticated user id. Tokens are supplied by
    /// deployment configuration; the API never invents an identity for an unknown token.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tokens { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Thin authentication transport. It resolves a configured bearer token to a user id and
/// performs no application logic.
/// </summary>
public sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<BearerTokenAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<BearerTokenAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var header = values.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header[prefix.Length..].Trim();
        if (token.Length == 0 || !Options.Tokens.TryGetValue(token, out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer token is not valid."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Name, userId)],
            BearerTokenAuthenticationOptions.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, BearerTokenAuthenticationOptions.SchemeName)));
    }
}
