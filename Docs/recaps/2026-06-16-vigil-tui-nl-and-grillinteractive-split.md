# Vigil TUI Natural Language and GrillInteractive Split

**Date:** 2026-06-16
**Type:** refactoring
**Environment / Systems:** Vigil .NET 8 project (C#, Spectre.Console TUI, Grok via OpenAI SDK)

## TL;DR
Enhanced the Grill-me TUI so natural language can auto-load files and folders mentioned in the current directory (e.g. "analyze each of these files in this folder") and trigger the governed /diagnose pipeline without explicit /load. Removed empty Class1.cs placeholders from all layers. Split the monolithic ~810-line GrillInteractive.cs (after grill-me review) into focused GrillInteractive.cs (static helpers + protocol records) and new GrillSessionState.cs (state + context records). Updated AGENTS.md to loosen the "no new files" guardrail per user direction. All verified with clean builds and full test suite (42/42 passing).

## Context & Goal
User reported that bare `vigil` launch + natural language could not load evidence or invoke the formal diagnose path (had to use /load + /diagnose explicitly). Later flagged GrillInteractive.cs as a large single file and the presence of empty Class1.cs scaffolding. Goals: make "anything" possible via natural language or explicit skill calls; audit and refactor for cleanliness while following AGENTS.md, TDD, title comments, and onion rules.

## Key Points Explored
- NL intent parsing, path extraction (regexes for quoted/absolute/relative filenames), auto-load gating, and diagnose intent detection (prefix patterns + new contains-based signals for "use /diagnose", "analyze ... issue ... fix").
- Folder support: new FolderOrDirectoryIntentRegex, IsSensibleEvidenceFile filter (excludes .git/bin/obj/junk, limited exts), and TryLoadSensibleFilesFromLaunchDirectory (shallow, deduped, size-capped loads into session state for both Consult and Diagnose paths).
- Code smells: GrillInteractive.cs mixed static helpers (ParseIntent, flag parsing, paste, NL load), supporting records, and the full GrillSessionState class (evidence list, turns, tokens, GetCompactContextForChat, load methods). Class1.cs were template leftovers (Domain had explicit placeholder note).
- Grill-me process on splitting: reviewed TUI handoff history (intentionally one file for "pure helpers"), AGENTS "prefer no new files" rule, coupling between statics and state, public surface used by Program.cs and tests, deletion test, title comments. User selected option B (minimal two-file split), authorized new files/folders, and requested loosening the guardrail in AGENTS.md.

## Decisions & Outcomes
- Added folder-aware NL auto-load and broadened TryParseDiagnoseIntent (TDD-led: new tests for user's exact phrasing + dir loading; all existing + new tests green).
- Removed all three Class1.cs via git rm (zero references anywhere; post-removal build + 42 tests clean).
- Loosened AGENTS.md guardrail with targeted inline edit to allow user-directed hygiene splits for locality/scan-ability.
- Split executed as option B: created `Vigil.Application/GrillSessionState.cs` (moved class + EvidenceExcerpt/Turn/CompactChatContext with proper title comments and original inner comments); used targeted search_replace on GrillInteractive.cs to remove moved sections (retained static helpers + protocol records); added header note. Same namespace; internal cross-calls (state → GrillInteractive.XXX) and all caller sites unchanged.
- Verification: `dotnet build` succeeded; focused Grill/NL/session tests + full suite passed.

## Open Questions / Next Steps
None in this thread (work complete). Future TUI evolution (e.g. more commands, Program.cs runner polish) can reference the new structure.

## Artifacts
- New: `C:\Vigil\Vigil\Vigil.Application\GrillSessionState.cs`
- Modified: `Vigil.Application\GrillInteractive.cs`, `AGENTS.md`
- Deleted (git): `Vigil.Domain\Class1.cs`, `Vigil.Application\Class1.cs`, `Vigil.Infrastructure\Class1.cs`
- Tests: multiple new facts in `Vigil.Tests\UnitTest1.cs`
- This recap: `C:\Vigil\Vigil\Docs\recaps\2026-06-16-vigil-tui-nl-and-grillinteractive-split.md`