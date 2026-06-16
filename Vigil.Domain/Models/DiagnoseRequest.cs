// == DiagnoseRequest (from Diagram 6 + §3/10) == //

using System.Collections.Generic;
using Vigil.Domain.Models;

namespace Vigil.Domain.Models;

/// <summary>
/// The input DTO from presentation (CLI) into the core.
/// Contains the raw sources plus optional scoping hints.
/// Offline flag forces the heuristic analyzer (no model call, zero cost).
/// </summary>
public record DiagnoseRequest(
    IReadOnlyList<RawSource> Sources,
    ScopeHints Hints,
    bool Offline = false);
