// == IGrillAdvisor (conversational Strategy seam for natural-language grill-me sessions) == //
// Pure Domain interface (no externals). Allows swapping Grok (primary) vs. heuristic/stub fallbacks.
// Called from the TUI runner via IVigilClient.ConsultAsync (the presentation seam).
// Context (cwd, compact session state with tokens/evidence summary, last diagnosis) is passed so the advisor
// can be a good debugging partner without duplicating the full diagnose pipeline (which stays for /diagnose).

using System;
using System.Threading.Tasks;

namespace Vigil.Domain.Abstractions;

public interface IGrillAdvisor
{
    Task<string> ConsultAsync(string message, string? cwd = null, Guid? lastDiagnosisId = null, string? compactContext = null);
}
