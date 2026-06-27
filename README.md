# Vigil

**An incident-diagnosis engine for systems and platform work — now with an interactive "Grill-me" TUI as the primary experience.**

Vigil helps you figure out what broke by feeding it heterogeneous evidence (logs, change records, configs, code, etc.). It returns governed, cited, ranked diagnoses with a deterministic validation gate, honest provenance, redaction before any model egress, and a zero-cost heuristic fallback.

The **primary way to use Vigil is the interactive Grill-me TUI** (launch with bare `vigil` or `dotnet run --project Vigil.Cli` in a target directory). Talk to it in natural language. It maintains live session state (accumulated evidence, conversation turns, running token usage, compact context for the LLM). Use `/diagnose` (or just describe the problem) to trigger the full governed pipeline at any time. Slash commands keep power-user flags and workflows inside the conversation.

One-shot/scripted usage (`vigil diagnose ...`) is fully preserved for pipes, CI, automation, and power users.

## Download (Windows — no .NET install)

A self-contained build is published on [GitHub Releases](https://github.com/IsaacSimms/Vigil/releases/latest).

| Asset | What you get |
|-------|----------------|
| [Vigil-win-x64.zip](https://github.com/IsaacSimms/Vigil/releases/latest/download/Vigil-win-x64.zip) | `vigil.exe`, sample logs, `QUICKSTART.txt`, `run-demo.ps1` |

Unzip anywhere, open PowerShell in that folder, and run `.\vigil`. No SDK, runtime, or git clone required. Windows SmartScreen may warn on unsigned executables — choose **More info → Run anyway**.

Current release: [v1.0.0](https://github.com/IsaacSimms/Vigil/releases/tag/v1.0.0).

## Quick Start (Interactive Grill-me Session — Primary UX)

### From the GitHub Release (recommended)

```powershell
# 1. Download and unzip Vigil-win-x64.zip (link above)
cd C:\path\to\Vigil-win-x64

# 2. Optional — enable Grok for natural-language turns (offline/heuristic works without a key)
$env:XAI_API_KEY = "xai-your-key-here"

# 3. cd into the directory with your incident context (or stay in the unzip folder for the bundled demo)
cd C:\path\to\my\incident

# 4. Launch the TUI (bare command = interactive session)
.\vigil

# Inside the session:
the payment service started 500ing right after deploy-456 to the api
/status
/load samples\app.log
/load samples\changes.txt
/diagnose --symptom "intermittent 500s after the change"
/status
exit
```

Offline one-shot demo (no API key):

```powershell
cd C:\path\to\Vigil-win-x64
.\run-demo.ps1
```

### From source (developers)

```powershell
# 1. Set your xAI key (optional but enables real Grok for NL turns; without it you get heuristic + full /diagnose path)
$env:XAI_API_KEY = "xai-your-key-here"

# 2. cd into the directory with your incident context
cd C:\path\to\my\incident

# 3. Launch the TUI (bare command = interactive session)
dotnet run --project Vigil.Cli

# Inside the session:
the payment service started 500ing right after deploy-456 to the api
/status
/load Docs\TestFiles\SimpleLogIncident\app.log
/load Docs\TestFiles\SimpleLogIncident\changes.txt
/diagnose --symptom "intermittent 500s after the change"
/status
exit
```

The session shows a banner with the launch directory, tracks evidence count + turns + **running token usage + compact context** (passed to every NL turn), and lets you interleave free conversation with formal diagnoses.

## Using the Grill-me TUI

### Launch
- Release: `cd /your/dir; .\vigil` (from the unzipped [GitHub Release](https://github.com/IsaacSimms/Vigil/releases/latest))
- Source: `cd /your/dir; dotnet run --project Vigil.Cli`
- No args or bare invocation in an interactive terminal enters the TUI (the guard in Program.cs).
- Subcommands (`vigil diagnose ...`) bypass the TUI for scripted use.

### Natural Language (Primary Input)
Just type. Your words become the query + context for the advisor (Grok when key present, or informative heuristic stub). The runner passes the current `GrillSessionState` (cwd, loaded evidence summaries, prior turns, token tally, last formal diagnosis) on every turn via `compactContext`.

Example flow:
```
the api is throwing timeouts after the config change last night
I suspect the DB pool
/status   # see evidence, tokens, last diagnosis
```

The LLM (via `IGrillAdvisor` / `GrokGrillAdvisor`) acts as a debugging partner: references context, suggests next evidence. Natural language can now also trigger the formal governed `/diagnose` pipeline directly (e.g. "analyze each of these files in this folder and tell me the issue plus a potential fix. Use /diagnose") — files mentioned or "in this folder" are auto-loaded into session evidence when intent is clear; the TUI dispatches to the full cited+validated Diagnosis path instead of (or in addition to) chat. Explicit `/load` + `/diagnose` remain available for precision.

### Session Commands (Kept Flags + Power User Workflows)
- `/load <relative-or-absolute-path>` — Read a file from disk (relative to launch dir), turn it into a `RawSource`, add to session evidence. Use real logs/changes from your incident dir.
- `/diagnose [--symptom "text"] [--offline] [--dry-run]` — Force a full governed diagnosis using *current accumulated session evidence* + optional symptom. Runs the complete pipeline (interpreters, assembler/rank/cap, redaction, analyzer (Grok or heuristic), `DiagnosisValidator` gate with citation resolution + ≤5 truncate, provenance, persist to repo). Full tree + citations + provenance rendered inside the session.
- `/status` — Evidence count, turns, total tokens used so far (running list from `SessionState`).
- `/cwd` — Show launch directory.
- `help` / `?` — List commands.
- `exit` / `quit` / `q` — Leave (diagnoses produced during the session are already persisted via the core use case).

All the old flags (`--offline`, `--dry-run`, `--json` where applicable) are honored inside `/diagnose`.

### Tokens & Context (The "Running List")
`SessionState` tracks:
- Loaded evidence (`AddEvidence`, `GetCurrentEvidenceSnapshot`, `CurrentEvidenceCount`).
- Conversation turns.
- `TotalTokensUsed` (updated on formal diagnose paths via `Provenance.Usage`; chat turns contribute via the advisor in future extensions).
- `GetCompactContextForChat()` — lightweight summary fed to the LLM on every NL turn so it can "remember" what you've loaded and discussed.

This is visible via `/status` and passed automatically.

### Examples with Committed Test Data
From repo root (or any dir):
```
dotnet run --project Vigil.Cli
/load Docs\TestFiles\SimpleLogIncident\app.log
/load Docs\TestFiles\SimpleLogIncident\changes.txt
/diagnose --symptom "payment failures after deploy"
```

Outputs a real Diagnosis (heuristic here; Grok when key set) with citations that resolve against the loaded artifacts.

## One-Shot / Scripting Usage (Preserved for Compat + Power Users)

All the original pipe-first behavior works unchanged. Examples below use `dotnet run` from a clone; with the release zip, substitute `.\vigil` for `dotnet run --project Vigil.Cli --`.

```powershell
# Pipe evidence + flags
type Docs\TestFiles\SimpleLogIncident\app.log | dotnet run --project Vigil.Cli -- diagnose --symptom "payment failures after deploy"
# Release equivalent:
# type samples\app.log | .\vigil diagnose --symptom "payment failures after deploy"

# With changes
type Docs\TestFiles\SimpleLogIncident\app.log | dotnet run --project Vigil.Cli -- diagnose --changes "Docs\TestFiles\SimpleLogIncident\changes.txt" --symptom "intermittent errors after change"

# Dry-run (preview without model spend)
type Docs\TestFiles\ComplexWithConfigAndChanges\auth.log | dotnet run --project Vigil.Cli -- diagnose --dry-run --symptom "auth failures"

# Offline (force heuristic, nothing leaves the box)
type Docs\TestFiles\CsvAndSyslog\metrics.csv | dotnet run --project Vigil.Cli -- diagnose --offline --symptom "test"

# JSON for piping onward
type Docs\TestFiles\JsonLogsDeployment\deploy.json | dotnet run --project Vigil.Cli -- diagnose --json --symptom "deployment issues"
```

See `vigil diagnose --help` for options. `--json` and the core pipeline remain the way to integrate Vigil as infrastructure.

## Local Setup

### End users (GitHub Release)

1. Download [Vigil-win-x64.zip](https://github.com/IsaacSimms/Vigil/releases/latest/download/Vigil-win-x64.zip) and unzip.
2. Open PowerShell in the unzipped folder.
3. Optional: set `XAI_API_KEY` (env var or User scope). Without it, everything still works via the heuristic + full `/diagnose` path.
4. `.\vigil` for the TUI, or `.\vigil diagnose ...` for one-shot. See `QUICKSTART.txt` in the zip.

Add the folder to your `PATH` if you want bare `vigil` from any directory.

### Developers (build from source)

1. .NET 8 SDK.
2. `git clone https://github.com/IsaacSimms/Vigil.git ; cd Vigil`
3. `dotnet build`
4. Set `XAI_API_KEY` (env var or User scope). Without it everything still works via the heuristic + full diagnosis path.
5. `dotnet run --project Vigil.Cli` (bare) for TUI, or with `diagnose ...` for one-shot.

### Publishing a new GitHub Release (maintainers)

To rebuild the release zip locally (same artifact attached to [GitHub Releases](https://github.com/IsaacSimms/Vigil/releases)):

```powershell
# From the repo root — outputs dist\Vigil-win-x64.zip + dist\Vigil-win-x64\
.\scripts\publish-release.ps1
```

Manual publish (equivalent core step):

```powershell
dotnet publish Vigil.Cli\Vigil.Cli.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o .\publish
# Produces .\publish\vigil.exe (AssemblyName is already vigil)
```

Upload `dist\Vigil-win-x64.zip` to a new GitHub Release (tag e.g. `v1.0.0` to match `Vigil.Cli.csproj` `<Version>`). The `releases/latest/download/...` link in this README resolves to whatever release is marked latest.

Re-publish when TUI, NL intent, `GrillInteractive`, or `GrillSessionState` change so the standalone binary stays in sync with source.

## Development on the Platform

Vigil is built for ambitious extension while keeping the core trustworthy.

### Build & Test
- `dotnet build`
- `dotnet test` (includes the core pipeline + TUI helpers, NL intent parsing for auto-load + /diagnose, `IGrillAdvisor` seam, and multi-turn simulations crossing state + Consult + Diagnose).

### TDD Expectations
Non-trivial behavior (new commands in the runner, advisor enhancements, context logic, seam interactions) is test-first with xUnit + FluentAssertions. The Interface (UL) is the test surface.

Existing keystone-style tests (recorded paths, heuristic doubles, validator gate) remain live.

### Architecture (Heart Preserved, UX Evolved)
- Strict onion (project refs enforced): Domain (entities + seams) → Application (orchestration + thin coordinators) → Infrastructure (adapters, Grok + heuristic, interpreters, repos) .
- Presentation (`Vigil.Cli`) references Application for contracts and Infra **only** at composition root for DI.
- Primary Seams (UL): `IDiagnosisAnalyzer`, `IArtifactInterpreter` + selector, `IVigilClient`, and the new `IGrillAdvisor` (for conversational NL).
- All business logic for diagnosis stays in C# behind seams. The TUI runner in Cli is "thicker" (session state, intent parsing, context assembly, action dispatch) because the interactive Grill-me experience is the stated primary goal — this was an explicit user-approved loosening while keeping the heart (onion, testable seams, no SDK leakage past Infra, determinism around the model, title comments on important blocks, etc.).
- `SessionState` + `GrillInteractive` (pure helpers in Application) are the reusable non-UI core of the TUI.
- `GrokGrillAdvisor` is the Adapter for free-form chat (plain completions, context injected, good grill-me system prompt). The structured diagnosis path is untouched.

See `docs/Vigil-SystemsDesign.md` and the Mermaid diagrams for the locked core design. The interactive TUI layers on top without rewriting the trust contract.

### Extending the Platform
- **Inside the TUI**: Edit the runner in `Vigil.Cli/Program.cs`. Add commands, improve NL heuristics, surface more from `state` (e.g. last full Diagnosis tree), wire `state.RecordTokens` for chat turns (extend `IGrillAdvisor` return type later if needed).
- **New NL capabilities**: Implement/enhance `IGrillAdvisor` (or swap the Grok one). The runner already passes rich context on every turn.
- **New evidence formats**: Add `IArtifactInterpreter` impls in Infrastructure + register in Program.cs composition root. The selector picks at runtime.
- **Real advisor for NL**: The current `GrokGrillAdvisor` is the starting point. Add usage capture, streaming, optional tools that let the model suggest loads or trigger `/diagnose`, etc.
- **Persistence / history in TUI**: Wire `DiagnosisQuery` + `ISpecification` into the runner (e.g. `/history similar`).
- **Tests**: Add facts that cross the seams (like the new session simulation). For TUI-specific logic, drive the pure helpers + client directly (no console required).
- Composition root (`Program.cs`) is the single place for DI choices (key presence, model vs heuristic, advisor, etc.).

Follow title comments (`// == Title Here == //`), only `/// <summary>` at type/file level, xUnit+Fluent, etc.

### Key Environment
`XAI_API_KEY` only (never committed). Temp in shell or permanent User registry. Same mechanism works for production secrets.

## Features (Current)

- **Primary**: Interactive Grill-me TUI with natural language, live `GrillSessionState` (evidence, turns, running tokens + context passed to LLM), `/load` from launch dir, easy interleaving with full governed diagnoses.
- Full diagnose pipeline (text evidence, auto-interpret, rank+cap, redaction, Grok or heuristic, deterministic citation validation + ≤5 cap, provenance, in-memory persist + query).
- Seams for evolution (analyzer, interpreters, client transport, now grill advisor).
- `--offline` / heuristic always available.
- `--dry-run`, `--json`, etc. in both TUI and one-shot.
- Zero-cost, honest Liskov-substitute heuristic baseline.
- All third-party (OpenAI SDK) confined to Infrastructure.

## License

MIT — see [LICENSE](LICENSE).

---

Built following the design in `docs/`. The core (governed diagnosis, seams, validation gate) remains the trustworthy heart. The interactive TUI is the new primary workflow for figuring things out in natural language while still giving you the rigorous cited output on demand.

For the latest design notes see the plan artifacts and `docs/Vigil-SystemsDesign.md`.