// == TextRedactor (IRedactor impl for text masking per §8; images not auto-redacted in v1) == //

using System.Text.RegularExpressions;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Models;

namespace Vigil.Infrastructure.Redactors;

/// <summary>
/// Masks common secrets in text artifacts before egress to the model.
/// Runs in Application, immediately before analyzer call (the true egress).
/// Simple regex-based for v1; honest stance for images (not masked here).
/// </summary>
public class TextRedactor : IRedactor
{
    private static readonly Regex[] SecretPatterns = new[]
    {
        new Regex(@"(?i)(password|passwd|pwd|secret|api[_-]?key|token|auth|credential)\s*[:=]\s*([^\s&""']+)", RegexOptions.Compiled),
        new Regex(@"(?i)(-----BEGIN (RSA |EC |DSA )?PRIVATE KEY-----[\s\S]*?-----END (RSA |EC |DSA )?PRIVATE KEY-----)", RegexOptions.Compiled),
        new Regex(@"(?i)(sk-[a-zA-Z0-9]{20,})", RegexOptions.Compiled) // example openai-like
    };

    public EvidenceBundle Redact(EvidenceBundle bundle)
    {
        if (bundle == null) return null;

        var redactedArtifacts = new List<Vigil.Domain.Entities.EvidenceArtifact>();
        foreach (var artifact in bundle.Artifacts)
        {
            if (artifact.TextContent != null)
            {
                var redactedText = artifact.TextContent;
                foreach (var pattern in SecretPatterns)
                {
                    redactedText = pattern.Replace(redactedText, m => 
                    {
                        if (m.Groups.Count > 2)
                            return m.Groups[1].Value + "=***REDACTED***";
                        return "***REDACTED***";
                    });
                }
                // Create redacted version (since entity immutable-ish in practice)
                var redacted = new Vigil.Domain.Entities.EvidenceArtifact(
                    artifact.Id, artifact.Modality, artifact.Kind, redactedText, artifact.ImageBytes, 
                    artifact.MediaType, artifact.Timestamp, artifact.Resource);
                redactedArtifacts.Add(redacted);
            }
            else
            {
                redactedArtifacts.Add(artifact); // images pass through (per v1 honest stance)
            }
        }

        return new EvidenceBundle(redactedArtifacts, bundle.Exclusions, bundle.Symptom);
    }
}
