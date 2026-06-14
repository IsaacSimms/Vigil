// == EvidenceArtifact Entity (from Diagram 3 + SystemsDesign §4) == //

using Vigil.Domain.Enums;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain.Entities;

/// <summary>
/// Core entity representing a single piece of evidence (log, change record, screenshot, etc.).
/// One of the primary inputs to the diagnose pipeline. Citations point at its Id (record-level grounding).
/// </summary>
public class EvidenceArtifact
{
    public Guid Id { get; } // Stable identity for citations (EvidenceArtifactId in Citation)
    public Modality Modality { get; } // Text or Image - bounds what the analyzer (Grok) can consume
    public ArtifactKind Kind { get; } // One of LogFile | ChangeRecord | ... per design
    public string? TextContent { get; } // For text modalities
    public byte[]? ImageBytes { get; } // For image modalities (base64 later in adapter)
    public string? MediaType { get; } // Detected from magic bytes in interpreter (not extension)
    public DateTimeOffset? Timestamp { get; } // Used for temporal ranking in EvidenceAssembler
    public ResourceRef? Resource { get; } // Optional scope hint (e.g. host, service)

    public EvidenceArtifact(
        Guid id,
        Modality modality,
        ArtifactKind kind,
        string? textContent = null,
        byte[]? imageBytes = null,
        string? mediaType = null,
        DateTimeOffset? timestamp = null,
        ResourceRef? resource = null)
    {
        Id = id;
        Modality = modality;
        Kind = kind;
        TextContent = textContent;
        ImageBytes = imageBytes;
        MediaType = mediaType;
        Timestamp = timestamp;
        Resource = resource;
    }

    public bool HasResource() => Resource is not null; // Convenience for ranking and scoping logic
}
