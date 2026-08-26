using System.Security.Claims;
using System.Text;
using Crm.Api.Common.Security;
using Crm.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crm.Api.Configuration;

/// <summary>
/// Authentication for the two caller populations (spec FR-023).
///
/// Staff federate to the corporate identity provider; external portal users hold CRM-issued
/// tokens. Both are JWT bearer schemes, selected per request by issuer, and both converge on the
/// same <see cref="ICurrentUser"/> - so there is exactly one authorization path, not two.
///
/// Both schemes are always registered and configured lazily from <see cref="AuthOptions"/>. That
/// matters: reading configuration while services are being registered would miss anything layered
/// in afterwards, which is exactly how a deployment override or a test host supplies settings.
///
/// This feature delivers the seams only - no token is issued here and no provider is configured.
/// </summary>
public static class AuthenticationSetup
{
    public const string StaffScheme = "Staff";
    public const string PortalScheme = "Portal";
    public const string SelectorScheme = "CrmBearer";

    public static IServiceCollection AddCrmAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, SelectorScheme, options =>
            {
                options.ForwardDefaultSelector = SelectScheme;
            })
            .AddJwtBearer(StaffScheme, _ => { })
            .AddJwtBearer(PortalScheme, _ => { });

        services
            .AddOptions<JwtBearerOptions>(StaffScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                var staff = auth.Value.Staff;

                jwt.Authority = staff.Authority;
                jwt.Audience = staff.Audience;
                jwt.MapInboundClaims = false;
                jwt.RequireHttpsMetadata = true;
                jwt.TokenValidationParameters = BuildValidationParameters(
                    staff.Issuer,
                    staff.Audience,
                    staff.SigningKey);
                jwt.Events = StampPopulation(CallerPopulation.Staff);
            });

        services
            .AddOptions<JwtBearerOptions>(PortalScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                var portal = auth.Value.Portal;

                jwt.Audience = portal.Audience;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = BuildValidationParameters(
                    portal.Issuer,
                    portal.Audience,
                    portal.SigningKey);
                jwt.Events = StampPopulation(CallerPopulation.Portal);
            });

        return services;
    }

    /// <summary>
    /// Routes a request to the scheme that owns its issuer. The token is read here, not trusted:
    /// the selected scheme still validates signature, issuer, audience, and lifetime.
    /// </summary>
    private static string SelectScheme(HttpContext context)
    {
        var auth = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        var issuer = ReadIssuerWithoutValidating(context);

        if (issuer is not null &&
            !string.IsNullOrWhiteSpace(auth.Portal.Issuer) &&
            string.Equals(issuer, auth.Portal.Issuer, StringComparison.Ordinal))
        {
            return PortalScheme;
        }

        // Anything else is treated as a staff token. A disabled or misconfigured scheme simply
        // fails validation, which is a 401 - never an unhandled failure.
        return StaffScheme;
    }

    private static string? ReadIssuerWithoutValidating(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header["Bearer ".Length..].Trim();

        try
        {
            return new JsonWebTokenHandler().ReadJsonWebToken(token).Issuer;
        }
        catch (ArgumentException)
        {
            // A malformed token is not a routing decision; the scheme rejects it properly.
            return null;
        }
    }

    private static TokenValidationParameters BuildValidationParameters(
        string? issuer,
        string? audience,
        string? signingKey)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.NameIdentifier,
        };

        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            parameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            parameters.ValidateIssuerSigningKey = true;
        }

        return parameters;
    }

    /// <summary>
    /// Stamps the population from the scheme that authenticated the request. It is never read from
    /// the token, so a portal token cannot present itself as staff.
    /// </summary>
    private static JwtBearerEvents StampPopulation(CallerPopulation population) => new()
    {
        OnTokenValidated = context =>
        {
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(CrmClaims.Population, population.ToString()));
            }

            return Task.CompletedTask;
        },
    };
}
