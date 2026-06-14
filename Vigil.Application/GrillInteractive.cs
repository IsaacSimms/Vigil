// == GrillInteractive pure helpers (agentic TUI foundation + running context/token list per approved plan + Hole 4) == //
// Non-UI, reusable, fully testable. Lives in Application so future GUIs or embedded sessions can drive the same Grill-me logic.
// Keeps the heart: no SDK, no presentation chrome, depends only on Domain types. TDD-led.

using System;
using System.Collections.Generic;
using System.Linq;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Application
{
    /// <summary>
    /// Small pure types supporting the primary agentic natural-language Grill-me TUI experience.
    /// Session state maintains the visible "running list" of evidence, turns, last diagnosis, and token tally for the chat.
    /// </summary>
    public static class GrillInteractive
    {
        // == Launch decision (bare `vigil` in a dir launches the interactive TUI; explicit subcommands stay compatibility path) == //
        public static bool ShouldRunInteractive(string[] args, bool isTty = true)
        {
            if (args == null || args.Length == 0)
                return isTty; // bare invocation in interactive terminal = primary TUI

            var first = args[0].ToLowerInvariant();
            // Known subcommands keep the old one-shot/scripted behavior.
            if (first == "diagnose" || first == "history")
                return false;

            // Unknown first token or other flags → fall back to interactive (TTY preference).
            return isTty;
        }

        // == Intent / action parser (NL is primary; slash commands preserve the useful old flags inside the session) == //
        public static IntentResult ParseIntent(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new IntentResult(false, null, Array.Empty<string>(), null, input);

            var trimmed = input.Trim();

            if (trimmed.StartsWith("/"))
            {
                // Very small parser sufficient for kept commands + /load.
                var parts = trimmed[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
                var args = parts.Skip(1).ToArray();
                return new IntentResult(true, cmd, args, null, null);
            }

            // Free natural language → treat the whole thing as potential symptom / chat turn.
            // The TUI / coordinator will decide chat vs. escalate to diagnose.
            var symptomHint = trimmed.Length > 120 ? trimmed.Substring(0, 117) + "..." : trimmed;
            return new IntentResult(false, null, Array.Empty<string>(), symptomHint, trimmed);
        }
    }

    public sealed record IntentResult(
        bool IsExplicitCommand,
        string? CommandName,
        IReadOnlyList<string> Arguments,
        string? SuggestedSymptom,
        string? FreeText);

    // == Supporting records for SessionState (visible in the chat) == //
    public sealed record Turn(string UserMessage, string Reply, DateTimeOffset Timestamp);
    public sealed record CompactChatContext(
        string LaunchDirectory,
        int EvidenceCount,
        string? LastDiagnosisSummary,
        string? LastTurnSummary,
        int ApproximateTokensInContext);

    // == SessionState: the heart of the persistent grill-me conversation (evidence accumulation + running token + context list) == //
    public sealed class GrillSessionState
    {
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

            // Very lightweight summary for the advisor prompt (real token estimation can evolve here or in a coordinator).
            int approx = Math.Min(8000, (_evidence.Count * 300) + (_turns.Count * 150) + (lastDiagSummary?.Length ?? 0));
            if (TotalTokensUsed > 0) approx = Math.Max(approx, TotalTokensUsed / 2);

            return new CompactChatContext(
                LaunchDirectory,
                _evidence.Count,
                lastDiagSummary,
                lastTurn?.Reply,
                approx);
        }

        // For TUI display of the "running list"
        public IReadOnlyList<RawSource> GetCurrentEvidenceSnapshot() => _evidence.ToList();
    }
}
