// == RawDiagnosis (from Diagram 4; the deserialized tool response before validation) == //

using System.Collections.Generic;
using Vigil.Domain.Entities;

namespace Vigil.Domain.Models;

/// <summary>
/// What comes out of the Grok tool-use call after JSON deserialization.
/// Still untrusted - must go through DiagnosisValidator (citation resolution, drop/strip, cap to 5).
/// The model is constrained by the tool schema to produce this shape.
/// </summary>
public record RawDiagnosis(string Summary, IReadOnlyList<CandidateCause> Causes);
