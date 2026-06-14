// == Diagnosis Entity (from Diagram 3 + SystemsDesign §4) == //

using System;
using System.Collections.Generic;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain.Entities;

/// <summary>
/// The root output of the entire pipeline. Always has provenance (Model vs Heuristic) and 
/// a list of causes that have passed the deterministic validation gate (§6).
/// Institutional memory is built by persisting these.
/// </summary>
public class Diagnosis
{
    public Guid Id { get; }
    public ResourceRef Subject { get; } // The thing that had the incident (host, service, etc.)
    public string Summary { get; }      // Model-generated high-level explanation
    public AnalyzerProvenance Provenance { get; } // Always present; never silent degradation
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<CandidateCause> Causes { get; } // Guaranteed <= 5 and all cited after validation

    public Diagnosis(
        Guid id,
        ResourceRef subject,
        string summary,
        AnalyzerProvenance provenance,
        DateTimeOffset createdAt,
        IReadOnlyList<CandidateCause> causes)
    {
        Id = id;
        Subject = subject;
        Summary = summary;
        Provenance = provenance;
        CreatedAt = createdAt;
        Causes = causes;
    }

    public CandidateCause TopCause()
    {
        if (Causes.Count == 0)
            throw new InvalidOperationException("No causes available.");
        // First element is treated as top (assembler + validator produce ranked list)
        return Causes[0];
    }
}
