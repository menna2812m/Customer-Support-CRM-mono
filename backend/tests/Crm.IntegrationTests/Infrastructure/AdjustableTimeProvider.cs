namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// A clock a test can push forward.
///
/// Session limits are measured in hours, which is the right choice for people and the wrong one
/// for a test suite: waiting out an inactivity window is not an option, and lowering the limit to
/// seconds would test a configuration nobody deploys. Moving the clock instead exercises the real
/// limits with the real settings.
///
/// Built on the system clock rather than a fixed instant so that rows written before and after an
/// advance still order correctly against database defaults.
/// </summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;

    /// <summary>Moves every subsequent reading forward. Never backwards - time does not go back.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        _offset += by;
    }
}
