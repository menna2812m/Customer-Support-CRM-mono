using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Application.Identity.Claiming;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// Spec AR-008 and Constitution XI: a refused claim and a collision are recorded, and what is
/// recorded is the address and nothing else.
/// </summary>
/// <remarks>
/// The second half is the half that needs a test. "Record enough to investigate" and "record no
/// more than the address" pull in opposite directions, and the failure mode of the first one
/// winning is a token or a claim dump sitting in an audit store for years. Asserting on the exact
/// set of metadata keys is what makes adding one a deliberate act.
/// </remarks>
public sealed class ClaimAuditTests
{
    private static readonly Guid Person = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task A_refused_claim_records_the_address_and_the_reason_and_nothing_else()
    {
        var recorder = new CapturingAuditRecorder();
        var audit = Create(recorder);

        await audit.RecordRefusedClaimAsync(Person, "noor@example.com", ErrorCodes.IdentityEmailNotVerified);

        var entry = recorder.Entries.ShouldHaveSingleItem();

        entry.Action.ShouldBe("identity.claim.refused");
        entry.TargetType.ShouldBe("User");
        entry.TargetId.ShouldBe(Person.ToString());

        // Anonymous: a sign-in is not somebody administering somebody else until it succeeds.
        entry.ActorId.ShouldBeNull();

        entry.Metadata.ShouldNotBeNull();
        entry.Metadata.Keys.Order(StringComparer.Ordinal).ShouldBe(["email", "reason"]);
        entry.Metadata["email"].ShouldBe("noor@example.com");
        entry.Metadata["reason"].ShouldBe("identity_email_not_verified");
    }

    [Fact]
    public async Task A_collision_records_the_address_and_the_code_and_nothing_else()
    {
        var recorder = new CapturingAuditRecorder();
        var audit = Create(recorder);

        await audit.RecordCollisionAsync(Person, "reissued@example.com");

        var entry = recorder.Entries.ShouldHaveSingleItem();

        entry.Action.ShouldBe("identity.claim.collision");
        entry.Metadata.ShouldNotBeNull();

        // No subject, no token, no provider payload. A person resolving this needs the address and
        // the record it collided with; anything more is a liability that never expires.
        entry.Metadata.Keys.Order(StringComparer.Ordinal).ShouldBe(["email", "reason"]);
        entry.Metadata["reason"].ShouldBe(ErrorCodes.IdentitySubjectCollision);
    }

    [Fact]
    public async Task An_ambiguous_claim_names_no_person_because_no_choice_was_made_between_them()
    {
        var recorder = new CapturingAuditRecorder();
        var audit = Create(recorder);

        await audit.RecordRefusedClaimAsync(null, "shared@example.com", ErrorCodes.IdentityEmailAmbiguous);

        var entry = recorder.Entries.ShouldHaveSingleItem();

        // Naming one of the matches would read as though the system had picked it, which is exactly
        // what it refused to do.
        entry.TargetId.ShouldBeNull();
        entry.TargetType.ShouldBeNull();
        entry.Metadata!["email"].ShouldBe("shared@example.com");
    }

    [Fact]
    public async Task A_claim_records_which_person_was_claimed()
    {
        var recorder = new CapturingAuditRecorder();
        var audit = Create(recorder);

        await audit.RecordClaimedAsync(Person, "noor@example.com");

        var entry = recorder.Entries.ShouldHaveSingleItem();

        entry.Action.ShouldBe("identity.person.claimed");
        entry.TargetId.ShouldBe(Person.ToString());

        // No reason, because nothing was refused. Only the address, which is what FR-020 asks be
        // recorded about a claim having happened at all.
        entry.Metadata!.Keys.ShouldBe(["email"]);
    }

    private static ClaimAudit Create(IAuditRecorder recorder) =>
        new(recorder, new FixedCorrelation(), TimeProvider.System);

    private sealed class CapturingAuditRecorder : IAuditRecorder
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);

            return Task.CompletedTask;
        }
    }

    private sealed class FixedCorrelation : ICorrelationAccessor
    {
        public string CorrelationId => "correlation-1";

        public string? IpAddress => null;
    }
}
