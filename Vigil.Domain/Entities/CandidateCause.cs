// == CandidateCause Entity (from Diagram 3 + SystemsDesign §4) == //

using System.Collections.Generic;
using Vigil.Domain.Enums;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain.Entities;

/// <summary>
/// A ranked, cited explanation for the root cause. Lives inside Diagnosis.Causes (capped at 5).
/// Every surviving cause after DiagnosisValidator must have at least one resolving Citation.
/// </summary>
public sealed record CandidateCause(
    string Description,                    // Human-readable "what broke"
    string? CausalChain,                   // Optional "why it led here" narrative from the model
    Confidence Confidence,                 // Bounded [0,1] from the analyzer
    Severity Severity,                     // From enum; used for filtering and display
    CauseCategory Category,                // Coarse classification for queries
    IReadOnlyList<Citation> Citations);    // Must resolve against the EvidenceBundle or be stripped

