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

## Setup

### Prerequisites
- .NET 8 SDK

### Clone & Build

```bash
git clone https://github.com/<your-org>/vigil.git
cd vigil
dotnet build
```

### Set your xAI API Key

Vigil reads the key **exclusively** from the `XAI_API_KEY` environment variable.

**PowerShell (user level, persists across sessions):**

```powershell
[Environment]::SetEnvironmentVariable("XAI_API_KEY", "xai-your-key-here", "User")
```

Restart your terminal.

**Temporary (current session only):**

```powershell
$env:XAI_API_KEY = "xai-your-key-here"
```

If the variable is not set (or empty), Vigil automatically falls back to the local heuristic analyzer (no API calls, no cost).

**Production / CI**

Set the same `XAI_API_KEY` environment variable in your deployment environment (Azure Key Vault, GitHub Actions secrets, Docker `-e`, Kubernetes Secret, etc.). The code never changes — only the provider of the value does.

## Running

### Basic usage

```powershell
# Pipe live evidence
journalctl -u nginx --since "10 min ago" | dotnet run --project Vigil.Cli -- diagnose --symptom "intermittent 500s after deploy"

# Multiple named sources
dotnet run --project Vigil.Cli -- diagnose `
  --logs "app.log" `
  --changes "deploy.log" `
  --symptom "outage after rollout"
```

### Useful flags

- `--offline` — Force the heuristic (nothing leaves the machine).
- `--dry-run` — Assemble + redact evidence and show exactly what would be sent (no model call).
- `--json` — Machine-readable output (great for piping into tickets, notifiers, etc.).
- `--symptom` — Free-text description of the observed symptom.

### Example output (human)

A ranked tree of causes with confidence, severity, category, and citations back to specific artifact IDs.

### Example output (machine)

```json
{
  "id": "...",
  "subject": { "kind": "service", "identifier": "payment-api" },
  "summary": "...",
  "provenance": { "analyzedBy": "Model", "usage": { "inputTokens": 1240, "outputTokens": 312 } },
  "causes": [ ... ]
}
```

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