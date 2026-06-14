// == IRedactor (Domain abstraction from Diagram 6 + §8) == //

using Vigil.Domain.Models;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// Runs in Application, immediately before the analyzer call (the real egress boundary).
/// Text secrets are masked here. Images are not auto-redacted in v1 (honest stance).
/// Never redacts in presentation - that would be the wrong seam.
/// </summary>
public interface IRedactor
{
    EvidenceBundle Redact(EvidenceBundle bundle);
}
