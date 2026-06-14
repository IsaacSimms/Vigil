# Vigil — Project Recap (Design Phase, Weeks 4–5)

**Date:** 2026-06-13
**Type:** project (multi-thread design arc → build-ready)
**Environment / Systems:** C# / .NET 8, Spectre.Console.Cli, official `Anthropic` NuGet SDK (beta, pinned), EF Core + SQLite (deferred), xUnit, Clean/Onion architecture
**Course:** COSC 438 (Object-Oriented Design) — solo project, "progress over completion" grading

## TL;DR
Vigil went from a Week-4 "observability / drift-detection engine" to a finalized, build-ready **reactive incident-diagnosis engine** — an engineer supplies evidence about a known incident and gets back a ranked, cited causal explanation. Across four threads the architecture was drafted, critically reviewed, pivoted in response to instructor feedback, pressure-tested via `/grill-me`, and fully documented. The design is locked; Weeks 6–7 are the two build iterations.

## Context & Goal
Vigil serves two goals at once, kept deliberately separate in the artifacts: a **graded course deliverable** (design quality and architectural correctness outrank production functionality; synthetic data is acceptable) and a **real product** Isaac intends to keep building after the course. Product-focused docs read as if Vigil is purely a system being stood up; course-specific material is isolated to dedicated notes files.

Anchoring thesis: **model as infrastructure, not conversation** — backed by six differentiators from a raw chat window (trust contract, data governance, evidence assembly at scale, repeatability/integration, institutional memory, normalization).

## Key Points Explored

**Thread 1 — Initial design (2026-06-02).** Drafted Vigil as an infrastructure *observability engine* (ingest change records + logs, query, AI-assisted anomaly detection, rule-based alerting). A mid-session critical review caught five substantive design defects in the first draft:
- A pipeline mislabeled as Chain of Responsibility (no genuine short-circuit).
- A Specification pattern that conflicted with EF Core translatability.
- `AlertRule` modeled three contradictory ways.
- Factory Method incorrectly named (was actually Simple Factory).
- Sequence diagram contradicting UC4's on-demand detection model.

All five were corrected into `Vigil-InitialDesign-v2.md`. Stack, Clean Architecture layering, and a ten-pattern committed design were selected here. Also covered: conceptual Q&A (Spectre.Console, EF Core, testing without enterprise infra, Azure hosting, GitHub repo description, MIT vs Apache 2.0 vs GPL-3.0). Confirmed the project is intentionally solo despite being scoped for a 2–3 person team.

**Thread 2 — Verifiability pivot (≈2026-06-07).** Instructor feedback asked for two things: greater specificity about **how the model performs each action**, and a **verifiable/validatable** design. Forcing specificity surfaced the core correction — the problem framing was wrong. Pivoted from drift/anomaly *monitoring* to reactive incident *diagnosis*. This is not cosmetic: it changes the primary output (a `Diagnosis` with ranked `CandidateCause`s vs. a continuous deviation score), what a successful run is (a grounded causal story vs. a drift number), and what the model is asked to do (correlate heterogeneous evidence into a causal narrative vs. detect drift from a baseline). The onion architecture stayed intact — same machine, clearer problem. The verifiability requirement drove the deterministic `DiagnosisValidator` gate. Pivot framed as **Option A** (confident sharpening — own it, attribute it to the feedback, no apology).

**Thread 3 — Grilling + lock (2026-06-07).** Ran `/grill-me` ten questions deep to pressure-test and finalize every open decision. The complete architecture was locked here (see Decisions). Established the **standing working agreement**: explicit approval required before creating or modifying any files.

**Thread 4 — Detailed design (2026-06-08).** Week-5 deliverable. Produced the nine-diagram Mermaid set and the 13-page detailed-design reference doc. Resolved the `Vigil.Api` clarification: it is Isaac's optional ASP.NET HTTP surface, **"designed for, not built in v1"** — distinct from the Anthropic API, which is the model tier's external dependency with its own offline fallback path.

## Decisions & Outcomes

**Identity.** Reactive incident-diagnosis engine. Takes heterogeneous evidence about a *known* incident (logs, change records, code, configs, stack traces, screenshots) → returns a governed, cited, ranked diagnosis. Not a drift monitor; change records are one `Kind` among many.

**Architecture.** Onion/Clean, five projects, dependencies inward only, enforced by project references:
- `Vigil.Domain` — pure core, no external refs. Entities, value objects, specifications, all abstractions.
- `Vigil.Application` — orchestration; depends only on Domain. Diagnose use case, evidence assembler, diagnosis query.
- `Vigil.Infrastructure` — concrete details (interpreters, repositories, Anthropic adapter + heuristic analyzer, redactor). Depends on Application + Domain.
- `Vigil.Api` (optional) + `Vigil.Cli` — presentation. Infrastructure referenced only here, only for DI wiring at the composition root. Zero business logic in presentation.

**Domain model.** Entities: `EvidenceArtifact` (`Modality` Text|Image, `ArtifactKind`, content, metadata), `Diagnosis` (subject `ResourceRef`, summary, `AnalyzerProvenance`, ranked `CandidateCause` list ≤5). Value objects: `CandidateCause` (description, causal-chain narrative, `Confidence`, `Severity`, `CauseCategory`, citations), `Citation` (`EvidenceArtifactId` + optional snippet), `AnalyzerProvenance` (`AnalyzedBy` Model|Heuristic, `FallbackReason`, `TokenUsage`), `ResourceRef`, `Confidence` (enforces 0–1 at construction). **Retired** drift-era concepts: `Anomaly`, `DeviationScore` (parked for a future drift mode), `AnalysisSubject`.

**The Strategy (UL) seam.** `IDiagnosisAnalyzer` is the Seam (UL): a multimodal Anthropic Adapter (UL) as the primary Implementation (UL), and a deliberately minimal proximity-heuristic as the offline fallback. This is a deep Module (UL) — large behavior behind a small Interface (UL).

**Validation gate.** `DiagnosisValidator` is a deterministic gate. `CandidateCause` capped at ≤5, enforced twice. Citations require a UUID `evidence_artifact_id` resolving to a real artifact in the assembled bundle. Confidence bounded `[0,1]`. The thesis in one line: **the model judges; the system constrains and verifies.**

**AI integration.** Pinned `report_diagnosis` tool-use JSON schema (`maxItems: 5`, bounded confidence, UUID-keyed citations). Multimodal content blocks. `AnthropicOptions` resolved at the composition root via environment variables or .NET User Secrets — **never hard-coded**. Honest no-auto-redact stance for images; redaction before egress via `IRedactor`. Anthropic SDK is beta — pin the version (note: package IDs ≤3.x are the unofficial `tryAGI.Anthropic`, do not confuse).

**Transport.** In-process via `IVigilClient` / `InProcessVigilClient` is the v1 default (serverless; natural shape for a future desktop GUI). `HttpVigilClient` over `Vigil.Api` is designed-for but not built in v1.

**Patterns locked.** Strategy, Simple Factory, Chain of Responsibility (genuine short-circuit), Template Method, Adapter, Command, Repository, Specification (Composite). Observer deferred to v1.1.

**Keystone test.** Demonstrate the model boundary without a live call: feed the adapter/validator a recorded model response over a known bundle where one cause cites a real artifact ID and one cites a fake ID → assert exactly one `CandidateCause` survives, exactly one validation failure recorded, confidence in range, `AnalyzedBy = Model`. A six-cause recorded response asserts truncation to five after citation validation. A simulated SDK failure asserts fallback to the heuristic tier with `FallbackReason` stamped.

## Open Questions / Next Steps
- **Weeks 6–7 build iterations.** Iteration 1: text-only diagnose loop — domain model; text interpreters (JSON, CSV, syslog, plain text) + `ChangeRecordInterpreter`; `ArtifactInterpreterSelector`; CoR + Template Method base; `EvidenceAssembler` (rank + cap); in-memory repositories; Anthropic adapter (tool-use, text blocks only); proximity heuristic; full `DiagnosisValidator` gate; `AnthropicOptions` + secrets + no-key→offline; CLI `diagnose` (stdin + flags, `--offline`, `--json`, `--dry-run`); `vigil history`.
- **Implementation-time items to lock** (identified in the detailed design §4B/§7) — light sanity pass, do not re-litigate settled decisions.
- **Deferred to v1.1:** `Vigil.Api` + alerting infrastructure (seams reserved), Observer pattern, EF Core/SQLite swap behind the existing Repository interface.
- **Multimodal image support** planned for v1 proper.
- **Longer-term:** post-course product development.

## Artifacts
All finalized unless noted.
- `Vigil-InitialDesign-v2.md` — Week-4 initial design (superseded by the systems design after the pivot, but the source of the corrected pattern set).
- `Vigil-SystemsDesign.md` — authoritative product spec. Architecture, layered + dependency diagrams, domain class diagram, analyzer/interpreter diagrams, diagnose sequence, validation gate, patterns table, roadmap. Reads as pure system, no course references.
- `Vigil-ClassProjectNotes.md` — course-specific layer: instructor-feedback mapping, rewritten use cases, pattern justifications, SOLID alignment, V&V test plan, weekly plan. Human-review only, not for an implementation agent.
- `Vigil-ThreadHandoff.md` — ideation→implementation handoff companion to the systems design; course references stripped.
- `Vigil-Diagrams-Mermaid.md` — nine Lucidchart-compatible Mermaid code blocks (component/layer, dependency direction, domain model, analyzer/AI hierarchy, artifact interpretation [Strategy + Simple Factory + CoR + Template Method], application orchestration [headline], Specification + Composite, diagnose sequence, implementation Gantt).
- `Vigil-DetailedDesign.docx` — 13-page, 12-section Week-5 reference. Isaac authors his own final Word submission from this; the docx is reference only, not submitted directly.
