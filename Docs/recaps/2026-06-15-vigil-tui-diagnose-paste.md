# Vigil TUI Diagnose Skill and Multi-line Paste

**Date:** 2026-06-15
**Type:** implementation
**Environment / Systems:** C# / .NET 8, Windows, Spectre.Console TUI, xUnit + FluentAssertions

## TL;DR

This thread assessed Vigil's TUI readiness, then implemented two high-priority UX fixes: a testable in-session **diagnose skill** (NL intent + correct `/diagnose` flag parsing, no fake sample evidence) and a **`/paste` command** for multi-line clipboard input (END terminator, evidence + bounded consult preview). Tests grew from 25 to 37; build and full suite pass.

## Context & Goal

The user wanted to understand where the Vigil project stood relative to a **viable Grill-me TUI**, then prioritize and implement the most important gaps. Initial analysis identified broken in-session `/diagnose` behavior (ignored flags, sample-data injection) and single-line input limiting paste. Work proceeded through plan-mode grill-me sessions, then agent-mode TDD implementation for both slices.

## Key Points Explored

- **Architecture scan:** Five-project onion intact; core diagnose pipeline functional; TUI MVP exists (`RunGrillMeSession`, `GrillSessionState`, `IGrillAdvisor`, NL auto-load in uncommitted work at thread start).
- **TUI viability gaps ranked:** (1) trustworthy in-session `/diagnose`, (2) multi-line paste, (3) live Grok smoke test, (4) chat token accounting, (5) `/history`.
- **Diagnose skill grill-me decisions:**
  - NL intent detection + explicit `/diagnose` route to one governed handler.
  - Symptom priority: `--symptom` → utterance remainder → last non-command turn → generic fallback + warning.
  - Empty evidence: run with symptom only; warn honestly; never inject sample `RawSource`.
  - Auto-load (`TryExtractAndLoadPaths`) before every diagnose (slash + NL).
  - Scope: TUI only; pure module in `GrillInteractive.cs`.
- **Paste plan grill-me decisions:**
  - `/paste [name]` command only; main `>` prompt stays single-line `AnsiConsole.Ask`.
  - Destination: add `RawSource` evidence **and** send bounded preview to `ConsultAsync`.
  - Terminator: `END` only (rejected double-blank — false positives inside logs).
  - Consult payload: ~8 KB preview + pointer to evidence name (rejected full-paste-to-advisor token bomb).
- **Plan review caught:** double-blank terminator and unbounded consult payload were fixed before paste implementation.

## Decisions & Outcomes

### Diagnose skill (shipped)

- Added to [`Vigil.Application/GrillInteractive.cs`](Vigil.Application/GrillInteractive.cs):
  - `ParseDiagnoseFlagsWithRemainder` (quoted `--symptom`, `--offline`, `--dry-run`)
  - `TryParseDiagnoseIntent` (moderate verb patterns: `diagnose me`, `figure out what broke`, etc.)
  - `ResolveSymptom`, `BuildDiagnoseRequest` (session evidence only)
- Wired [`Vigil.Cli/Program.cs`](Vigil.Cli/Program.cs): `HandleDiagnoseInSession` shared by `/diagnose` and NL diagnose-intent; empty-evidence warning; `dryRun` render; removed sample injection.
- Fixed `TrimLeadingDiagnosePunctuation` stripping `--` from flags when NL remainder followed em-dash.
- **6 new tests** for diagnose skill; session simulation for NL intent + `--offline`.

### Multi-line paste (shipped)

- Added to `GrillInteractive.cs`:
  - `IsPasteEndMarker`, `FinalizePastedLines`, `ResolvePasteEvidenceName`, `ValidatePasteSize`, `BuildPasteConsultMessage` (`MaxPasteConsultChars = 8192`)
  - `GrillSessionState.TryAddPastedEvidence` (1 MB cap, duplicate/too-large semantics like `/load`)
- Wired `Program.cs`: `/paste` command, `ReadMultilinePasteFromConsole`, `HandlePasteInSession`; updated banner and `/help`.
- **6 new tests** for paste helpers.

### Verification

- `dotnet test` — **37/37 passed**
- `dotnet build Vigil.slnx` — clean

## Open Questions / Next Steps

- Bracketed-paste on main `>` prompt (Grok Build-style paste without typing `/paste`).
- Live `XAI_API_KEY` smoke test for advisor + model diagnose paths.
- Chat token recording from `ConsultAsync` (advisor returns usage).
- `/history` wired to `QueryHistoryAsync` + repo.
- One-shot `DiagnoseCommand` `--changes` / `--logs` parity (deferred from diagnose slice).
- Multi-line input ergonomics beyond `/paste` if users want zero-command paste.

## Artifacts

- **Source (modified):**
  - [`Vigil.Application/GrillInteractive.cs`](Vigil.Application/GrillInteractive.cs) — diagnose skill + paste module + `TryAddPastedEvidence`
  - [`Vigil.Cli/Program.cs`](Vigil.Cli/Program.cs) — `HandleDiagnoseInSession`, `HandlePasteInSession`, `ReadMultilinePasteFromConsole`
  - [`Vigil.Tests/UnitTest1.cs`](Vigil.Tests/UnitTest1.cs) — 12 new facts (6 diagnose + 6 paste)
- **Plans (session):** diagnose skill plan + paste plan with grill-me amendments in `~/.grok/sessions/.../plan.md`
- **Prior handoffs (context):** [`Docs/handoffs/2026-06-14-vigil-tui-handoff.md`](Docs/handoffs/2026-06-14-vigil-tui-handoff.md)
- **Manual smoke commands:**
  ```
  dotnet run --project Vigil.Cli
  /load app.log
  /diagnose --symptom "payment failures" --offline
  diagnose me — intermittent 500s after deploy
  /paste app.log
  <paste log lines>
  END
  /status
  ```