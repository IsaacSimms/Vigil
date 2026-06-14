// == Confidence Value Object (from Diagram 3 + SystemsDesign §4: 0-1 invariant enforced at construction) == //

namespace Vigil.Domain.ValueObjects;

/// <summary>
/// Value object representing bounded confidence [0,1]. Enforces invariant at construction (out-of-range rejected, never silently clamped).
/// Used in CandidateCause. Immutable record for value equality. CompareTo supports ranking.
/// </summary>
public sealed record Confidence : IComparable<Confidence>
{
    public double Value { get; } // The confidence score; invariant: 0 <= Value <= 1

    public Confidence(double value)
    {
        // Enforce the 0-1 boundary here (the single place per design). 
        // This is the "floor" that makes downstream validation and rendering trustworthy.
        if (value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value), "Confidence must be between 0 and 1.");
        Value = value;
    }

    public int CompareTo(Confidence? other) // Supports ordering in lists and TopCause selection
    {
        if (other is null) return 1;
        return Value.CompareTo(other.Value);
    }

    public static implicit operator double(Confidence c) => c.Value; // Convenience for any legacy numeric use
}

public static class ConfidenceExtensions
{
    public static Confidence ToConfidence(this double value) => new(value); // Fluent construction helper
}
