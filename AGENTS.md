# Vigil — Agent & Contributor Instructions

**Purpose:** This file + `CONTEXT.md` + the design documents in `docs/` define the non-negotiable guardrails for the greenfield implementation and all future work. The design in `docs/Vigil-SystemsDesign.md` (and the nine Mermaid diagrams) is locked. Deviations require explicit re-approval.

All agents and humans working in this repository must internalize and follow these rules. When in doubt, re-read the primary design docs before writing or editing code.

## Strict Rules (Non-Negotiable)

- **Strict onion rules.**  
  Dependencies point strictly inward only and are enforced by project references (not convention).  
  - `Vigil.Domain` references **nothing** external (no frameworks, no NuGet, no outer layers).  
  - `Vigil.Application` depends only on `Vigil.Domain`.  
  - `Vigil.Infrastructure` depends on `Vigil.Application` + `Vigil.Domain`.  
  - Presentation projects (`Vigil.Cli`, `Vigil.Api`) depend on `Vigil.Application` for contracts. They may reference `Vigil.Infrastructure` **exclusively** at the composition root for dependency-injection wiring. No other references.  
  No business logic may live in Infrastructure or Presentation layers.

- **All business logic in C#; PowerShell only for external data collection.**  
  The entire diagnose pipeline, interpreters, assemblers, validation, analyzers, repositories, use cases, and any orchestration are C#.  
  PowerShell (or other shells) may be used only to *gather* raw evidence that is then piped or passed into the Vigil tool (e.g. `journalctl ... | vigil diagnose ...`). No business rules, no interpretation logic, no diagnosis construction in scripts.

- **Use xUnit + FluentAssertions; TDD where non-trivial.**  
  Tests are first-class. For any behavior that is not trivial (new use-case paths, new interpreter logic, validation rules, seam interactions, fallback paths, etc.), follow Test-Driven Development: write a failing test first that expresses the desired behavior, then implement the minimal code to pass it, then refactor.  
  Use the xUnit test framework together with FluentAssertions for readable assertions.  
  Keystone tests (recorded model responses against the validator + seam) are especially important and must remain live without requiring a real API key.

- **Interactive TUI as primary UX + Spectre for CLI compat.**  
  Bare `vigil` (or `dotnet run --project Vigil.Cli`) launches the agentic Grill-me TUI (natural language primary, SessionState for context/tokens/evidence, commands like /load /diagnose for interleaving).  
  Spectre.Console.Cli subcommands (`diagnose` etc.) are kept thin for scripts/pipes/CI/power users (parse + gather + call seam + render).  
  The TUI runner is intentionally "thicker" (state, intent, context assembly, dispatch) per user-approved priority for the interactive experience while the core diagnosis pipeline/seams remain behind `IVigilClient` + other Domain interfaces. Presentation still does zero core business logic for the governed path. All of that lives behind the seams in Application/Domain.  
  **Zero business logic in presentation for the diagnosis engine.** (TUI orchestration for UX is the exception that proves the rule for the stated goal.) The same principle applies to future surfaces.

## Ubiquitous Language (UL) — Use These Terms Exactly

When discussing design, writing comments, commit messages, or reviewing, employ the project's Ubiquitous Language without substitution:

- **Module (UL)**
- **Interface (UL)**
- **Implementation (UL)**
- **Seam (UL)** (from Michael Feathers) — the location of an Interface (UL)
- **Adapter (UL)** — a concrete thing that satisfies an Interface (UL) at a Seam (UL)
- **Strategy (UL)**, **Depth (UL)**, **Leverage (UL)**, **Locality (UL)**, **Simple Factory (UL)** (selection, explicitly not GoF Factory Method), etc.

See `docs/Vigil-SystemsDesign.md` §12 (patterns table) and the teaching/design notes in the diagrams for precise usage and justification. The primary Seams (UL) are `IDiagnosisAnalyzer`, `IArtifactInterpreter`, `IVigilClient`, and `IGrillAdvisor`.

## Coding Conventions (in addition to the strict rules above)

- When beginning a class, important method, or significant block of code, give it a title comment using the convention:
  ```csharp
  // == Title Here == //
  ```
  Adapt syntax for the language (e.g. `# == Title Here == #` for scripts).  
  For multi-line notes, place the comment on the line immediately above.  
  Single-line observations about a specific line go on the same line (with space) and should align where practical.

- Only use `/// <summary>` XML documentation comments at class-level, interface-level, or file-level (describing the entire type). Do **not** use them on individual properties, fields, methods, or enum members. Use ordinary `//` comments for those.

- Prefer editing and refactoring existing code over creating brand-new files or types (the "deletion test" and "one adapter means hypothetical, two means real" principles apply). This guardrail may be loosened by explicit user direction for hygiene splits of overgrown modules (e.g., extracting GrillSessionState from GrillInteractive helpers) when it improves scan-ability, Locality (UL), and maintainability.

- When editing, use the most targeted/inline change possible.

- Follow the full architecture described in `CONTEXT.md` and the docs. Re-consult the exact section + diagram(s) for any slice you touch before coding.

## Testing & Quality

- TDD (as stated above) for non-trivial work.
- Every Seam (UL) must have tests that cross it (the Interface (UL) is the test surface).
- Model boundary tests must be runnable with recorded responses and zero live API calls.
- The heuristic tier serves as a deterministic, always-available test double for the analyzer seam.
- Validation gate behavior (citation resolution, drops, truncation to ≤5, provenance) must be asserted deterministically.

## PowerShell / Data Collection Boundary

- Any PowerShell (or shell) scripts in the repo or examples are **only** for producing raw evidence artifacts (logs, change records, screenshots via external commands).
- They must feed data into Vigil through the public CLI surface (pipes, flags, files). They must never duplicate logic that belongs in an `IArtifactInterpreter`, the assembler, or the analyzer.
- Example correct usage: `Get-EventLog ... | vigil diagnose --symptom "..." --json`

## Workflow Reminders for Agents

1. Before any code change: run through the relevant diagnostic information in `CONTEXT.md`, the matching section of `docs/Vigil-SystemsDesign.md`, and the corresponding diagram(s).
2. State the Seam (UL) or Module (UL) you are touching and why the change respects its Interface (UL).
3. For non-trivial behavior, lead with the test (TDD).
4. Keep all third-party concerns (Grok via OpenAI SDK, future EF, etc.) behind the appropriate Adapter (UL) or Repository. SDK types never leak past Infrastructure.
5. Presentation changes must be limited to input gathering + seam invocation + rendering.
6. After changes, verify that the onion dependency direction is still respected at the project-reference level.
7. Update this `AGENTS.md` or `CONTEXT.md` only when the underlying locked design actually changes (and only after re-approval).

## References

- Primary spec: `docs/Vigil-SystemsDesign.md`
- Diagrams & teaching notes: `docs/Vigil-Architecture-Diagrams-Mermaid.md`
- Historical decisions & iteration plan: `docs/2026-06-13-vigil-project-PreImplementation-recap.md`
- Quick orientation: `CONTEXT.md` (this directory)
- Global coding standards (title comments, summary doc policy, review expectations) apply in addition to the rules here.

The architecture was deliberately designed for **Depth (UL)** at the seams so that the Grok (xAI) integration, input heterogeneity, and transport concerns can evolve independently while the core diagnosis contract and verification gate remain stable and testable.

Follow the rules. The design is the source of truth.
