// == IArtifactInterpreter Seam (UL) - Strategy (UL) for heterogeneous input (from Diagram 5 + SystemsDesign §10) == //

using System.Collections.Generic;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// Strategy (UL) for turning raw bytes/text into normalized EvidenceArtifacts.
/// ArtifactInterpreterSelector (Simple Factory selection, not GoF Factory Method) picks one per RawSource.
/// Post-processing often uses the CoR + Template Method (InterpretationHandler) for metadata and malformed guards.
/// </summary>
public interface IArtifactInterpreter
{
    // Content sniffing - no --format flag in the common case.
    bool CanParse(RawSource source);

    // Produces zero or more artifacts + may contribute to exclusions via the handler chain.
    IEnumerable<EvidenceArtifact> Interpret(RawSource source);
}
