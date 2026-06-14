// == Domain Enums (from Diagram 3 + SystemsDesign §4) == //

namespace Vigil.Domain.Enums;

/// <summary>
/// All enumerations defined in the Domain model. These provide the closed set of categories 
/// used for classification, scoping, and provenance. Changes here are high-impact (affects 
/// queries, rendering, and the heuristic).
/// </summary>

public enum Modality
{
    Text,   // Interpreted via text interpreters; sent as text blocks to Grok
    Image   // Interpreted via ImageInterpreter; sent as base64 vision blocks (v1.1+)
}

public enum ArtifactKind
{
    LogFile,        // e.g. journalctl, application logs
    ChangeRecord,   // The special first-class citizen for "what changed"
    Code,           // Source snippets, diffs
    Screenshot,     // Terminal or UI captures (vision)
    Config,         // Config files, env, helm values
    StackTrace,     // Exception traces
    Other           // Fallback for unknown
}

public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}

public enum CauseCategory
{
    ConfigChange,       // Direct config mutation
    Deployment,         // Deploy, rollout, image change
    ResourceExhaustion, // CPU, mem, disk, connection limits
    Permission,         // Authz, RBAC, ACL issues
    DependencyFailure,  // Downstream service, DB, queue
    External,           // 3rd party, infra provider, human factor
    Other
}

public enum AnalyzerTier
{
    Model,     // Produced by the Grok adapter (IDiagnosisAnalyzer primary impl)
    Heuristic  // Produced by proximity baseline (offline / fallback)
}

public enum FallbackReason
{
    Timeout,
    ApiUnavailable,
    OfflineFlag,  // --offline explicitly requested
    NoApiKey,     // No XAI_API_KEY at composition root
    Refusal       // Model refused to produce structured output
}
