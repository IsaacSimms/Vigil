// == IDiagnosisAnalyzer Seam (UL) - Strategy (UL) seam for model vs heuristic (from Diagram 4 + SystemsDesign §7) == //

using System.Threading.Tasks;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// The primary Strategy (UL) seam for diagnosis analysis. Allows swapping between the Grok model adapter (primary Implementation (UL)) and the heuristic fallback without affecting callers.
/// All SDK types (e.g. OpenAIClient) are confined to the Adapter (UL) implementation in Infrastructure per the teaching note in Diagram 4.
/// </summary>
public interface IDiagnosisAnalyzer
{
    // The core contract: takes assembled evidence + optional symptom, returns raw result or failure. 
    // Used by DiagnoseUseCase (Diagram 6). Fallback decision lives in the use case (§9).
    Task<AnalyzerResult> AnalyzeAsync(EvidenceBundle bundle, string? symptom);
}
