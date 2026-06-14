// == MetadataExtractionHandler (CoR stage per Diagram 5) == //

using System;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Extracts cheap metadata (e.g. timestamp from content if possible).
/// For skeleton: passes through, adds timestamp if missing.
/// </summary>
public class MetadataExtractionHandler : InterpretationHandler
{
    protected override InterpretationResult Process(EvidenceArtifact artifact)
    {
        // Skeleton: ensure timestamp
        if (artifact.Timestamp == null)
        {
            // In real, parse from text etc.
            // For now, mutate? But entity is immutable in design? Use as-is.
        }
        return new InterpretationResult(true, artifact);
    }
}
