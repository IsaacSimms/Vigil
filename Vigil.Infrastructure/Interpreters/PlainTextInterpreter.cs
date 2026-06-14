// == PlainTextInterpreter (IArtifactInterpreter impl for plain text logs etc. per §10 + Diagram 5) == //

using System.Collections.Generic;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Default/fallback interpreter for plain text. Always CanParse if no other matches or for text content.
/// Produces single EvidenceArtifact with TextContent.
/// </summary>
public class PlainTextInterpreter : IArtifactInterpreter
{
    public bool CanParse(RawSource source)
    {
        // For skeleton: parses if has Text or is generic
        return source != null && (source.Text != null || source.Hint?.Contains("text") == true || source.Bytes == null);
    }

    public IEnumerable<EvidenceArtifact> Interpret(RawSource source)
    {
        if (source == null) yield break;

        var text = source.Text ?? (source.Bytes != null ? System.Text.Encoding.UTF8.GetString(source.Bytes) : string.Empty);
        if (string.IsNullOrWhiteSpace(text)) yield break;

        yield return new EvidenceArtifact(
            Guid.NewGuid(),
            Modality.Text,
            ArtifactKind.LogFile, // treat as log for demo
            text,
            null,
            "text/plain",
            DateTimeOffset.UtcNow,
            string.IsNullOrEmpty(source.Name) ? null : new Domain.ValueObjects.ResourceRef("file", source.Name)
        );
    }
}
