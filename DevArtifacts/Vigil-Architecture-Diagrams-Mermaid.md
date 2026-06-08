# Vigil — Diagram Source (Mermaid for Lucidchart)

**How to use this file.** In Lucidchart, open a document → left toolbar → **Diagram as code** → paste one block below → it renders instantly. Do this once per diagram. Lucid supports Mermaid (optimized for v11.14) for class, sequence, flowchart, state, C4, Gantt, and ERD types — all the types used here. The render is a static image on the canvas; to restyle, edit the code and click **Update**.

**Conventions used (for Lucid compatibility):**
- Nullable reference types (`?`) are shown only on fields/properties. Method-parameter nullability is described in the design doc prose, not in the diagram, because nested/edge-case generics and `?` inside method parens can trip Lucid's Mermaid parser.
- Collection returns are written as `Task~T[]~` (parser-safe) where the actual contract returns `IReadOnlyList<T>` — noted in the doc.
- If any single block fails to render in Lucid, the fix is almost always to simplify one signature (drop a generic nesting); the structure is what matters for the grade, not the exact return type.

Diagram order matches the design document's placeholder callouts.

---

## Diagram 1 — Architecture: component & layer overview (flowchart)

```mermaid
flowchart TD
    subgraph PRES["Presentation"]
        CLI["Vigil.Cli<br/>(Spectre.Console.Cli)"]
        API["Vigil.Api<br/>(ASP.NET Core, optional)"]
    end
    subgraph APP["Vigil.Application — orchestration"]
        UC["DiagnoseUseCase"]
        ASM["EvidenceAssembler<br/>(rank + cap)"]
        VAL["DiagnosisValidator"]
        QRY["DiagnosisQuery"]
    end
    subgraph DOM["Vigil.Domain — pure core"]
        ENT["Entities & value objects"]
        ABS["Abstractions (interfaces)"]
        SPEC["Specifications"]
    end
    subgraph INFRA["Vigil.Infrastructure — details"]
        INT["Artifact interpreters + selector"]
        REPO["Repositories<br/>(in-memory; EF/SQLite later)"]
        AI["Anthropic adapter / heuristic analyzer"]
        RED["Redactor"]
    end

    CLI --> CLIENT["IVigilClient"]
    API --> CLIENT
    CLIENT -->|in-process default| UC
    UC --> ASM
    UC --> VAL
    UC --> ABS
    ASM --> ABS
    VAL --> ABS
    QRY --> SPEC
    INT -.implements.-> ABS
    REPO -.implements.-> ABS
    AI -.implements.-> ABS
    RED -.implements.-> ABS
    ENT --- ABS
```

---

## Diagram 2 — Dependency direction (compile-time project references)

```mermaid
flowchart LR
    Cli["Vigil.Cli"] --> App["Vigil.Application"]
    Cli -. composition root .-> Infra["Vigil.Infrastructure"]
    Api["Vigil.Api"] --> App
    Api -. composition root .-> Infra
    Infra --> App
    App --> Dom["Vigil.Domain"]
    Infra --> Dom
```

---

## Diagram 3 — Domain model (class diagram)

```mermaid
classDiagram
    class EvidenceArtifact {
        +Guid Id
        +Modality Modality
        +ArtifactKind Kind
        +string? TextContent
        +byte[]? ImageBytes
        +string? MediaType
        +DateTimeOffset? Timestamp
        +ResourceRef? Resource
        +bool HasResource()
    }
    class Diagnosis {
        +Guid Id
        +ResourceRef Subject
        +string Summary
        +AnalyzerProvenance Provenance
        +DateTimeOffset CreatedAt
        +IReadOnlyList~CandidateCause~ Causes
        +CandidateCause TopCause()
    }
    class CandidateCause {
        +string Description
        +string? CausalChain
        +Confidence Confidence
        +Severity Severity
        +CauseCategory Category
        +IReadOnlyList~Citation~ Citations
    }
    class Citation {
        +Guid EvidenceArtifactId
        +string? Snippet
    }
    class AnalyzerProvenance {
        +AnalyzerTier AnalyzedBy
        +FallbackReason? Reason
        +TokenUsage? Usage
    }
    class ResourceRef {
        +string Kind
        +string Identifier
        +bool Equals(ResourceRef other)
    }
    class Confidence {
        +double Value
        +Confidence(double value)
        +int CompareTo(Confidence other)
    }
    class TokenUsage {
        +int InputTokens
        +int OutputTokens
    }

    Diagnosis "1" --> "0..5" CandidateCause
    Diagnosis "1" --> "1" AnalyzerProvenance
    Diagnosis "1" --> "1" ResourceRef
    CandidateCause "1" --> "1..*" Citation
    CandidateCause "1" --> "1" Confidence
    AnalyzerProvenance "1" --> "0..1" TokenUsage

    class Modality {
        <<enumeration>>
        Text
        Image
    }
    class ArtifactKind {
        <<enumeration>>
        LogFile
        ChangeRecord
        Code
        Screenshot
        Config
        StackTrace
        Other
    }
    class Severity {
        <<enumeration>>
        Low
        Medium
        High
        Critical
    }
    class CauseCategory {
        <<enumeration>>
        ConfigChange
        Deployment
        ResourceExhaustion
        Permission
        DependencyFailure
        External
        Other
    }
    class AnalyzerTier {
        <<enumeration>>
        Model
        Heuristic
    }
    class FallbackReason {
        <<enumeration>>
        Timeout
        ApiUnavailable
        OfflineFlag
        NoApiKey
        Refusal
    }
```

---

## Diagram 4 — Analyzer / AI adapter hierarchy (class diagram)

```mermaid
classDiagram
    class IDiagnosisAnalyzer {
        <<interface>>
        +AnalyzeAsync(EvidenceBundle bundle, string symptom) Task~AnalyzerResult~
    }
    class AnthropicDiagnosisAnalyzer {
        -IAnthropicClient client
        -AnthropicOptions options
        +AnalyzeAsync(EvidenceBundle bundle, string symptom) Task~AnalyzerResult~
        -BuildContentBlocks(EvidenceBundle bundle) IReadOnlyList~ContentBlock~
        -BuildDiagnosisTool() ToolSchema
        -MapToRawDiagnosis(ToolUseResponse response) RawDiagnosis
        -DetectMediaType(byte[] bytes) string
    }
    class HeuristicDiagnosisAnalyzer {
        +AnalyzeAsync(EvidenceBundle bundle, string symptom) Task~AnalyzerResult~
        -ScoreByProximity(EvidenceArtifact artifact, DateTimeOffset firstError) double
    }
    class AnthropicOptions {
        +string Model
        +int MaxTokens
        +int TimeoutSeconds
        +double Temperature
        +string ApiKey
    }
    class AnalyzerResult {
        +bool IsSuccess
        +RawDiagnosis? Diagnosis
        +AnalyzerTier Tier
        +FallbackReason? FailureReason
        +TokenUsage? Usage
    }
    class RawDiagnosis {
        +string Summary
        +IReadOnlyList~CandidateCause~ Causes
    }

    IDiagnosisAnalyzer <|.. AnthropicDiagnosisAnalyzer
    IDiagnosisAnalyzer <|.. HeuristicDiagnosisAnalyzer
    AnthropicDiagnosisAnalyzer --> AnthropicOptions
    AnthropicDiagnosisAnalyzer ..> AnalyzerResult : returns
    HeuristicDiagnosisAnalyzer ..> AnalyzerResult : returns
    AnalyzerResult --> RawDiagnosis
```

> **Teaching note (state in the doc, not the diagram):** `IAnthropicClient`, `ContentBlock`, `ToolSchema`, and `ToolUseResponse` are SDK-facing types. They appear **only** as private members/return types of `AnthropicDiagnosisAnalyzer`. That confinement is the Adapter pattern working: no SDK type crosses the `IDiagnosisAnalyzer` seam into Application or Domain.

---

## Diagram 5 — Artifact interpretation: Strategy + Simple Factory + Chain of Responsibility + Template Method (class diagram)

```mermaid
classDiagram
    class IArtifactInterpreter {
        <<interface>>
        +bool CanParse(RawSource source)
        +IEnumerable~EvidenceArtifact~ Interpret(RawSource source)
    }
    class JsonLogInterpreter
    class CsvLogInterpreter
    class SyslogInterpreter
    class PlainTextInterpreter
    class ChangeRecordInterpreter
    class ImageInterpreter

    class ArtifactInterpreterSelector {
        -IEnumerable~IArtifactInterpreter~ interpreters
        +IArtifactInterpreter Select(RawSource source)
    }

    class InterpretationHandler {
        <<abstract>>
        -InterpretationHandler next
        +InterpretationHandler SetNext(InterpretationHandler handler)
        +InterpretationResult Handle(EvidenceArtifact artifact)
        #InterpretationResult Process(EvidenceArtifact artifact)*
    }
    class MetadataExtractionHandler {
        #InterpretationResult Process(EvidenceArtifact artifact)
    }
    class MalformedGuardHandler {
        #InterpretationResult Process(EvidenceArtifact artifact)
    }
    class InterpretationResult {
        +bool Continue
        +EvidenceArtifact? Artifact
        +string? ExclusionReason
    }

    IArtifactInterpreter <|.. JsonLogInterpreter
    IArtifactInterpreter <|.. CsvLogInterpreter
    IArtifactInterpreter <|.. SyslogInterpreter
    IArtifactInterpreter <|.. PlainTextInterpreter
    IArtifactInterpreter <|.. ChangeRecordInterpreter
    IArtifactInterpreter <|.. ImageInterpreter
    ArtifactInterpreterSelector --> IArtifactInterpreter : selects among (Simple Factory)
    InterpretationHandler <|-- MetadataExtractionHandler
    InterpretationHandler <|-- MalformedGuardHandler
    InterpretationHandler --> InterpretationHandler : next (chain)
    InterpretationHandler ..> InterpretationResult
```

> **Design note (state in the doc):** two distinct mechanisms are shown together. (1) `ArtifactInterpreterSelector` picks *one* `IArtifactInterpreter` per source via `CanParse` — **Simple Factory selection over Strategy** (explicitly **not** Factory Method: it selects among existing strategies, it does not defer instantiation to subclasses). (2) `InterpretationHandler` is the **Chain of Responsibility + Template Method** that runs ordered processing stages over the produced artifacts; `Handle` is the fixed template (run `Process`, forward only when `Continue` is true), `Process` is the per-stage hook, and a malformed artifact short-circuits the chain into the exclusions report rather than throwing. The exact set of handler stages is an implementation-time detail to lock in Week 6; the pattern structure is fixed.

---

## Diagram 6 — Application orchestration (class diagram) — headline diagram

```mermaid
classDiagram
    class IVigilClient {
        <<interface>>
        +DiagnoseAsync(DiagnoseRequest request) Task~Diagnosis~
        +QueryHistoryAsync(ISpecification spec) Task~Diagnosis[]~
    }
    class InProcessVigilClient {
        -DiagnoseUseCase diagnoseUseCase
        -DiagnosisQuery query
        +DiagnoseAsync(DiagnoseRequest request) Task~Diagnosis~
        +QueryHistoryAsync(ISpecification spec) Task~Diagnosis[]~
    }
    class DiagnoseUseCase {
        -ArtifactInterpreterSelector selector
        -EvidenceAssembler assembler
        -IRedactor redactor
        -IDiagnosisAnalyzer analyzer
        -DiagnosisValidator validator
        -IDiagnosisRepository repository
        +Execute(DiagnoseRequest request) Task~Diagnosis~
    }
    class EvidenceAssembler {
        +EvidenceBundle Assemble(IEnumerable~EvidenceArtifact~ artifacts, ScopeHints hints)
        -double RankByRelevance(EvidenceArtifact artifact, ScopeHints hints)
        -EvidenceBundle ApplyTokenCap(IEnumerable~EvidenceArtifact~ ranked)
    }
    class DiagnosisValidator {
        -ICitationResolver resolver
        +ValidationResult Validate(RawDiagnosis raw, EvidenceBundle bundle)
    }
    class ICitationResolver {
        <<interface>>
        +bool Resolve(Citation citation, EvidenceBundle bundle)
    }
    class IRedactor {
        <<interface>>
        +EvidenceBundle Redact(EvidenceBundle bundle)
    }
    class IDiagnosisAnalyzer {
        <<interface>>
        +AnalyzeAsync(EvidenceBundle bundle, string symptom) Task~AnalyzerResult~
    }
    class IDiagnosisRepository {
        <<interface>>
        +SaveAsync(Diagnosis diagnosis) Task
        +QueryAsync(ISpecification spec) Task~Diagnosis[]~
    }
    class EvidenceBundle {
        +IReadOnlyList~EvidenceArtifact~ Artifacts
        +ExclusionReport Exclusions
        +string? Symptom
    }
    class DiagnoseRequest {
        +IReadOnlyList~RawSource~ Sources
        +ScopeHints Hints
        +bool Offline
        +bool DryRun
    }
    class ScopeHints {
        +ResourceRef? Resource
        +DateTimeOffset? From
        +DateTimeOffset? To
        +string? Symptom
    }
    class ValidationResult {
        +Diagnosis Diagnosis
        +ValidationReport Report
    }

    IVigilClient <|.. InProcessVigilClient
    InProcessVigilClient --> DiagnoseUseCase
    DiagnoseUseCase --> EvidenceAssembler
    DiagnoseUseCase --> IRedactor
    DiagnoseUseCase --> IDiagnosisAnalyzer
    DiagnoseUseCase --> DiagnosisValidator
    DiagnoseUseCase --> IDiagnosisRepository
    DiagnoseUseCase ..> DiagnoseRequest
    DiagnosisValidator --> ICitationResolver
    DiagnosisValidator ..> ValidationResult
    EvidenceAssembler ..> EvidenceBundle
    DiagnoseRequest --> ScopeHints
```

> **Why this is the headline diagram (state in the doc):** `DiagnoseUseCase` depends only on Domain-owned abstractions (`IRedactor`, `IDiagnosisAnalyzer`, `ICitationResolver`, `IDiagnosisRepository`) — the structural proof of Dependency Inversion. It is also where the instructor's "how does the model perform the action, and how do you verify it" question is answered as *structure*: the model call (`IDiagnosisAnalyzer`) and the deterministic gate (`DiagnosisValidator`) are separate collaborators, so the stochastic step is isolated and the verification step is independently testable.

---

## Diagram 7 — Specification + Composite (class diagram) — optional, strengthens the patterns section

```mermaid
classDiagram
    class ISpecification~T~ {
        <<interface>>
        +Expression ToExpression()
        +bool IsSatisfiedBy(T candidate)
        +ISpecification~T~ And(ISpecification~T~ other)
        +ISpecification~T~ Or(ISpecification~T~ other)
        +ISpecification~T~ Not()
    }
    class AndSpecification~T~
    class OrSpecification~T~
    class NotSpecification~T~
    class CategorySpecification
    class ResourceSpecification
    class SeveritySpecification

    ISpecification~T~ <|.. AndSpecification~T~
    ISpecification~T~ <|.. OrSpecification~T~
    ISpecification~T~ <|.. NotSpecification~T~
    ISpecification~T~ <|.. CategorySpecification
    ISpecification~T~ <|.. ResourceSpecification
    ISpecification~T~ <|.. SeveritySpecification
    AndSpecification~T~ --> "2" ISpecification~T~ : composes
    OrSpecification~T~ --> "2" ISpecification~T~ : composes
    NotSpecification~T~ --> "1" ISpecification~T~ : wraps
```

> **Note (state in the doc):** `ToExpression()` returns `Expression<Func<T, bool>>` — the EF-Core-translatable form, shown here as `Expression` for parser safety. The And/Or/Not specifications **hold** other `ISpecification<T>` instances, which is the Composite relationship. Combining the underlying expression trees requires parameter rebinding via an `ExpressionVisitor` (not `Expression.Invoke`, which EF Core cannot translate).

---

## Diagram 8 — The diagnose pipeline (sequence diagram)

```mermaid
sequenceDiagram
    actor Eng as Engineer (CLI)
    participant Cmd as DiagnoseCommand
    participant Cl as IVigilClient
    participant UC as DiagnoseUseCase
    participant Int as IArtifactInterpreter
    participant Asm as EvidenceAssembler
    participant Red as IRedactor
    participant An as IDiagnosisAnalyzer
    participant Val as DiagnosisValidator
    participant Repo as IDiagnosisRepository

    Eng->>Cmd: pipe evidence + flags
    Cmd->>Cl: DiagnoseAsync(request)
    Cl->>UC: Execute(request)
    UC->>Int: Interpret(rawSources)
    Int-->>UC: EvidenceArtifacts
    UC->>Asm: Assemble + rank + cap
    Asm-->>UC: EvidenceBundle (+ exclusions)
    UC->>Red: Redact(bundle)
    Red-->>UC: redacted bundle
    UC->>An: AnalyzeAsync(redacted bundle, symptom)
    An-->>UC: AnalyzerResult (model) or typed failure
    Note over UC,An: on typed failure - fall back to heuristic, stamp provenance
    UC->>Val: Validate(raw, bundle)
    Val-->>UC: Diagnosis (+ validation report)
    UC->>Repo: SaveAsync(diagnosis)
    UC-->>Cmd: Diagnosis
    Cmd-->>Eng: render (loud provenance if heuristic) or --json
```

---

## Diagram 9 — Implementation schedule (Gantt)

> **Adjust the dates to your actual calendar.** Week 5 = this design doc (in progress); Weeks 6 and 7 are the two build iterations. The plan is sequenced so each week ends with something demonstrable.

```mermaid
gantt
    title Vigil Implementation Plan (3 Weeks)
    dateFormat YYYY-MM-DD
    axisFormat %b %d

    section Week 5 - Detailed Design
    This design document             :done,   w5a, 2026-06-02, 6d
    Diagnosis tool-use schema lock   :active, w5b, 2026-06-06, 2d
    Interface signatures finalized   :        w5c, 2026-06-06, 2d

    section Week 6 - Iteration 1 (text-only diagnose loop)
    Domain model                     :w6a, 2026-06-09, 2d
    Interpreters + selector + CoR    :w6b, after w6a, 2d
    Assembler (rank + cap)           :w6c, after w6b, 1d
    In-memory repositories           :w6d, after w6b, 1d
    Anthropic adapter (tool use)     :w6e, after w6c, 2d
    Heuristic analyzer               :w6f, after w6c, 1d
    Validation gate                  :w6g, after w6e, 1d
    CLI diagnose + history           :w6h, after w6g, 1d

    section Week 7 - Iteration 2 (multimodal + hardening)
    ImageInterpreter + multimodal    :w7a, 2026-06-16, 2d
    Redactor (text) + image stance   :w7b, after w7a, 1d
    Provenance render + token usage  :w7c, after w7b, 1d
    Recorded-response boundary tests :w7d, after w7c, 2d
    Stretch - v1.1 alerting seam     :crit, w7e, after w7d, 1d
```
