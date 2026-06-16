# Architecture Improvements: Proximity Scoring Unification & Dry-Run Removal

**Date:** 2026-06-16
**Type:** refactor

## TL;DR

Two architectural improvements made to the Vigil codebase: (1) duplicated temporal proximity scoring was unified into a single canonical `ArtifactRelevanceScorer` module in Domain, and (2) the `--dry-run` flag was removed entirely as a useless duplicate of `--offline` that leaked presentation concerns into the core use case.

## Context & Goal

Session opened with a codebase audit to check whether `AGENTS.md` and `CONTEXT.md` were current. After updating those docs, the `/improve-codebase-architecture` skill was run to surface deepening opportunities. Five candidates were presented; two were implemented in this session.

## Key Points Explored

**Doc audit:**
- `CONTEXT.md` showed only `DiagnoseAsync` and `QueryHistoryAsync` on `IVigilClient` — missing the `ConsultAsync` method added during the TUI implementation.
- `IGrillAdvisor` was a live Domain seam with a real Infrastructure adapter (`GrokGrillAdvisor`) but was not documented anywhere in `AGENTS.md` or `CONTEXT.md`.

**Proximity scoring duplication:**
- Identical formula `1000 / (1 + delta.TotalSeconds)` appeared independently in `EvidenceAssembler` (Application) and `HeuristicDiagnosisAnalyzer` (Infrastructure).
- Bonus weights had drifted: ChangeRecord was +300 in the assembler, +500 in the heuristic; resource logic had different semantics in each.
- Because `EvidenceBundle` only carried `string? Symptom`, the heuristic couldn't access `ScopeHints.From` or `ScopeHints.Resource` — so it derived a `firstError` approximation from artifact timestamps instead of using the engineer's declared incident window.

**Dry-run analysis:**
- `DiagnoseUseCase.Execute` checked `request.DryRun` and short-circuited to a heuristic analysis — producing output identical to `--offline`. The only difference was a "DRY-RUN PREVIEW" label in the renderer.
- Confirmed the flag was pure accidental complexity: it duplicated `--offline` while living in the wrong layer (core use case, not presentation).

## Decisions & Outcomes

**Doc updates (`AGENTS.md`, `CONTEXT.md`):**
- Added `IGrillAdvisor` as a 4th key seam in `CONTEXT.md` with its interface signature, both adapters, and no-key fallback behavior.
- Updated `IVigilClient` interface in `CONTEXT.md` to include `ConsultAsync`.
- Added `IGrillAdvisor` to the primary seams list in `AGENTS.md`.

**`ArtifactRelevanceScorer` (new — `Vigil.Domain/ArtifactRelevanceScorer.cs`):**
- Pure static module in Domain. No interface, no seam — one formula, no variation axis across callers.
- Canonical weights: temporal `1000 / (1 + delta)`, ChangeRecord +500, resource match +500, resource presence +100 (no target), wrong/absent resource gets nothing.
- `EvidenceBundle` changed from `string? Symptom` to `ScopeHints? Hints` so the heuristic analyzer (and any future analyzers) receive the full incident window and resource scope the assembler used for ranking.
- `EvidenceAssembler.RankByRelevance` reduced to a one-line delegation to the scorer.
- `HeuristicDiagnosisAnalyzer.ScoreByProximity` deleted; uses `bundle.Hints?.From ?? firstError` as reference time, `bundle.Hints?.Resource` as target resource.
- `TextRedactor` updated to pass `bundle.Hints` through on bundle reconstruction.
- 6 TDD tests added covering: exact timestamp match, ChangeRecord bonus, resource match vs. non-match, presence bonus with no target, null reference time, and null artifact timestamp.

**Dry-run removal:**
- `bool DryRun` removed from `DiagnoseRequest`.
- Dry-run short-circuit block removed from `DiagnoseUseCase.Execute`.
- `DiagnoseCommandArgs.DryRun` removed; `--dry-run`/`--dryrun` flag parsing removed from `GrillInteractive.ParseDiagnoseFlagsWithRemainder`.
- `[CommandOption("--dry-run")]` removed from `DiagnoseCommand.Settings`; `RenderHuman` simplified (no conditional header or footer).
- `RenderFullDiagnosisInSession` in `Program.cs` simplified; `/help` text updated.
- All `DiagnoseCommandArgs` constructor calls in tests updated; `--dry-run` removed from flag parsing test.

**Verification:** 48/48 tests pass, zero build errors, across both changes.

## Open Questions / Next Steps

Three remaining candidates from the `/im` session:
1. **Deepen `ICitationResolver`** — `SimpleCitationResolver` is a one-liner existence check; the seam is real but too shallow to express temporal/resource grounding in tests.
2. **Deepen the Grok adapter** — the `report_diagnosis` tool schema lives as a raw JSON string alongside manual `GetProperty()` deserialization; a `DiagnosisToolSchema` sub-module would make both independently testable.
3. **Interpreter pipeline outcomes** — failed interpretations (e.g. malformed JSON logs) are currently silent; no seam exists to observe what happened to each source.

## Artifacts

- `Vigil.Domain/ArtifactRelevanceScorer.cs` — new file
- `Vigil.Domain/Models/EvidenceBundle.cs` — `string? Symptom` → `ScopeHints? Hints`
- `Vigil.Domain/Models/DiagnoseRequest.cs` — `bool DryRun` removed
- `Vigil.Application/Coordinators/EvidenceAssembler.cs` — delegates to scorer
- `Vigil.Infrastructure/HeuristicDiagnosisAnalyzer.cs` — uses scorer + bundle hints; `ScoreByProximity` deleted
- `Vigil.Infrastructure/Redactors/TextRedactor.cs` — passes `bundle.Hints` through
- `Vigil.Application/UseCases/DiagnoseUseCase.cs` — dry-run block removed
- `Vigil.Application/GrillInteractive.cs` — `DryRun` removed from args record and parser
- `Vigil.Cli/Commands/DiagnoseCommand.cs` — flag and conditional rendering removed
- `Vigil.Cli/Program.cs` — renderer simplified, help text updated
- `CONTEXT.md` — `IGrillAdvisor` seam documented; `IVigilClient` interface corrected
- `AGENTS.md` — `IGrillAdvisor` added to primary seams list
