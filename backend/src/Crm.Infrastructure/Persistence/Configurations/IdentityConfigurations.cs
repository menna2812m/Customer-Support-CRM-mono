using Crm.Application.Authorization;
using Crm.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence conventions for the identity tables. Discovered automatically by
/// <see cref="CrmDbContext"/>, as feature 001 established.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("User");
        builder.Property(user => user.ProviderSubject).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();

        // The subject is how a returning person is recognised; the email uniqueness is what turns a
        // reissued address into a refusal rather than a silent merge (spec FR-005).
        builder.HasIndex(user => user.ProviderSubject).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.IsActive);

        // Placement deliberately carries no foreign key: the organization feature owns those tables
        // and does not exist yet. Recorded in the plan's Complexity Tracking.
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Role");
        builder.Property(role => role.Name).HasMaxLength(100).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(500);
        builder.HasIndex(role => role.Name).IsUnique();

        // Seeded by migration, because this feature ships no screen for editing them (spec FR-022).
        // Identifiers are fixed rather than generated so the seed is idempotent across environments.
        builder.HasData(
            new
            {
                Id = IdentitySeed.AdministratorRoleId,
                Name = "Administrator",
                Description = "Every permission in the catalog.",
                IsSystem = true,
                CreatedAt = IdentitySeed.SeededAt,
                CreatedBy = (Guid?)null,
                UpdatedAt = (DateTimeOffset?)null,
                UpdatedBy = (Guid?)null,
            },
            new
            {
                Id = IdentitySeed.AgentRoleId,
                Name = "Agent",
                Description = "Day-to-day customer and ticket work.",
                IsSystem = true,
                CreatedAt = IdentitySeed.SeededAt,
                CreatedBy = (Guid?)null,
                UpdatedAt = (DateTimeOffset?)null,
                UpdatedBy = (Guid?)null,
            },
            new
            {
                Id = IdentitySeed.ReadOnlyRoleId,
                Name = "ReadOnly",
                Description = "Read access for oversight and reporting.",
                IsSystem = true,
                CreatedAt = IdentitySeed.SeededAt,
                CreatedBy = (Guid?)null,
                UpdatedAt = (DateTimeOffset?)null,
                UpdatedBy = (Guid?)null,
            });
    }
}

/// <summary>
/// Fixed identifiers and timestamps for the seeded roles. Constants rather than generated values,
/// so re-running a migration produces the same rows instead of duplicates.
/// </summary>
public static class IdentitySeed
{
    public static readonly Guid AdministratorRoleId = Guid.Parse("0195f1a0-0000-7000-8000-000000000001");
    public static readonly Guid AgentRoleId = Guid.Parse("0195f1a0-0000-7000-8000-000000000002");
    public static readonly Guid ReadOnlyRoleId = Guid.Parse("0195f1a0-0000-7000-8000-000000000003");

    public static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Permissions granted to the agent role.</summary>
    public static readonly string[] AgentPermissions =
    [
        Permissions.Customers.View,
        Permissions.Customers.Create,
        Permissions.Customers.Update,
        Permissions.Tickets.View,
        Permissions.Tickets.Create,
    ];

    /// <summary>Permissions granted to the read-only role.</summary>
    public static readonly string[] ReadOnlyPermissions =
    [
        Permissions.Customers.View,
        Permissions.Tickets.View,
        Permissions.Reports.View,
    ];
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RolePermission");
        builder.HasKey(grant => new { grant.RoleId, grant.Permission });
        builder.Property(grant => grant.Permission).HasMaxLength(100).IsRequired();

        builder
            .HasOne<Role>()
            .WithMany()
            .HasForeignKey(grant => grant.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // The administrator role is resolved from the catalog rather than a written list, so a
        // permission added later is not silently missing from it. The cost is deliberate: adding a
        // permission changes the model and therefore requires a migration, which is the right place
        // for a change to who can do what.
        var administrator = Permissions.All.Select(permission => new
        {
            RoleId = IdentitySeed.AdministratorRoleId,
            Permission = permission,
        });

        var agent = IdentitySeed.AgentPermissions.Select(permission => new
        {
            RoleId = IdentitySeed.AgentRoleId,
            Permission = permission,
        });

        var readOnly = IdentitySeed.ReadOnlyPermissions.Select(permission => new
        {
            RoleId = IdentitySeed.ReadOnlyRoleId,
            Permission = permission,
        });

        builder.HasData([.. administrator, .. agent, .. readOnly]);
    }
}

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleAssignment");
        builder.HasKey(assignment => new { assignment.UserId, assignment.RoleId });

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<Role>()
            .WithMany()
            .HasForeignKey(assignment => assignment.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Session");
        builder.Property(session => session.RevokedReason).HasMaxLength(100);
        builder.Property(session => session.ClientDescription).HasMaxLength(200);
        builder.Property(session => session.IpAddressAtCreation).HasMaxLength(45);

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(session => session.UserId);

        // "This user's live sessions" is the query sign-out-everywhere and deactivation both run.
        builder.HasIndex(session => session.RevokedAt).HasFilter("[RevokedAt] IS NULL");
    }
}

public sealed class RenewalCredentialConfiguration : IEntityTypeConfiguration<RenewalCredential>
{
    public void Configure(EntityTypeBuilder<RenewalCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RenewalCredential");
        builder.Property(credential => credential.TokenHash).HasMaxLength(200).IsRequired();

        builder
            .HasOne<Session>()
            .WithMany()
            .HasForeignKey(credential => credential.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Renewal looks the credential up by hash on every call, so the hash carries the index.
        builder.HasIndex(credential => credential.TokenHash).IsUnique();
        builder.HasIndex(credential => credential.SessionId);
    }
}

public sealed class AuthenticationEventConfiguration : IEntityTypeConfiguration<AuthenticationEvent>
{
    public void Configure(EntityTypeBuilder<AuthenticationEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuthenticationEvent");
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.Outcome).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.SubjectReference).HasMaxLength(200);
        builder.Property(entry => entry.IpAddress).HasMaxLength(45);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.Detail).HasMaxLength(500);

        builder.HasIndex(entry => entry.OccurredAt);
        builder.HasIndex(entry => entry.UserId);
        builder.HasIndex(entry => entry.Action);
    }
}
