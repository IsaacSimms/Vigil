// == RawSource (supporting type for IArtifactInterpreter and DiagnoseRequest per Diagram 5/6; minimal practical per available docs) == //

namespace Vigil.Domain.Models;

/// <summary>
/// Carrier for raw input bytes/text coming from stdin or flags.
/// The concrete interpreters use CanParse to decide who owns it (Simple Factory selection).
/// Hint can carry original filename or format suggestion when auto-detection is ambiguous.
/// </summary>
public record RawSource(string? Name = null, string? Text = null, byte[]? Bytes = null, string? Hint = null);
