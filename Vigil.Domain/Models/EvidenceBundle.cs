// == EvidenceBundle (from Diagram 6; minimal per docs) == //

using System.Collections.Generic;
using Vigil.Domain.Entities;
using Vigil.Domain.Models; // for ExclusionReport if separate

namespace Vigil.Domain.Models;

/// <summary>
/// The normalized, ranked, capped collection of artifacts that is sent to the analyzer.
/// Produced by EvidenceAssembler (rank + cap). Redactor runs on it immediately before egress.
/// Exclusions record what was dropped so the engineer can see selection bias.
/// </summary>
public record EvidenceBundle(
    IReadOnlyList<EvidenceArtifact> Artifacts,
    ExclusionReport Exclusions,
    string? Symptom = null);
