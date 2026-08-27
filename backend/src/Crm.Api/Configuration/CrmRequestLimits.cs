namespace Crm.Api.Configuration;

/// <summary>
/// The request limits from spec FR-055, in one place so the API and its hosts agree.
/// An endpoint may lower a limit for its own payload; raising one is a reviewed exception.
/// </summary>
public static class CrmRequestLimits
{
    /// <summary>Maximum request body size: 10 MB.</summary>
    public const long MaxBodyBytes = 10L * 1024 * 1024;

    /// <summary>Maximum JSON nesting depth.</summary>
    public const int MaxJsonDepth = 32;
}
