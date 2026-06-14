// == AnalyzerResult (from Diagram 4 + §7) == //

using Vigil.Domain.Enums;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain.Models;

/// <summary>
/// The result of crossing the IDiagnosisAnalyzer seam.
/// Either a successful RawDiagnosis (still needs validation) or a typed failure that triggers heuristic fallback in the use case.
/// SDK exceptions are translated here so they never leak past Infrastructure.
/// </summary>
public record AnalyzerResult(
    bool IsSuccess,
    RawDiagnosis? Diagnosis = null,
    AnalyzerTier Tier = default,
    FallbackReason? FailureReason = null,
    TokenUsage? Usage = null);
