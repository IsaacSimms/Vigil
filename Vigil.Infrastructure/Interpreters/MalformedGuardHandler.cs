// == MalformedGuardHandler (CoR guard per Diagram 5 + §10) == //

using System;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Guards against malformed artifacts. Short-circuits with ExclusionReason if invalid.
/// Feeds the validation/exclusions report (genuine short-circuit, not always-every-stage).
/// </summary>
public class MalformedGuardHandler : InterpretationHandler
{
    protected override InterpretationResult Process(EvidenceArtifact artifact)
    {
        if (artifact == null || (string.IsNullOrWhiteSpace(artifact.TextContent) && (artifact.ImageBytes == null || artifact.ImageBytes.Length == 0)))
        {
            return new InterpretationResult(false, null, "Malformed: no content");
        }
        return new InterpretationResult(true, artifact);
    }
}
