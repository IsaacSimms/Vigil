// == ScopeHints (from Diagram 6) == //

using System;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain.Models;

/// <summary>
/// Optional narrowing provided by the engineer.
/// If omitted the assembler still ranks everything and the model decides scope (safe because of the citation floor).
/// </summary>
public record ScopeHints(
    ResourceRef? Resource = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Symptom = null);
