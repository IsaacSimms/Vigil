// == DiagnosisValidator (from Diagram 6 + SystemsDesign §6: the deterministic gate) == //

using System;
using System.Collections.Generic;
using System.Linq;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects; // for ResourceRef, AnalyzerProvenance in skeleton paths
using Vigil.Domain.Enums;

namespace Vigil.Application.Coordinators;

/// <summary>
/// The deterministic validation gate after the model step (§6).
/// Sequence: resolve citations via ICitationResolver against the bundle,
/// drop/strip ungrounded, rank/truncate to <=5, emit Diagnosis + report.
/// "The model judges; the system constrains and verifies."
/// </summary>
public class DiagnosisValidator
{
    private readonly ICitationResolver _resolver;

    public DiagnosisValidator(ICitationResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public ValidationResult Validate(RawDiagnosis raw, EvidenceBundle bundle, AnalyzerTier tier = AnalyzerTier.Model)
    {
        if (raw == null || bundle == null)
        {
            // Minimal failure path for skeleton
            var emptyDiagnosis = new Diagnosis(
                Guid.NewGuid(),
                new ResourceRef("unknown", "unknown"), // no Hints on bundle; hints were input to assembler
                "Validation failed (skeleton)",
                new AnalyzerProvenance(tier),
                DateTimeOffset.UtcNow,
                Array.Empty<CandidateCause>());
            return new ValidationResult(emptyDiagnosis, new ValidationReport(new[] { "Invalid input to validator" }, Array.Empty<string>()));
        }

        var drops = new List<string>();
        var strips = new List<string>();
        var validCauses = new List<CandidateCause>();

        foreach (var cause in raw.Causes.Take(10)) // cap input
        {
            var resolvingCitations = new List<Citation>();
            var nonResolving = new List<Citation>();

            foreach (var citation in cause.Citations)
            {
                if (_resolver.Resolve(citation, bundle))
                {
                    resolvingCitations.Add(citation);
                }
                else
                {
                    nonResolving.Add(citation);
                    strips.Add($"Stripped non-resolving citation {citation.EvidenceArtifactId} for cause");
                }
            }

            if (resolvingCitations.Count > 0)
            {
                // Keep cause with only resolving citations
                var kept = new CandidateCause(
                    cause.Description,
                    cause.CausalChain,
                    cause.Confidence,
                    cause.Severity,
                    cause.Category,
                    resolvingCitations);
                validCauses.Add(kept);
            }
            else
            {
                drops.Add($"Dropped cause with zero resolving citations: {cause.Description}");
            }
        }

        // Rank by confidence and truncate to <=5 (backstop per design)
        var ranked = validCauses
            .OrderByDescending(c => c.Confidence.Value)
            .Take(5)
            .ToList();

        if (validCauses.Count > ranked.Count)
        {
            drops.Add($"Truncated {validCauses.Count - ranked.Count} lower-confidence causes to cap of 5");
        }

        // Build final Diagnosis (skeleton provenance; real from analyzer result)
        var diagnosis = new Diagnosis(
            Guid.NewGuid(),
            new ResourceRef("subject", "unknown"), // bundle does not carry hints; was used upstream
            raw.Summary,
            new AnalyzerProvenance(tier), // from analyzer result or default
            DateTimeOffset.UtcNow,
            ranked);

        var report = new ValidationReport(drops, strips);
        return new ValidationResult(diagnosis, report);
    }
}
