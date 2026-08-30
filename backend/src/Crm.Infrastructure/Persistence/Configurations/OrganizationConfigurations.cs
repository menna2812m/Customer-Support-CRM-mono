using Crm.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence conventions for the organizational units (feature 003).
///
/// Every uniqueness index here is <b>filtered</b> on <c>[IsDeleted] = 0</c>. Soft-deleted rows stay
/// in the table forever, so a plain unique index would retire a code permanently the first time a
/// unit carrying it was deleted - which contradicts spec FR-006, where deleting and recreating is
/// the stated remedy for a mistyped code. Uniqueness is enforced by the database rather than by a
/// prior read, so two administrators creating the same code at the same moment produce a refusal
/// rather than two rows.
/// </summary>
internal static class OrganizationSchema
{
    internal const int NameLength = 200;
    internal const int CodeLength = 32;

    /// <summary>Excludes retired rows, so a deleted unit's code and name become available again.</summary>
    internal const string LiveRowsOnly = "[IsDeleted] = 0";

    /// <summary>The columns every unit shares, mapped identically wherever they appear.</summary>
    internal static void ConfigureSharedShape<T>(EntityTypeBuilder<T> builder)
        where T : OrganizationUnit
    {
        builder.Property(unit => unit.NameAr).HasMaxLength(NameLength).IsRequired();
        builder.Property(unit => unit.NameEn).HasMaxLength(NameLength).IsRequired();
        builder.Property(unit => unit.Code).HasMaxLength(CodeLength).IsRequired();

        // "The units that may still be chosen" is the query a placement chooser runs (FR-009).
        builder.HasIndex(unit => unit.IsActive);
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Branch");
        OrganizationSchema.ConfigureSharedShape(builder);

        builder.HasIndex(branch => branch.Code)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        // Branch names are unique among branches: two branches both shown as "Riyadh" would be
        // indistinguishable in a placement chooser. Each language is checked independently.
        builder.HasIndex(branch => branch.NameAr)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        builder.HasIndex(branch => branch.NameEn)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);
    }
}

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Department");
        OrganizationSchema.ConfigureSharedShape(builder);

        builder.HasIndex(department => department.Code)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        builder.HasIndex(department => department.NameAr)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        builder.HasIndex(department => department.NameEn)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        // A team joins a department by being created in it or moved to it, never by being added to
        // a collection, so the navigation is read through its backing field.
        builder.HasMany(department => department.Teams)
            .WithOne()
            .HasForeignKey(team => team.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(department => department.Teams)
            .HasField("_teams")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Team");
        OrganizationSchema.ConfigureSharedShape(builder);

        builder.Property(team => team.DepartmentId).IsRequired();

        // A team code identifies a team globally, so it is unique across all teams...
        builder.HasIndex(team => team.Code)
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        // ...but a team name is only ever read under its department, so "Tier 1" may exist under
        // several. The asymmetry with the code index is the clarified decision, not an oversight.
        builder.HasIndex(team => new { team.DepartmentId, team.NameAr })
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        builder.HasIndex(team => new { team.DepartmentId, team.NameEn })
            .IsUnique()
            .HasFilter(OrganizationSchema.LiveRowsOnly);

        // "The teams of this department" is the query the department screen runs on every view.
        builder.HasIndex(team => team.DepartmentId);
    }
}
