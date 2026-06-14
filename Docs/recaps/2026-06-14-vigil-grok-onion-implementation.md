# Vigil xAI/Grok Implementation and Onion Architecture

**Date:** 2026-06-14
**Type:** implementation
**Environment / Systems:** C# / .NET 8, Windows, PowerShell, xAI Grok API (via official OpenAI NuGet SDK for compatibility)
**People / Teams:** solo project (Isaac)

## TL;DR
The thread greenfielded the Vigil incident-diagnosis CLI tool by switching the backend from Anthropic/Claude to xAI/Grok, created supporting project docs (CONTEXT.md, AGENTS.md), and implemented the full v1 Clean/Onion architecture in .NET 8. This included the complete Domain model and seams, Application orchestration, Infrastructure with interpreters/CoR/Grok adapter/heuristic/repo/redactor, CLI with DI and Spectre commands, sample TestFiles, multiple README and plan updates for local PS usability, warning fixes, and end-to-end verification that the diagnose loop works with piped mock data (falling back to heuristic when no key).

## Context & Goal
The repo started as bare-bones .NET projects following the pre-existing design in Docs/ (Vigil-SystemsDesign.md, Architecture-Diagrams-Mermaid.md, recap). The user wanted to proceed with the greenfield implementation per the approved plan (no further approvals needed after initial), but replace the original Anthropic integration with xAI/Grok (as the AI was switched in docs earlier). Primary goal: deliver a working v1 CLI that can take evidence via pipe/flags, produce cited diagnoses, respect the onion rules, use TDD, and be easy to run/test locally on Windows/PowerShell. Key constraint: key only via XAI_API_KEY env var (never in repo or code).

## Key Points Explored
- Switched all references in design docs from Anthropic/Claude to xAI/Grok (adapter name, options, env var, SDK usage via OpenAI compat at https://api.x.ai/v1, tool-use for report_diagnosis schema).
- Created root CONTEXT.md (five-project onion summary, key seams IDiagnosisAnalyzer/IArtifactInterpreter/IVigilClient, UL terms) and AGENTS.md (onion rules, C# business logic only, xUnit+FluentAssertions+TDD, Spectre.Cli thin, title comments for important blocks, etc.).
- Explored and confirmed initial structure (4 projects, correct refs, placeholders) against the design; identified gaps in models/abstractions/seams.
- Phase-by-phase implementation per plan:
  - Domain: full entities/VOs/enums from Diagram 3/§4 (EvidenceArtifact, Diagnosis, etc. with invariants like Confidence 0-1), all abstractions/seams from Diagrams 4-7/§3/7/10/11 (interfaces + composites), minimal aux types (RawSource, bundles, reports) per grill-me.
  - Application: UseCase (orchestration with interpret/assemble/redact/analyze/validate/persist; later enhanced for DryRun/Offline), EvidenceAssembler (rank+cap), DiagnosisValidator (citation resolve/drop/strip/truncate≤5 + reports per §6), DiagnosisQuery.
  - Infrastructure: GrokOptions, basic text interpreters (PlainText, JsonLog, ChangeRecord) + selector + CoR handlers (InterpretationHandler + Metadata/MalformedGuard per §10/Diagram 5), HeuristicDiagnosisAnalyzer (proximity Liskov sub per §9), InMemory repo, TextRedactor (regex secrets per §8), GrokDiagnosisAnalyzer (text-only, tool definition with pinned schema, mapping to RawDiagnosis, usage capture, failure reasons; all OpenAI types confined inside).
  - CLI: Program.cs with full DI (ServiceCollection for interpreters/selector/assembler/redactor/analyzers choice by key/repo/UseCase/client), thin DiagnoseCommand (Spectre, flags, stdin via pipe, render tree/json), History stub, InProcessVigilClient.
- Test data: Created Docs/TestFiles with 4 example subfolders (SimpleLogIncident, JsonLogsDeployment, CsvAndSyslog, ComplexWithConfigAndChanges) containing realistic mock logs/changes/config/JSON/syslog for testing interpreters and full pipeline.
- Documentation and usability: Multiple README.md iterations (better local setup, key handling, PS-specific commands using `type file |` for stdin instead of `<` which fails in PowerShell, references to TestFiles and design docs). plan.md maintained with phase execution reports. CONTEXT/AGENTS updated as needed.
- Key handling: Exclusively via `XAI_API_KEY` env var (temp $env: or permanent [Environment]::Set... "User" in registry); never in code/files/repo/.gitignore covers *.env. Grok chosen only if present, else heuristic.
- Fixes and verification: Nullable warnings (CS8618 on _next field made nullable; CS8603 on null return changed to throw); 10/10 tests passing (model invariants, seams, interpreters, heuristic, validator, repo, full loop); builds clean; onion verified via dotnet list reference; end-to-end via samples (e.g. type TestFiles/... | dotnet run ... --diagnose --symptom ... producing JSON/tree with citations/provenance).
- Exploration: Structure analysis, git status for untracked TestFiles (intentionally added as samples), key location (registry for User scope), PS vs cmd differences for redirection/piping.

## Decisions & Outcomes
- Strictly followed the approved plan's phases and grill-me clarifications (use provided docs/Mermaid as truth for shapes; folders from start; early tests; DI with Microsoft.Extensions; folders/namespaces; TestFiles early; schema derivation in adapter).
- TDD for non-trivial (failing tests first for invariants, contracts, cross-seam behavior, loop); title comments only on important blocks (not minor fixes like nullable tweaks).
- Key never in repo (env var only); production via same mechanism or secret manager.
- All code follows AGENTS (onion, C# only, xUnit+Fluent, Spectre thin, comments per global rules).
- v1 text-only loop functional: interpreters/selector/assemble/redact/analyze (Grok or heuristic)/validate/persist/query via CLI with flags (--offline/--dry-run/--json/--symptom), stdin pipe from samples, provenance, citations, caps.
- Documentation (README, CONTEXT, AGENTS, plan.md with reports, this recap) makes the project easy to onboard and test locally without real data or key.
- No unapproved arch changes; everything references design.

## Open Questions / Next Steps
- Full Phase 6 hardening (recorded/keystone tests specifically for Grok boundary without live key, more provenance/token render polish, full redactor for images if pushing multimodal).
- Real key testing and production wiring (Key Vault etc.).
- Potential CLI enhancements (better source arg handling vs stdin, history queries).
- Whether to commit TestFiles (they were intentionally added as user samples and referenced in README; git showed them as untracked in one view).

## Artifacts
- **Source:** Domain (full models/seams per design), Application (UseCase/Assembler/Validator/Query + clients), Infrastructure (interpreters/CoR/Grok+heuristic/ repo/redactor/GrokOptions + adapter), Cli (Program DI + DiagnoseCommand + InProcessVigilClient), Tests (10 passing, including full loop).
- **Docs:** Docs/recaps/2026-06-14-vigil-grok-onion-implementation.md (this file); Docs/TestFiles/ (4 subfolders with mock logs/changes/config/JSON/syslog for testing); CONTEXT.md; AGENTS.md; multiple README.md iterations; plan.md (with phase reports); original design (SystemsDesign.md, Architecture-Diagrams-Mermaid.md, recap).
- **Config/Build:** .slnx; csprojs with refs/packages (OpenAI 2.0.0, DI 8.0, Spectre 0.49, xunit/FluentAssertions); successful `dotnet build` / `dotnet test` / `dotnet run --project Vigil.Cli` with samples.
- **Commands/Examples:** PS env var setup for key; `type Docs\TestFiles\... | dotnet run --project Vigil.Cli -- diagnose --json --symptom "..."` (and variants for dry-run/offline); `dotnet publish` for exe.
- **Other:** Warnings fixed in two files; .gitignore covers .env and build noise; no keys/secrets in repo.