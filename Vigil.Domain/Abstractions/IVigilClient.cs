// == IVigilClient Seam (UL) - Transport / presentation-to-core seam (from Diagram 6 + SystemsDesign §3) == //

using System.Collections.Generic;
using System.Threading.Tasks;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// The load-bearing seam between presentation and core (Dependency Inversion proof in headline Diagram 6).
/// InProcessVigilClient is the default (wires DiagnoseUseCase + all collaborators).
/// HttpVigilClient (over optional Api) is designed-for but not built in v1.
/// Presentation must do nothing but gather + call + render.
/// </summary>
public interface IVigilClient
{
    Task<Diagnosis> DiagnoseAsync(DiagnoseRequest request);
    Task<Diagnosis[]> QueryHistoryAsync(ISpecification<Diagnosis> spec);

    // == Grill-me conversational seam (additive, per approved plan and user hole-2 agreement) == //
    // Primary path for natural language in the interactive TUI. The implementation (InProcess or future Http)
    // will delegate to a grill advisor (Grok or heuristic fallback). Context is compact (cwd, summaries, token hints)
    // rather than full bundles so the TUI can keep the conversation flowing without always hitting the full
    // evidence assembly + validator path (that is reserved for explicit "produce a formal Diagnosis" moments).
    Task<string> ConsultAsync(string message, string? cwd = null, Guid? lastDiagnosisId = null, string? compactContext = null);
}
