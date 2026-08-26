namespace Crm.Domain.Common;

/// <summary>
/// Base type for every persisted entity. Identity-based equality: two entities of the same
/// type with the same non-default identifier are the same entity, regardless of field values.
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct, IEquatable<TId>
{
    protected Entity(TId id) => Id = id;

    /// <summary>Primary key. Assigned once at creation and never reassigned.</summary>
    public TId Id { get; protected init; }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Transient entities (default id) are only equal by reference.
        if (Id.Equals(default) || other.Id.Equals(default))
        {
            return false;
        }

        return GetType() == other.GetType() && Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}

/// <summary>Convenience base for the default identifier type used across the CRM.</summary>
public abstract class Entity : Entity<Guid>
{
    protected Entity(Guid id)
        : base(id) { }

    /// <summary>
    /// Creates a sequential identifier. Sequential values keep clustered index inserts cheap
    /// compared with fully random ones.
    /// </summary>
    protected static Guid NewId() => Guid.CreateVersion7();
}
