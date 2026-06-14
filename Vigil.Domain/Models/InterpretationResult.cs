// == InterpretationResult (from Diagram 5; for CoR pipeline) == //

using Vigil.Domain.Entities;

namespace Vigil.Domain.Models;

/// <summary>
/// Result of one stage in the short-circuiting interpretation Chain of Responsibility.
/// If Continue == false the artifact is excluded (ExclusionReason recorded) and the chain stops for that item.
/// </summary>
public record InterpretationResult(
    bool Continue,
    EvidenceArtifact? Artifact = null,
    string? ExclusionReason = null);
