// == ValidationResult (from Diagram 6 + §6) == //

using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Domain.Models;

/// <summary>
/// Output of the deterministic validation gate. 
/// The Diagnosis here has already had ungrounded causes dropped/stripped and been truncated to <=5.
/// The Report tells the engineer exactly what the validator did (trust contract).
/// </summary>
public record ValidationResult(Diagnosis Diagnosis, ValidationReport Report);
