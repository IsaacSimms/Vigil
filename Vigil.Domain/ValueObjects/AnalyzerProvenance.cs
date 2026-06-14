// == AnalyzerProvenance Value Object (from Diagram 3 + SystemsDesign §4: mandatory, declares tier) == //

using Vigil.Domain.Enums;

namespace Vigil.Domain.ValueObjects;

/// <summary>
/// Mandatory provenance attached to every Diagnosis. 
/// "Never silently degrade" - if we fell back to heuristic the engineer must see why.
/// Captures token cost when the model tier was used.
/// </summary>
public sealed record AnalyzerProvenance(
    AnalyzerTier AnalyzedBy,           // Model or Heuristic
    FallbackReason? Reason = null,     // Only set when AnalyzedBy == Heuristic
    TokenUsage? Usage = null);         // Only populated for Model tier (cost observation)

