using System.Security.Claims;
using System.Text;
using Crm.Api.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Crm.Api.Configuration;

/// <summary>
/// Validates the credentials the CRM itself issues (spec FR-010, research decision 1).
///
/// Feature 001 registered two bearer schemes on the assumption that staff would present tokens
/// minted by the corporate identity provider. Feature 002 changed that: the provider authenticates
/// a person once, at sign-in, and the API then issues its own credential. A provider token cannot
/// be revoked by us, cannot be rotated single-use, and cannot carry CRM permissions - all three are
/// required, so the CRM became the issuer.
///
/// One consequence is worth stating plainly. In feature 001 a caller's population could not be
/// forged because the authenticating scheme stamped it. It still cannot be forged, for a different
/// reason: the claim is inside a credential this application signed, and nothing from an inbound
/// provider token is ever copied into it.
///
/// The portal scheme is gone from this file rather than left inert. The portal feature will issue
/// its own credentials through the same issuer, or register a scheme of its own if it needs
/// different keys; a disabled duplicate of this configuration would only rot.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>The scheme that validates CRM-issued access credentials.</summary>
    public const string CrmScheme = "CrmBearer";

    public static IServiceCollection AddCrmAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(CrmScheme).AddJwtBearer(CrmScheme, _ => { });

        services
            .AddOptions<JwtBearerOptions>(CrmScheme)
            .Configure<IOptions<TokenOptions>>((jwt, tokenOptions) =>
            {
                var token = tokenOptions.Value;

                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = token.Issuer,
                    ValidateAudience = true,
                    ValidAudience = token.Audience,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = CrmClaims.Permission,
                    IssuerSigningKey = string.IsNullOrWhiteSpace(token.SigningKey)
                        ? null
                        : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(token.SigningKey)),
                };

                // A revoked session must stop working before its credential expires, so the
                // revocation check runs on every validated request rather than only at renewal.
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = static async context =>
                    {
                        var sessions = context.HttpContext.RequestServices
                            .GetRequiredService<Crm.Application.Abstractions.ISessionStore>();

                        var sessionId = context.Principal?.FindFirstValue(CrmClaims.SessionId);

                        if (!Guid.TryParse(sessionId, out var id) || !await sessions.IsActiveAsync(id))
                        {
                            context.Fail("The session is no longer active.");
                        }
                    },
                };
            });

        return services;
    }
}
