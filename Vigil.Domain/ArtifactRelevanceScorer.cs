// == ArtifactRelevanceScorer (canonical Domain scoring — temporal proximity, artifact kind, resource match) == //
// Pure static module. No interface, no seam: one formula, no variation axis across callers.
// Shared by EvidenceAssembler (ranking before bundle cap) and HeuristicDiagnosisAnalyzer (ranking causes).
// Placing it in Domain ensures both Application and Infrastructure resolve the same curve.

using System;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain;

public static class ArtifactRelevanceScorer
{
    private const double TemporalWeight        = 1000.0; // decay weight for proximity curve
    private const double ChangeRecordBonus     =  500.0; // ChangeRecords are primary causal signals
    private const double ResourceMatchBonus    =  500.0; // artifact is on the exact scoped resource
    private const double ResourcePresenceBonus =  100.0; // artifact is resource-tagged; no explicit target to compare

    // == Score an artifact relative to an incident reference time and optional resource target == //
    // referenceTime: hints.From (engineer-supplied window) or first-error approximation. Null skips temporal component.
    // targetResource: engineer-supplied resource scope. Null awards presence bonus instead of match/miss.
    public static double Score(EvidenceArtifact artifact, DateTimeOffset? referenceTime, ResourceRef? targetResource = null)
    {
        double score = 0;

        if (referenceTime.HasValue && artifact.Timestamp.HasValue)
        {
            var delta = Math.Abs((artifact.Timestamp.Value - referenceTime.Value).TotalSeconds);
            score += TemporalWeight / (1 + delta);
        }

        if (artifact.Kind == ArtifactKind.ChangeRecord)
            score += ChangeRecordBonus;

        if (targetResource != null)
        {
            // Explicit scope: match earns bonus; wrong resource earns nothing
            if (artifact.Resource != null && artifact.Resource.Equals(targetResource))
                score += ResourceMatchBonus;
        }
        else if (artifact.Resource != null)
        {
            score += ResourcePresenceBonus;
        }

        return score;
    }
}
