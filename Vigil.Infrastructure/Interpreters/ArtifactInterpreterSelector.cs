// == ArtifactInterpreterSelector (Simple Factory selection over IArtifactInterpreter strategies per Diagram 5 + §10) == //

using System;
using System.Collections.Generic;
using System.Linq;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Models;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Selects the first IArtifactInterpreter that CanParse the RawSource.
/// Registered in composition root with all available interpreters.
/// Explicitly NOT GoF Factory Method (selects among existing, per design note).
/// </summary>
public class ArtifactInterpreterSelector : IArtifactInterpreterSelector
{
    private readonly IEnumerable<IArtifactInterpreter> _interpreters;

    public ArtifactInterpreterSelector(IEnumerable<IArtifactInterpreter> interpreters)
    {
        _interpreters = interpreters ?? throw new ArgumentNullException(nameof(interpreters));
    }

    public IArtifactInterpreter Select(RawSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var selected = _interpreters.FirstOrDefault(i => i.CanParse(source));
        if (selected == null)
        {
            // Fallback to plain text for skeleton; in real would throw or use Other
            selected = _interpreters.OfType<PlainTextInterpreter>().FirstOrDefault();
        }

        return selected ?? throw new InvalidOperationException("No suitable interpreter found for source.");
    }
}
