# Vigil

**An incident-diagnosis engine for systems and platform work.**

Vigil takes heterogeneous evidence about a *known* incident (logs, change records, code, configs, stack traces, screenshots) and returns a **governed, cited, ranked diagnosis** — what most likely broke, why, and the specific records that support each conclusion.

It is deliberately built as *infrastructure*, not a chat session. Every claim is cited, output is capped and validated, secrets are redacted before leaving the machine, and a local heuristic fallback ensures sensitive incidents can be analyzed with nothing leaving the box.

## Architecture Overview

Vigil follows strict **Clean / Onion architecture** with dependencies pointing inward only (enforced by project references).

- **Vigil.Domain** — Pure core. Entities (`EvidenceArtifact`, `Diagnosis`, `CandidateCause`), value objects (`Confidence`, `Citation`, `AnalyzerProvenance`), enumerations, and all abstractions. No external references.
- **Vigil.Application** — Orchestration only. `DiagnoseUseCase`, `EvidenceAssembler` (rank + cap + exclusions), `DiagnosisValidator` (deterministic citation gate), `DiagnosisQuery`.
- **Vigil.Infrastructure** — Concrete details and adapters.
  - Artifact interpreters (Strategy pattern) + `ArtifactInterpreterSelector` (Simple Factory).
  - Chain of Responsibility + Template Method for per-artifact processing.
  - `GrokDiagnosisAnalyzer` (Adapter) — uses the official OpenAI NuGet SDK pointed at xAI (`https://api.x.ai/v1`).
  - `HeuristicDiagnosisAnalyzer` — proximity-based, zero-cost, Liskov-substitute fallback.
  - In-memory `IDiagnosisRepository` (EF Core + SQLite is a later swap behind the same seam).
  - `TextRedactor`.
- **Vigil.Cli** — Presentation only (Spectre.Console.Cli). Thin commands. All business logic lives behind the `IVigilClient` seam.

### Key Seams (Strategy + Adapter)

- `IDiagnosisAnalyzer` — Model vs. heuristic (the primary Seam).
- `IArtifactInterpreter` — Heterogeneous input formats.
- `IVigilClient` — Transport (in-process by default; HTTP over optional `Vigil.Api` designed but not built in v1).

All third-party types (OpenAI SDK, etc.) are confined inside Infrastructure. The Domain owns the interfaces.

The model is the only stochastic element. Everything around it — evidence assembly, citation validation, output caps, provenance — is deterministic and testable.

## Features (v1)

- Pipe-first CLI (stdin + repeatable `--logs`, `--changes`, `--image` flags).
- Text evidence supported (JSON, CSV, syslog, plain text, change records).
- Automatic format detection via `IArtifactInterpreter` strategies.
- Ranked, cited diagnoses (≤5 causes, UUID-backed citations).
- Deterministic validation gate (citation resolution, grounding checks, truncation).
- `--dry-run` (preview bundle + redactions/exclusions without a model call).
- `--offline` (force heuristic, nothing leaves the machine).
- `--json` output for piping into other tools.
- Honest provenance (`AnalyzedBy: Model | Heuristic`, `FallbackReason`, token usage).
- Redaction before egress (text secrets masked; images not auto-redacted in v1).
- Institutional memory via in-memory repository + `ISpecification<Diagnosis>` queries (EF later).
- Zero-cost heuristic baseline that is a true substitute for the model tier.

## Local Setup (Windows + PowerShell)

These steps get you building and running Vigil on your own machine.

### 1. Prerequisites
- Install the **.NET 8 SDK** (https://dotnet.microsoft.com/download/dotnet/8.0)
- A terminal that can run PowerShell (Windows Terminal, VS Code terminal, or classic PowerShell)

### 2. Get the code
If you don't have it yet:
```powershell
git clone https://github.com/<your-org>/vigil.git
cd vigil
```

(If the code is already on your machine, just `cd` into the folder that contains `Vigil.slnx` and the `Vigil.Cli` folder.)

### 3. Build the project (first time)
```powershell
dotnet build
```
This restores packages and compiles everything. You should see "Build succeeded" at the end.

### 4. Set your xAI API key (required for real Grok calls)

Vigil looks **only** for the environment variable `XAI_API_KEY`.

**Option A – Temporary (works right now in this terminal window):**
```powershell
$env:XAI_API_KEY = "xai-your-actual-key-here"
```

**Option B – Permanent for this user (recommended for development):**
```powershell
[Environment]::SetEnvironmentVariable("XAI_API_KEY", "xai-your-actual-key-here", "User")
```
**Important:** Close this PowerShell window completely and open a **brand new** one. The change is stored in the Windows Registry under your user profile and only new processes see it.

Verify the key is visible in a new window:
```powershell
$env:XAI_API_KEY
```

If nothing is set, Vigil will automatically use the built-in heuristic analyzer instead (no internet, no cost, still produces output).

### 5. Run it (quick local test with sample data)

From the root folder, run a minimal test using the sample files that ship with the repo:

```powershell
# Using one of the built-in example folders we created for testing
type Docs\TestFiles\SimpleLogIncident\app.log | dotnet run --project Vigil.Cli -- diagnose --symptom "payment failures after deploy"
```

Or with the change record too:
```powershell
type Docs\TestFiles\SimpleLogIncident\app.log | dotnet run --project Vigil.Cli -- diagnose --changes "Docs\TestFiles\SimpleLogIncident\changes.txt" --symptom "intermittent errors after change"
```

You should see a diagnosis tree in the console. If your `XAI_API_KEY` is set, it will say `Provenance: Model`. If not, it will say `Provenance: Heuristic`.

### 6. Common commands

```powershell
# See all options
dotnet run --project Vigil.Cli -- diagnose --help

# Dry-run (see exactly what evidence would be sent, no AI call)
type Docs\TestFiles\ComplexWithConfigAndChanges\auth.log | dotnet run --project Vigil.Cli -- diagnose --dry-run --symptom "auth failures"

# JSON output (easy to pipe elsewhere)
type Docs\TestFiles\JsonLogsDeployment\deploy.json | dotnet run --project Vigil.Cli -- diagnose --json --symptom "deployment issues"

# Force offline mode (never calls xAI, even if key is set)
type Docs\TestFiles\CsvAndSyslog\metrics.csv | dotnet run --project Vigil.Cli -- diagnose --offline --symptom "test"
```

### 7. Build a standalone executable (optional)

If you want a .exe you can copy around without needing `dotnet run`:

```powershell
dotnet publish Vigil.Cli\Vigil.Cli.csproj -c Release -o .\publish
```

Then run it (still need the env var set in the same shell):
```powershell
type Docs\TestFiles\SimpleLogIncident\app.log | .\publish\Vigil.Cli.exe diagnose --symptom "test"
```


### Troubleshooting

- "No suitable interpreter" or weird output → make sure you're piping a file that matches one of the supported formats (plain text, JSON, CSV, syslog, or change records).
- Still getting heuristic when you set the key → you must open a **new** PowerShell window after using the `SetEnvironmentVariable` command, or use the temporary `$env:XAI_API_KEY=...` in the current window.
- Build errors → run `dotnet restore` then `dotnet build` again.

You now have a working local build and can feed it the sample files in `Docs\TestFiles\` or any real logs/changes you have on disk.

## Development

- Strict Clean/Onion architecture.
- TDD with xUnit + FluentAssertions.
- Title comments on every class and important block: `// == Title Here == //`.
- See `AGENTS.md`, `CONTEXT.md`, and the design documents in `Docs/` for full conventions and architecture rationale.
- The design is intentionally locked; changes to seams or core contracts require re-approval.

## License

MIT — see [LICENSE](LICENSE).

---

Built following the detailed design in `Docs/Vigil-SystemsDesign.md` and the Mermaid diagrams. The architecture was deliberately shaped for depth at the seams so the AI provider, input formats, and transport can evolve independently while the governance and verification contract remains stable.