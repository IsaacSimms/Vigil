// == Citation Value Object (from Diagram 3 + SystemsDesign §4: record-level, UUID evidence_artifact_id, Snippet for humans only) == //

namespace Vigil.Domain.ValueObjects;

/// <summary>
/// Points at a specific EvidenceArtifact by its Id. 
/// Snippet is purely for human findability in the rendered output - validation never uses it.
/// The validator only checks that the EvidenceArtifactId exists in the assembled bundle.
/// </summary>
public sealed record Citation(Guid EvidenceArtifactId, string? Snippet = null);
