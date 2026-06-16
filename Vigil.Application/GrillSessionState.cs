// == GrillSessionState (extracted from the original monolithic GrillInteractive.cs per user-directed option B split) == //
// The heart of the persistent "running list" / session memory for the agentic natural-language Grill-me TUI.
// Accumulates evidence (as RawSource), conversation turns, token usage, and last formal Diagnosis.
// Produces bounded CompactChatContext (with EvidenceExcerpts) passed to the IGrillAdvisor seam on every NL turn.
// Co-located supporting records: EvidenceExcerpt, Turn, CompactChatContext.
// Pure C# (no SDKs, no presentation), fully testable, depends only on Domain. Lives in Application so future
// GUIs or embedded sessions can drive the same interactive logic. TDD-led. Split authorized to improve Locality (UL)
// and make the ~810-line file more scannable while keeping the overall module cohesive at the namespace level.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Application
{
    public sealed record EvidenceExcerpt(string FileName, string Text, bool Truncated);

    public sealed record Turn(string UserMessage, string Reply, DateTimeOffset Timestamp);
    public sealed record CompactChatContext(
        string LaunchDirectory,
        int EvidenceCount,
        string? LastDiagnosisSummary,
        string? LastTurnSummary,
        int ApproximateTokensInContext,
        IReadOnlyList<EvidenceExcerpt> EvidenceExcerpts)
    {
        // == Format compact context for IGrillAdvisor (includes bounded evidence excerpts) == //
        public string FormatForAdvisor()
        {
            var sb = new StringBuilder();
            sb.Append($"cwd={LaunchDirectory}; evidence={EvidenceCount}; tokens~={ApproximateTokensInContext}");

            if (!string.IsNullOrWhiteSpace(LastDiagnosisSummary))
                sb.Append($"; lastDiagnosis={LastDiagnosisSummary}");

            if (!string.IsNullOrWhiteSpace(LastTurnSummary))
                sb.Append($"; lastTurn={LastTurnSummary}");

            foreach (var excerpt in EvidenceExcerpts)
            {
                var prefix = excerpt.Truncated ? "[truncated] " : "";
                sb.Append($"; excerpt[{excerpt.FileName}]={prefix}{excerpt.Text}");
            }

            return sb.ToString();
        }
    }

    // == SessionState: the heart of the persistent grill-me conversation (evidence accumulation + running token + context list) == //
    public sealed class GrillSessionState
    {
        public const long MaxAutoLoadBytes = 1_048_576;        // 1 MB cap for NL auto-load
        public const int MaxExcerptCharsPerFile = 500;
        public const int MaxTotalExcerptChars = 3_000;

        private readonly List<RawSource> _evidence = new();
        private readonly List<Turn> _turns = new();
        private int _inputTokens;
        private int _outputTokens;

        public string LaunchDirectory { get; }
        public int CurrentEvidenceCount => _evidence.Count;
        public IReadOnlyList<Turn> Turns => _turns;
        public Diagnosis? LastDiagnosis { get; private set; }
        public int TotalTokensUsed => _inputTokens + _outputTokens;

        public GrillSessionState(string launchDirectory)
        {
            LaunchDirectory = launchDirectory ?? Environment.CurrentDirectory;
        }

        public void AddEvidence(RawSource source)
        {
            if (source != null)
                _evidence.Add(source);
        }

        // == Shared evidence load (used by /load and NL auto-load) == //
        public EvidenceLoadOutcome TryLoadEvidenceFromPath(string candidate, string hint, long maxBytes = long.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return new EvidenceLoadOutcome(false, null, "not-found");

            var resolved = GrillInteractive.ResolvePath(candidate, LaunchDirectory);
            var fileName = Path.GetFileName(resolved);

            if (_evidence.Any(e => string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                return new EvidenceLoadOutcome(false, fileName, "already-loaded");

            if (!File.Exists(resolved))
                return new EvidenceLoadOutcome(false, candidate, "not-found");

            var size = new FileInfo(resolved).Length;
            if (size > maxBytes)
                return new EvidenceLoadOutcome(false, fileName, "too-large");

            var text = File.ReadAllText(resolved);
            AddEvidence(new RawSource(fileName, text, null, hint));
            return new EvidenceLoadOutcome(true, fileName, null);
        }

        // == Pasted clipboard/log content (used by /paste in TUI) == //
        public EvidenceLoadOutcome TryAddPastedEvidence(string? requestedName, string text, long maxBytes = MaxAutoLoadBytes)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new EvidenceLoadOutcome(false, null, "empty");

            var fileName = GrillInteractive.ResolvePasteEvidenceName(requestedName, _evidence.Count);

            if (_evidence.Any(e => string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                return new EvidenceLoadOutcome(false, fileName, "already-loaded");

            if (Encoding.UTF8.GetByteCount(text) > maxBytes)
                return new EvidenceLoadOutcome(false, fileName, "too-large");

            AddEvidence(new RawSource(fileName, text, null, "pasted"));
            return new EvidenceLoadOutcome(true, fileName, null);
        }

        // == NL folder / "this directory" support (shallow, filtered sensible evidence files only) == //
        // Used when user says "analyze each of these files in this folder" etc. in natural language.
        // Reuses the per-file loader for dedup + size + add logic. TopDirectoryOnly to stay predictable.
        public IReadOnlyList<string> TryLoadSensibleFilesFromLaunchDirectory(int maxFiles = 30, long perFileMaxBytes = MaxAutoLoadBytes)
        {
            var loadedNames = new List<string>();
            if (string.IsNullOrWhiteSpace(LaunchDirectory) || !Directory.Exists(LaunchDirectory))
                return loadedNames;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(LaunchDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(p => GrillInteractive.IsSensibleEvidenceFile(p))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Take(maxFiles);
            }
            catch
            {
                return loadedNames;
            }

            foreach (var fullPath in files)
            {
                var fi = new FileInfo(fullPath);
                if (fi.Length > perFileMaxBytes)
                    continue;

                var name = fi.Name;
                if (_evidence.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    var text = File.ReadAllText(fullPath);
                    AddEvidence(new RawSource(name, text, null, "nl-dir-load"));
                    loadedNames.Add(name);
                }
                catch
                {
                    // unreadable or locked — skip silently for NL UX
                }
            }

            return loadedNames;
        }

        public void AppendTurn(string userMessage, string reply)
        {
            _turns.Add(new Turn(userMessage ?? "", reply ?? "", DateTimeOffset.UtcNow));
        }

        public void SetLastDiagnosis(Diagnosis? diagnosis)
        {
            LastDiagnosis = diagnosis;
        }

        public void RecordTokens(int input, int output)
        {
            if (input > 0) _inputTokens += input;
            if (output > 0) _outputTokens += output;
        }

        public CompactChatContext GetCompactContextForChat()
        {
            var lastTurn = _turns.LastOrDefault();
            var lastDiagSummary = LastDiagnosis?.Summary;

            var excerpts = BuildEvidenceExcerpts();
            var excerptChars = excerpts.Sum(e => e.Text.Length);

            // Lightweight token estimate including bounded excerpts for the advisor prompt.
            int approx = Math.Min(8000, (_evidence.Count * 300) + (_turns.Count * 150) + (lastDiagSummary?.Length ?? 0) + excerptChars);
            if (TotalTokensUsed > 0) approx = Math.Max(approx, TotalTokensUsed / 2);

            return new CompactChatContext(
                LaunchDirectory,
                _evidence.Count,
                lastDiagSummary,
                lastTurn?.Reply,
                approx,
                excerpts);
        }

        private List<EvidenceExcerpt> BuildEvidenceExcerpts()
        {
            var excerpts = new List<EvidenceExcerpt>();
            var totalChars = 0;

            foreach (var source in _evidence)
            {
                if (totalChars >= MaxTotalExcerptChars)
                    break;

                var text = source.Text ?? "";
                var name = source.Name ?? "unknown";
                var remaining = MaxTotalExcerptChars - totalChars;
                var perFileLimit = Math.Min(MaxExcerptCharsPerFile, remaining);
                if (perFileLimit <= 0)
                    break;

                var truncated = text.Length > perFileLimit;
                var excerptText = truncated ? text[..perFileLimit] : text;
                excerpts.Add(new EvidenceExcerpt(name, excerptText, truncated));
                totalChars += excerptText.Length;
            }

            return excerpts;
        }

        // For TUI display of the "running list"
        public IReadOnlyList<RawSource> GetCurrentEvidenceSnapshot() => _evidence.ToList();
    }
}
