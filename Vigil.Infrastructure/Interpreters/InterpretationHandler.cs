// == InterpretationHandler (CoR + Template Method base per Diagram 5 + §10) == //

using System;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Infrastructure.Interpreters;

/// <summary>
/// Abstract base for short-circuiting Chain of Responsibility on per-artifact processing.
/// Template Method: Handle calls Process, forwards only if Continue.
/// Malformed artifacts short-circuit into ExclusionReport (per design).
/// </summary>
public abstract class InterpretationHandler
{
    private InterpretationHandler? _next;

    public InterpretationHandler SetNext(InterpretationHandler handler)
    {
        _next = handler;
        return handler;
    }

    public InterpretationResult Handle(EvidenceArtifact artifact)
    {
        var result = Process(artifact);
        if (!result.Continue && _next != null)
        {
            // short-circuit: do not forward
            return result;
        }

        if (_next != null && result.Continue)
        {
            return _next.Handle(result.Artifact ?? artifact);
        }

        return result;
    }

    protected abstract InterpretationResult Process(EvidenceArtifact artifact);
}
