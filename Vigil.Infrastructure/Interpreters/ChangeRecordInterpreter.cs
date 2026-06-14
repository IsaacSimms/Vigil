// == ChangeRecordInterpreter (IArtifactInterpreter for change records, high priority per §10) == //

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Interpreter for change records (e.g. "change X to Y at time Z").
/// High relevance in ranking. Produces ChangeRecord kind artifact.
/// </summary>
public class ChangeRecordInterpreter : IArtifactInterpreter
{
    public bool CanParse(RawSource source)
    {
        if (source?.Text == null) return false;
        // Simple detection for skeleton: contains "change" or "deployed"
        return source.Text.Contains("change", StringComparison.OrdinalIgnoreCase) ||
               source.Text.Contains("deploy", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<EvidenceArtifact> Interpret(RawSource source)
    {
        if (source?.Text == null) yield break;

        // Skeleton parse: extract simplistic
        var text = source.Text;
        var resourceMatch = Regex.Match(text, @"to\s+([\w\-]+)", RegexOptions.IgnoreCase);
        var idMatch = Regex.Match(text, @"change\s+([\w\-]+)", RegexOptions.IgnoreCase);

        var resource = resourceMatch.Success ? resourceMatch.Groups[1].Value : "unknown";
        var changeId = idMatch.Success ? idMatch.Groups[1].Value : Guid.NewGuid().ToString("N").Substring(0, 8);

        var content = $"ChangeRecord: {changeId} to {resource}";

        yield return new EvidenceArtifact(
            Guid.NewGuid(),
            Modality.Text,
            ArtifactKind.ChangeRecord,
            content,
            null,
            "text/plain",
            DateTimeOffset.UtcNow,
            new ResourceRef("resource", resource)
        );
    }
}
