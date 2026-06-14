// == ValidationReport (supporting from Diagram 6 / §6; records drops and strips from citation validation) == //

using System.Collections.Generic;

namespace Vigil.Domain.Models;

/// <summary>
/// Details every cause that was dropped (zero resolving citations) or had non-resolving citations stripped.
/// Produced deterministically in C# after the stochastic model step.
/// </summary>
public record ValidationReport(IReadOnlyList<string> Drops, IReadOnlyList<string> Strips);
