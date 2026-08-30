namespace Crm.Api.Organization;

/// <summary>
/// Which language the caller is reading in, so a list can sort by the name they actually see
/// (spec LR-002).
/// </summary>
/// <remarks>
/// Sorting is done in the database rather than in memory, so it composes with paging - ordering a
/// single page after the fact would produce a list that is sorted within each page and unsorted
/// across them. That means the language has to reach the query, which is why it is read here rather
/// than left to the client.
/// </remarks>
internal static class RequestLanguage
{
    private const string Default = "en";

    /// <summary>
    /// Reads the preferred language from the standard <c>Accept-Language</c> header, falling back to
    /// English. Only the primary subtag matters: <c>ar-SA</c> and <c>ar</c> sort identically.
    /// </summary>
    internal static string Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = context.Request.Headers.AcceptLanguage.ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            return Default;
        }

        // "ar-SA,ar;q=0.9,en;q=0.8" - the first entry is the preference; quality ordering beyond
        // that is more precision than a two-language product can use.
        var first = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(first))
        {
            return Default;
        }

        var tag = first.Split(';', StringSplitOptions.TrimEntries)[0];

        return tag.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : Default;
    }
}
