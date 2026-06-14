// == HeuristicDiagnosisAnalyzer (proximity impl as Liskov substitute for IDiagnosisAnalyzer per §9 + Diagram 4) == //

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Infrastructure;

/// <summary>
/// Deliberately minimal heuristic: ranks ChangeRecord and timestamped artifacts by temporal proximity to "first error" and resource match.
/// Templated descriptions. Zero-cost offline baseline. True Liskov sub for the seam (same contract, different scoring).
/// Used for --offline, model failure fallback, and deterministic tests.
/// </summary>
public class HeuristicDiagnosisAnalyzer : IDiagnosisAnalyzer
{
    public async Task<AnalyzerResult> AnalyzeAsync(EvidenceBundle bundle, string? symptom)
    {
        await Task.CompletedTask; // for async shape

        if (bundle == null || bundle.Artifacts.Count == 0)
        {
            return new AnalyzerResult(false, null, AnalyzerTier.Heuristic, FallbackReason.OfflineFlag);
        }

        // Find "first error" approx: earliest timestamp or first ChangeRecord
        var firstError = bundle.Artifacts
            .Where(a => a.Timestamp.HasValue)
            .OrderBy(a => a.Timestamp)
            .FirstOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow.AddMinutes(-5);

        var scored = bundle.Artifacts
            .Select(a => (Artifact: a, Score: ScoreByProximity(a, firstError)))
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();

        var causes = new List<CandidateCause>();
        foreach (var (artifact, score) in scored)
        {
            var desc = $"change {artifact.Id} to {artifact.Resource?.Identifier ?? "resource"} occurred ~{(int)Math.Abs((artifact.Timestamp.GetValueOrDefault(firstError) - firstError).TotalSeconds)}s before the first error";
            var conf = new Confidence(Math.Min(0.95, score / 1000.0)); // normalize
            causes.Add(new CandidateCause(
                desc,
                null,
                conf,
                Severity.Medium,
                CauseCategory.ConfigChange,
                new List<Citation> { new Citation(artifact.Id, artifact.TextContent?.Substring(0, Math.Min(50, artifact.TextContent.Length)) + "...") }
            ));
        }

        var raw = new RawDiagnosis("Heuristic proximity analysis (no AI)", causes);
        var usage = new TokenUsage(0, 0); // zero cost
        return new AnalyzerResult(true, raw, AnalyzerTier.Heuristic, null, usage);
    }

    private double ScoreByProximity(EvidenceArtifact artifact, DateTimeOffset firstError)
    {
        double score = 0;
        if (artifact.Timestamp.HasValue)
        {
            var delta = Math.Abs((artifact.Timestamp.Value - firstError).TotalSeconds);
            score += 1000 / (1 + delta);
        }
        if (artifact.Kind == ArtifactKind.ChangeRecord) score += 500;
        if (artifact.Resource != null) score += 200; // resource match bias
        return score;
    }
}
