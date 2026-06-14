// == ResourceRef Value Object (from Diagram 3 + SystemsDesign §4) == //

namespace Vigil.Domain.ValueObjects;

/// <summary>
/// Identifies the subject of an incident (e.g. Kind="host", Identifier="web-01").
/// Used for scoping (ScopeHints) and as Subject on Diagnosis.
/// Value equality + explicit Equals for clarity in the diagram.
/// </summary>
public sealed record ResourceRef(string Kind, string Identifier)
{
    public bool Equals(ResourceRef? other) // Explicit per the Mermaid diagram
    {
        if (other is null) return false;
        return Kind == other.Kind && Identifier == other.Identifier;
    }

    public override int GetHashCode() => HashCode.Combine(Kind, Identifier);
}
