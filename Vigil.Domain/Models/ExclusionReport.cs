// == ExclusionReport (supporting from Diagram 6 / InterpretationResult; minimal collection of reasons per docs) == //

using System.Collections.Generic;

namespace Vigil.Domain.Models;

/// <summary>
/// Records what was deliberately dropped during evidence assembly or interpretation.
/// Always shown to the engineer (trust contract). Produced by assembler and the CoR handlers.
/// </summary>
public record ExclusionReport(IReadOnlyList<string> Reasons);
