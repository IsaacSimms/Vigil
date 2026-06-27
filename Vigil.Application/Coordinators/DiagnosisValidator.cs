// == DiagnosisValidator (from Diagram 6 + SystemsDesign §6: the deterministic gate) == //

using System;
using System.Collections.Generic;
using System.Linq;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects; // for ResourceRef, AnalyzerProvenance, Citation

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

    // The use case assembles the real AnalyzerProvenance (tier + usage + fallback reason per §9);
    // the gate stamps it onto the Diagnosis verbatim rather than reconstructing a hollow one.
    public ValidationResult Validate(RawDiagnosis raw, EvidenceBundle bundle, AnalyzerProvenance provenance)
    {
        if (provenance == null) throw new ArgumentNullException(nameof(provenance));

        if (raw == null || bundle == null)
        {
            // Minimal failure path; still carries the analyzer's real provenance.
            var emptyDiagnosis = new Diagnosis(
                Guid.NewGuid(),
                bundle?.Hints?.Resource ?? new ResourceRef("unknown", "unknown"),
                "Validation failed (invalid input)",
                provenance,
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

        // Build final Diagnosis. Subject is the engineer's declared scope when present (§4);
        // provenance is the real one supplied by the use case (tier + usage + reason).
        var diagnosis = new Diagnosis(
            Guid.NewGuid(),
            bundle.Hints?.Resource ?? new ResourceRef("unknown", "unknown"),
            raw.Summary,
            provenance,
            DateTimeOffset.UtcNow,
            ranked);

        var report = new ValidationReport(drops, strips);
        return new ValidationResult(diagnosis, report);
    }
}
