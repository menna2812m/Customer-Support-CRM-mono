namespace Crm.Application.Identity.People;

/// <summary>
/// The two rules that stop the product locking itself out (spec FR-028, FR-029).
/// </summary>
/// <remarks>
/// This is the only rule in the feature that no constraint can express. Uniqueness is enforced by
/// an index, the placement invariant by deriving rather than accepting - but "at least one row must
/// remain" has no schema equivalent, so the guarantee has to come from isolation instead.
///
/// The decision is separated from the facts deliberately. Counting administrators and acting on the
/// answer must happen inside one serializable transaction, or two administrators demoting each
/// other at the same instant each read a safe count and together produce an unsafe result - two
/// individually valid operations arriving at a system nobody can administer. The store owns that
/// transaction, because transactions live where the database does; this owns the rule, because
/// rules do not belong in infrastructure. Splitting them is what lets the rule be tested
/// exhaustively without a database and the isolation be tested once.
/// </remarks>
public static class AdministratorGuard
{
    /// <summary>
    /// Decides whether a change that could remove administrator access may proceed.
    /// </summary>
    /// <remarks>
    /// The order matters. When somebody is the last administrator acting on their own account both
    /// rules refuse, and "another administrator must make this change" is useless advice when there
    /// is no other administrator. The reason that is actually true is reported.
    /// </remarks>
    public static AdministratorGuardResult Check(AdministratorChange change)
    {
        if (change.TargetHoldsAdministrator && change.OtherActiveAdministrators == 0)
        {
            return AdministratorGuardResult.LastAdministrator;
        }

        // Not conditional on the role: deactivating or deleting your own account locks you out
        // whether or not you administer anything.
        if (change.ActorId == change.TargetId)
        {
            return AdministratorGuardResult.SelfDemotion;
        }

        return AdministratorGuardResult.Allowed;
    }
}

/// <summary>
/// What a change needs to know about itself before it is permitted.
/// </summary>
/// <param name="ActorId">Who is making the change.</param>
/// <param name="TargetId">Whose access is being reduced.</param>
/// <param name="TargetHoldsAdministrator">Whether the target currently holds the administrator role.</param>
/// <param name="OtherActiveAdministrators">
/// Active, non-deleted administrators <em>other than the target</em>. Counting them separately is
/// what makes the rule readable: zero means removing this person leaves none.
/// </param>
public readonly record struct AdministratorChange(
    Guid ActorId,
    Guid TargetId,
    bool TargetHoldsAdministrator,
    int OtherActiveAdministrators);

/// <summary>The guard's verdict, mapped to a refusal code by the caller.</summary>
public enum AdministratorGuardResult
{
    Allowed = 0,

    /// <summary>Maps to <c>identity_last_administrator</c>.</summary>
    LastAdministrator = 1,

    /// <summary>Maps to <c>identity_self_demotion</c>.</summary>
    SelfDemotion = 2,
}
