using Crm.Api.Common.Errors;
using Crm.Application.Common;

namespace Crm.Api.Configuration;

/// <summary>
/// Answers a request for an API version that does not exist with the shared error contract
/// (spec FR-016).
///
/// Without this, an unknown version simply fails to match a route and the caller gets a bare
/// "not found" - which says nothing about the real problem and sends an integrator hunting for a
/// missing endpoint rather than fixing the version segment.
/// </summary>
public static class ApiVersionGuard
{
    private const string ApiPrefix = "/api/";

    public static IApplicationBuilder UseCrmApiVersionGuard(
        this IApplicationBuilder app,
        params string[] supportedVersions)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(supportedVersions);

        var supported = new HashSet<string>(supportedVersions, StringComparer.OrdinalIgnoreCase);

        return app.Use(async (context, next) =>
        {
            var version = ExtractVersionSegment(context.Request.Path.Value);

            if (version is not null && !supported.Contains(version))
            {
                await ErrorContractSetup.WriteProblemAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.UnsupportedApiVersion,
                    $"API version '{version}' is not supported. Supported versions: "
                        + string.Join(", ", supported.Order(StringComparer.Ordinal)) + ".");

                return;
            }

            await next();
        });
    }

    /// <summary>Returns the version segment (for example <c>v9</c>) of an /api/ path.</summary>
    private static string? ExtractVersionSegment(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = path[ApiPrefix.Length..];
        var end = remainder.IndexOf('/', StringComparison.Ordinal);
        var segment = end < 0 ? remainder : remainder[..end];

        return segment.Length > 1 && (segment[0] is 'v' or 'V') ? segment : null;
    }
}
