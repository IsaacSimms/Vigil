// == IArtifactInterpreterSelector (from Diagram 5 + SystemsDesign §10; Simple Factory selection over Strategy) == //

using Vigil.Domain.Models;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// Selects the appropriate IArtifactInterpreter for a given RawSource via CanParse.
/// This is Simple Factory selection (explicitly not GoF Factory Method).
/// Lives in orchestration but depends only on Domain abstractions.
/// </summary>
public interface IArtifactInterpreterSelector
{
    IArtifactInterpreter Select(RawSource source);
}
