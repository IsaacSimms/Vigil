// == EvidenceAssembler (from Diagram 6 + SystemsDesign §5: rank + cap + exclusions) == //

using System;
using System.Collections.Generic;
using System.Linq;
using Vigil.Domain;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Application.Coordinators;

/// <summary>
/// Gathers artifacts, ranks by relevance (temporal proximity, severity, resource match),
/// truncates to a token budget, and records exclusions.
/// Called before redaction and analysis. Deterministic.
/// </summary>
public class EvidenceAssembler
{
    private const int DefaultTokenBudget = 8000; // placeholder; real cap driven by model limits

    public EvidenceBundle Assemble(IEnumerable<EvidenceArtifact> artifacts, ScopeHints hints)
    {
        var list = artifacts?.ToList() ?? new List<EvidenceArtifact>();
        if (list.Count == 0)
        {
            return new EvidenceBundle(Array.Empty<EvidenceArtifact>(), new ExclusionReport(Array.Empty<string>()), hints);
        }

        // Rank (simple for skeleton; real uses temporal/severity/resource per §5)
        var ranked = list
            .OrderByDescending(a => RankByRelevance(a, hints))
            .ToList();

        // Apply cap (skeleton: keep all or simple limit; full impl uses token estimation)
        var capped = ApplyTokenCap(ranked);

        var exclusions = list.Count > capped.Count
            ? new ExclusionReport(list.Skip(capped.Count).Select(a => $"Excluded artifact {a.Id} by cap/rank").ToList())
            : new ExclusionReport(Array.Empty<string>());

        return new EvidenceBundle(capped, exclusions, hints);
    }

    private double RankByRelevance(EvidenceArtifact artifact, ScopeHints hints)
    {
        return ArtifactRelevanceScorer.Score(artifact, hints?.From, hints?.Resource);
    }

    private IReadOnlyList<EvidenceArtifact> ApplyTokenCap(IEnumerable<EvidenceArtifact> ranked)
    {
        // Very naive cap for skeleton (e.g. first N). Real version would estimate tokens from text/image size.
        // See §5 and EvidenceAssembler in Diagram 6.
        return ranked.Take(50).ToList(); // placeholder cap
    }
}
