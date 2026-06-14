# Vigil — Project Context (for Agents, Developers & Green field Implementation)

**Status:** Design core finalized; primary workflow evolved to interactive Grill-me TUI (see README for current usage; docs/ for core design).  
**Architecture:** Strict Clean/Onion (five projects, inward-only dependencies enforced by references). Heart preserved (seams, testable core, determinism around model).  
**Stack (v1):** C# / .NET 8, Spectre.Console (TUI + .Cli for compat), official OpenAI NuGet (xAI/Grok via base URL + XAI_API_KEY), xUnit + FluentAssertions, in-memory repositories (EF Core + SQLite deferred).  
Primary experience: bare `vigil` launches agentic TUI with NL to Grok + easy interleaving of governed diagnoses. One-shot `diagnose` preserved for scripts.

This file is a concise, scannable summary extracted directly from the locked design artifacts. **Always read the primary sources first** before implementation work:

- `docs/Vigil-SystemsDesign.md` (the product spec: principles, domain model, pipelines, seams, validation, patterns table, roadmap).
- `docs/Vigil-Architecture-Diagrams-Mermaid.md` (9 diagrams: layers, dependencies, domain model, analyzer hierarchy, interpreters, IVigilClient orchestration, sequence, Gantt, Specification+Composite).
- `docs/2026-06-13-vigil-project-PreImplementation-recap.md` (history + key decisions).

Everything below uses the Ubiquitous Language (UL) from the design (Seam (UL), Adapter (UL), Strategy (UL), Module (UL), Depth (UL), Leverage (UL), Locality (UL), etc.). Use these terms exactly.

## Five-Project Structure

Five projects across four layers. Presentation references Application for contracts and Infrastructure **exclusively** at the composition root for DI wiring. No business logic lives outside Application + Domain.

| Project              | Layer          | Depends On                  | Responsibilities (high level) |
|----------------------|----------------|-----------------------------|-------------------------------|
| Vigil.Domain        | Core (pure)   | Nothing (zero externals)   | Entities (EvidenceArtifact, Diagnosis, CandidateCause), value objects (Confidence, Citation, AnalyzerProvenance, ResourceRef, TokenUsage), enums (Modality, ArtifactKind, Severity, CauseCategory, AnalyzerTier, FallbackReason), Specifications, all abstractions (interfaces). Stable center. |
| Vigil.Application   | Orchestration | Vigil.Domain only          | DiagnoseUseCase, EvidenceAssembler (rank + cap + ExclusionReport), DiagnosisValidator (deterministic gate), DiagnosisQuery, supporting coordinators. |
| Vigil.Infrastructure| Details       | Application + Domain       | Artifact interpreters + selector, repositories (in-memory first), GrokDiagnosisAnalyzer (primary IDiagnosisAnalyzer impl) + HeuristicDiagnosisAnalyzer, IRedactor impl, GrokOptions, any future EF/SQLite repos. All third-party SDK types (OpenAIClient etc.) confined here. |
| Vigil.Cli           | Presentation  | Application (Infra only for composition root) | Spectre.Console.Cli commands (DiagnoseCommand, HistoryCommand, ...). Gathers input (stdin + flags), calls IVigilClient, renders (human or --json). |
| Vigil.Api           | Presentation (optional) | Application (Infra only for composition root) | ASP.NET Core surface (HttpVigilClient transport). "Designed for, not built in v1". |

**Dependency diagram (text form of Diagram 2):**
```
Cli ──> Application
Cli -. composition root .-> Infrastructure
Api ──> Application
Api -. composition root .-> Infrastructure
Infrastructure ──> Application ──> Domain
Infrastructure ──> Domain
```

## Strict Dependency / Onion Rules

- **Dependencies point inward only.** This is the structural expression of the Dependency Inversion Principle (UL).
- Enforced at compile time by project references (not convention or "architecture tests" alone).
- Domain literally cannot see frameworks, NuGet packages, or any outer layer because it has no references.
- Application never sees Infrastructure.
- Presentation never contains business logic and never directly new's Infrastructure types except inside the single composition root (Program.cs / DI bootstrap).
- The "load-bearing rule": a presentation component gathers raw input, invokes the core seam (IVigilClient), and renders the result. Nothing else.

## Key Seams (Primary Points of Variation)

Seams (UL) are the locations of the Module (UL) Interfaces (UL). They enable swapping Implementations (UL) (e.g. providers, formats, transports) without touching callers. The design deliberately makes these **deep** (UL): small surface, large verified behavior behind them → Leverage (UL) for callers + Locality (UL) for maintainers.

### 1. IDiagnosisAnalyzer (Domain) — The Strategy (UL) + Analyzer Seam
```csharp
public interface IDiagnosisAnalyzer
{
    Task<AnalyzerResult> AnalyzeAsync(EvidenceBundle bundle, string? symptom);
}
```

- **Primary Implementation (UL):** `GrokDiagnosisAnalyzer` (Infrastructure). Multimodal (text + images), structured output via tool use / function calling against the pinned `report_diagnosis` JSON schema (≤5 causes, bounded confidence, UUID citations). Uses the official OpenAI NuGet client configured for xAI (`base URL https://api.x.ai/v1`, `XAI_API_KEY`). All OpenAI SDK types (`OpenAIClient`, `ChatCompletion`, `ChatTool`, content parts, ...) are private to this class only — they never cross the seam.
- **Fallback / Alternative Implementation (UL):** `HeuristicDiagnosisAnalyzer` (proximity + resource matching on ChangeRecords/timestamped artifacts). Deliberately minimal, templated, zero external cost. Satisfies identical contract (Liskov). Used for `--offline`, on typed model failure, and as deterministic test double.
- **Why this seam earns its keep (Depth (UL)):** 
  - Model is the only stochastic element; everything around it (EvidenceAssembler, Validator, provenance) is deterministic and testable.
  - Enables governance (redaction before egress, --offline path), cost bounding, honest provenance (`AnalyzedBy = Model|Heuristic`, `FallbackReason`, `TokenUsage`).
  - Keystone tests can feed recorded responses + assert validation behavior with zero live calls.
- See: SystemsDesign §7 (full Adapter description + config), §9 (heuristic rationale), Diagrams 4 & 6, teaching note on SDK confinement.

### 2. IArtifactInterpreter (Domain) — Strategy (UL) for Heterogeneous Input
```csharp
public interface IArtifactInterpreter
{
    bool CanParse(RawSource source);
    IEnumerable<EvidenceArtifact> Interpret(RawSource source);
}
```

- Concrete Implementations (UL) (selected at runtime): `JsonLogInterpreter`, `CsvLogInterpreter`, `SyslogInterpreter`, `PlainTextInterpreter`, `ChangeRecordInterpreter`, `ImageInterpreter`.
- Selection: `ArtifactInterpreterSelector` (Simple Factory (UL) selection over strategies via `CanParse`; explicitly **not** GoF Factory Method).
- Post-interpret processing: Often wrapped in Chain of Responsibility (UL) + Template Method (UL) (`InterpretationHandler` base + `MetadataExtractionHandler`, `MalformedGuardHandler`, ...). Short-circuit on malformed; produces exclusions report.
- Axis of change isolated: input *format* varies independently of the rest of the pipeline. Engineer never declares format (except override); "what broke" is declared, not "what shape the bytes are".
- See: SystemsDesign §10, Diagram 5 (with design note), patterns table.

### 3. IVigilClient (Domain) — Transport / Presentation-to-Core Seam
```csharp
public interface IVigilClient
{
    Task<Diagnosis> DiagnoseAsync(DiagnoseRequest request);
    Task<Diagnosis[]> QueryHistoryAsync(ISpecification<Diagnosis> spec);
}
```

- **Default Implementation (UL):** `InProcessVigilClient` — wires `DiagnoseUseCase` (and collaborators: assembler, redactor, analyzer, validator, repo) inside the same process. Natural for CLI and future desktop GUI embedding.
- **Alternative:** `HttpVigilClient` (over optional `Vigil.Api`).
- Swapping is a composition-root decision only. Presentation never knows or cares which transport is active.
- **The load-bearing rule (repeated for emphasis):** Presentation carries **zero business logic**. Command → client.DiagnoseAsync(...) → render (tree or --json). This is what keeps future UIs additive rather than rewrites.
- See: SystemsDesign §3 (Transport), Diagram 6 (headline orchestration), sequence diagram (Diagram 8).

**Other Domain-owned seams/abstractions** (supporting the above): `IRedactor`, `IDiagnosisRepository`, `ICitationResolver`, `ISpecification<T>` (Composite (UL) form with `And`/`Or`/`Not` + `ToExpression` for EF later), `ArtifactInterpreterSelector`, `DiagnosisValidator`, etc.

## Other Non-Negotiables Extracted from Design

- **Determinism around the stochastic core:** DiagnosisValidator gate (deserialize → resolve citations against real bundle artifacts → drop/strip ungrounded → rank/truncate to ≤5 → emit Diagnosis + ValidationReport). "The model judges; the system constrains and verifies."
- **Redaction & security:** `IRedactor` runs in Application immediately before the analyzer call (the true egress). Images honest stance in v1 (not auto-redacted; --offline refuses them). Secrets never in repo; only at composition root via env / User Secrets / Key Vault. `XAI_API_KEY` for the model tier.
- **Evidence model & citations:** Every `CandidateCause` that survives validation has ≥1 real `Citation` (EvidenceArtifactId) that resolves in the assembled `EvidenceBundle`. `Citation.Snippet` is for humans only.
- **Cost & bounds:** Rank-and-cap in assembler; model requested ≤5; max-tokens; usage captured in `AnalyzerProvenance`.
- **Patterns earned (see §12 table):** Only where an axis of change is isolated (Strategy for interpreters + analyzer, Adapter for Grok SDK, Simple Factory selection, CoR+Template for interp pipeline, Repository, Specification+Composite, Command, Observer v1.1).
- **Error philosophy:** Expected problems (bad citations, parse fails, cap) flow through ValidationResult/ExclusionReport (testable, no throw). Only truly exceptional errors become exceptions (and even then, SDK exceptions stop at Infrastructure boundary).
- **v1 scope:** Full diagnose loop (text+image), Grok + heuristic with honest provenance, validation, in-memory persistence + query, CLI (stdin/flags, --offline/--json/--dry-run). Api and EF deferred.

## Quick Start for Implementation (Green field)

1. Re-read the exact section of SystemsDesign + relevant diagram(s) for the slice you're touching.
2. Follow the rules in the sibling `AGENTS.md`.
3. Prefer refactoring/extending existing Modules (UL) over new files.
4. TDD (xUnit + FluentAssertions) for anything non-trivial.
5. All C# business logic; PowerShell strictly for data collection pipelines that feed the tool.
6. When you touch code, start important classes/methods/blocks with the title comment convention.
7. Keep the design's Depth (UL) — do not leak SDK types, do not put logic in presenters, do not bypass seams.

This CONTEXT.md exists to give rapid orientation while forcing re-consultation of the full design docs for precision. The design is intentionally locked; progress over perfection, but architecture fidelity first.

---

*Generated from full scan of the design docs on 2026-06-13. Update this file only when the underlying SystemsDesign or diagrams change.*
