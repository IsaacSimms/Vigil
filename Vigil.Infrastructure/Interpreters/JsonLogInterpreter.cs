// == JsonLogInterpreter (basic IArtifactInterpreter for JSON logs per §10) == //

using System;
using System.Collections.Generic;
using System.Text.Json;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Parses JSON log lines. For skeleton, treats whole as single artifact if looks like JSON.
/// </summary>
public class JsonLogInterpreter : IArtifactInterpreter
{
    public bool CanParse(RawSource source)
    {
        if (source?.Text == null) return false;
        var trimmed = source.Text.Trim();
        return trimmed.StartsWith("{") && trimmed.EndsWith("}");
    }

    public IEnumerable<EvidenceArtifact> Interpret(RawSource source)
    {
        if (source?.Text == null) yield break;

        bool isValidJson = false;
        try
        {
            // Validate it's JSON (skeleton)
            JsonDocument.Parse(source.Text);
            isValidJson = true;
        }
        catch (JsonException)
        {
            // not valid, skip
        }

        if (isValidJson)
        {
            yield return new EvidenceArtifact(
                Guid.NewGuid(),
                Modality.Text,
                ArtifactKind.LogFile,
                source.Text,
                null,
                "application/json",
                DateTimeOffset.UtcNow,
                string.IsNullOrEmpty(source.Name) ? null : new ResourceRef("file", source.Name)
            );
        }
    }
}
