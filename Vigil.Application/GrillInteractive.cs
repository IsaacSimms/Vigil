// == GrillInteractive pure helpers (agentic TUI foundation + running context/token list per approved plan + Hole 4) == //
// Non-UI, reusable, fully testable. Lives in Application so future GUIs or embedded sessions can drive the same Grill-me logic.
// Keeps the heart: no SDK, no presentation chrome, depends only on Domain types. TDD-led.
// (GrillSessionState + its context records extracted to its own file for improved Locality (UL) and scan-ability per user-directed option B split.)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

        // == In-session diagnose skill (flag parsing, NL intent, symptom chain, request build) == //

        public static DiagnoseFlagParseResult ParseDiagnoseFlagsWithRemainder(string? text)
        {
            string? symptom = null;
            var offline = false;
            var remainderParts = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return new DiagnoseFlagParseResult(new DiagnoseCommandArgs(symptom, offline), null);

            var i = 0;
            var s = text.Trim();
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                    i++;

                if (i >= s.Length)
                    break;

                if (i + 1 < s.Length && s[i] == '-' && s[i + 1] == '-')
                {
                    var flagStart = i + 2;
                    var flagEnd = flagStart;
                    while (flagEnd < s.Length && !char.IsWhiteSpace(s[flagEnd]) && s[flagEnd] != '=')
                        flagEnd++;

                    var flag = s[flagStart..flagEnd].ToLowerInvariant();
                    i = flagEnd;

                    if (flag == "offline")
                    {
                        offline = true;
                        continue;
                    }

                    if (flag == "symptom")
                    {
                        while (i < s.Length && char.IsWhiteSpace(s[i]))
                            i++;
                        if (i < s.Length && s[i] == '=')
                        {
                            i++;
                            while (i < s.Length && char.IsWhiteSpace(s[i]))
                                i++;
                        }

                        var (value, next) = ReadFlagValue(s, i);
                        symptom = value;
                        i = next;
                        continue;
                    }

                    continue;
                }

                var (token, nextIndex) = ReadFlagValue(s, i);
                if (!string.IsNullOrWhiteSpace(token))
                    remainderParts.Add(token);
                i = nextIndex;
            }

            var remainder = remainderParts.Count > 0 ? string.Join(" ", remainderParts).Trim() : null;
            if (string.IsNullOrWhiteSpace(remainder))
                remainder = null;

            return new DiagnoseFlagParseResult(new DiagnoseCommandArgs(symptom, offline), remainder);
        }

        public static string? ExtractSlashDiagnoseArgTail(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var trimmed = input.Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
                return null;

            var withoutSlash = trimmed[1..].TrimStart();
            if (!withoutSlash.StartsWith("diagnose", StringComparison.OrdinalIgnoreCase))
                return null;

            var afterCommand = withoutSlash[8..].TrimStart();
            return afterCommand;
        }

        public static DiagnoseIntentResult? TryParseDiagnoseIntent(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var trimmed = input.Trim();
            foreach (var pattern in DiagnoseIntentPrefixPatterns)
            {
                var match = pattern.Match(trimmed);
                if (!match.Success)
                    continue;

                var afterPrefix = trimmed[match.Length..].Trim();
                afterPrefix = TrimLeadingDiagnosePunctuation(afterPrefix);
                var parsed = ParseDiagnoseFlagsWithRemainder(afterPrefix);
                return new DiagnoseIntentResult(true, parsed.Remainder, parsed.Args);
            }

            // == Broad NL signals so user can say "analyze the files in this folder... Use /diagnose" (or similar) and get governed path without exact prefix == //
            if (ContainsFormalDiagnosisSignal(trimmed))
            {
                // Use the full utterance as remainder source; ParseDiagnoseFlags will pull --flags if present
                var parsed = ParseDiagnoseFlagsWithRemainder(trimmed);
                return new DiagnoseIntentResult(true, parsed.Remainder ?? trimmed, parsed.Args);
            }

            return null;
        }

        // == Pure, deterministic detector for NL that should escalate to the formal Diagnose pipeline (not just chat Consult) == //
        private static bool ContainsFormalDiagnosisSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var t = text.ToLowerInvariant();

            // Explicit skill invocation anywhere in the utterance
            if (t.Contains("/diagnose") || t.Contains("use diagnose") || t.Contains("run diagnose") || t.Contains("trigger diagnose"))
                return true;

            // Strong analysis request that names the outcome the user wants (issue + fix/root/broke) — "analyze X and tell the issue and potential fix"
            var hasAnalysisVerb = t.Contains("analyze") || t.Contains("analyz") || t.Contains("root cause") || t.Contains("find what broke") || t.Contains("figure out what");
            var hasDesiredOutcome = t.Contains("issue") || t.Contains("fix") || t.Contains("broke") || t.Contains("problem") || t.Contains("root cause") || t.Contains("what the issue");
            if (hasAnalysisVerb && hasDesiredOutcome)
                return true;

            return false;
        }

        public static ResolvedDiagnoseInput ResolveSymptom(
            DiagnoseCommandArgs args,
            string? utteranceRemainder,
            GrillSessionState state)
        {
            if (!string.IsNullOrWhiteSpace(args.Symptom))
                return new ResolvedDiagnoseInput(args.Symptom.Trim(), false);

            if (!string.IsNullOrWhiteSpace(utteranceRemainder))
                return new ResolvedDiagnoseInput(utteranceRemainder.Trim(), false);

            var lastTurn = state.Turns.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastTurn?.UserMessage))
                return new ResolvedDiagnoseInput(lastTurn.UserMessage.Trim(), false);

            return new ResolvedDiagnoseInput(GenericSymptomFallback, true);
        }

        public static DiagnoseRequest BuildDiagnoseRequest(
            GrillSessionState state,
            DiagnoseCommandArgs args,
            string symptom)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var sources = state.GetCurrentEvidenceSnapshot();
            return new DiagnoseRequest(sources, new ScopeHints(Symptom: symptom), args.Offline);
        }

        private static (string Value, int NextIndex) ReadFlagValue(string s, int start)
        {
            if (start >= s.Length)
                return (string.Empty, start);

            if (s[start] == '"' || s[start] == '\'')
            {
                var quote = s[start];
                var i = start + 1;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    if (s[i] == quote)
                        return (sb.ToString(), i + 1);

                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        sb.Append(s[i + 1]);
                        i += 2;
                        continue;
                    }

                    sb.Append(s[i]);
                    i++;
                }

                return (sb.ToString(), i);
            }

            var end = start;
            while (end < s.Length && !char.IsWhiteSpace(s[end]))
                end++;

            return (s[start..end], end);
        }

        private static string TrimLeadingDiagnosePunctuation(string value)
        {
            // Trim separators only — not ASCII '-' (would destroy --offline/--symptom flags).
            var trimmed = value.TrimStart('—', ':', ' ');
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                trimmed = trimmed[2..].TrimStart();
            return trimmed;
        }

        private const string GenericSymptomFallback = "unspecified incident symptom";

        private static readonly Regex[] DiagnoseIntentPrefixPatterns =
        {
            new(@"^/diagnose\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new(@"^diagnose\s+me\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new(@"^diagnose\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new(@"^figure\s+out\s+what\s+broke\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new(@"^root\s+cause\s+this\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new(@"^find\s+what\s+broke\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        };

        // == Multi-line /paste skill (END terminator, evidence + bounded consult preview) == //

        public const int MaxPasteConsultChars = 8_192;

        public static bool IsPasteEndMarker(string? line)
        {
            return string.Equals(line?.Trim(), "END", StringComparison.OrdinalIgnoreCase);
        }

        public static string FinalizePastedLines(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return string.Empty;

            return string.Join('\n', lines).Trim();
        }

        public static string ResolvePasteEvidenceName(string? requestedName, int evidenceCount)
        {
            if (!string.IsNullOrWhiteSpace(requestedName))
                return Path.GetFileName(requestedName.Trim().Trim('"', '\''));

            return $"paste-{evidenceCount + 1}.txt";
        }

        public static bool ValidatePasteSize(string text, long maxBytes)
        {
            if (text == null)
                return true;

            return Encoding.UTF8.GetByteCount(text) <= maxBytes;
        }

        public static string BuildPasteConsultMessage(string fileName, string fullText, int lineCount, int maxPreviewChars = MaxPasteConsultChars)
        {
            var bytes = Encoding.UTF8.GetByteCount(fullText);
            var truncated = fullText.Length > maxPreviewChars;
            var preview = truncated ? fullText[..maxPreviewChars] : fullText;
            var pointer = $"[full content in evidence as {fileName}, {bytes} bytes, {lineCount} lines]";

            if (truncated)
                return $"[pasted content — preview truncated]\n{preview}\n...\n{pointer}";

            return $"[pasted content]\n{preview}\n{pointer}";
        }

        // == NL path extraction (detect file references before ConsultAsync; pure + testable) == //
        public static IReadOnlyList<string> ExtractFilePathCandidates(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            var found = new List<(int Index, string Path)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(int index, string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var candidate = TrimTrailingPunctuation(raw.Trim().Trim('"', '\''));
                if (!LooksLikeFilePathCandidate(candidate))
                    return;

                if (seen.Add(candidate))
                    found.Add((index, candidate));
            }

            foreach (Match match in QuotedPathRegex.Matches(input))
                TryAdd(match.Index, match.Groups[1].Value);

            foreach (Match match in WindowsAbsolutePathRegex.Matches(input))
                TryAdd(match.Index, match.Value);

            foreach (Match match in RelativePathRegex.Matches(input))
                TryAdd(match.Index, match.Value);

            foreach (Match match in SimpleFileNameRegex.Matches(input))
                TryAdd(match.Index, match.Value);

            return found
                .OrderBy(t => t.Index)
                .Select(t => t.Path)
                .ToList();
        }

        // == NL auto-load intent gate (only load when user signals file intent or uses explicit paths) == //
        // Now also triggers for "analyze ... files in this folder" (dir intent + verb) even without foo.log tokens.
        public static bool ShouldAttemptAutoLoad(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var hasFileCandidates = ExtractFilePathCandidates(input).Any();
            var hasDirSignal = MentionsCurrentFolderOrDirectory(input);

            if (!hasFileCandidates && !hasDirSignal)
                return false;

            if (LoadIntentVerbRegex.IsMatch(input))
                return true;

            if (ExplicitPathSyntaxRegex.IsMatch(input))
                return true;

            // Dir signal + analysis language is sufficient (e.g. the verb regex already covers "analyze")
            if (hasDirSignal && LoadIntentVerbRegex.IsMatch(input))
                return true;

            return false;
        }

        // == NL auto-load (resolve against launch dir, add RawSource evidence, report outcomes) == //
        // Enhanced to also handle "in this folder / these files in the directory" by delegating to state dir loader.
        public static PathLoadResult TryExtractAndLoadPaths(string input, GrillSessionState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (!ShouldAttemptAutoLoad(input))
                return PathLoadResult.Empty;

            var candidates = ExtractFilePathCandidates(input);
            var loaded = new List<string>();
            var notFound = new List<string>();
            var skipped = new List<string>();
            var tooLarge = new List<string>();
            var seenResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                var resolved = ResolvePath(candidate, state.LaunchDirectory);
                if (!seenResolved.Add(resolved))
                    continue;

                var outcome = state.TryLoadEvidenceFromPath(candidate, hint: "nl-auto-load", maxBytes: GrillSessionState.MaxAutoLoadBytes);
                if (outcome.Loaded)
                {
                    loaded.Add(outcome.FileName!);
                    continue;
                }

                switch (outcome.SkipReason)
                {
                    case "already-loaded":
                        skipped.Add(candidate);
                        break;
                    case "not-found":
                        notFound.Add(candidate);
                        break;
                    case "too-large":
                        tooLarge.Add(candidate);
                        break;
                }
            }

            // == Directory expansion for folder phrases (e.g. user's "each of these files in this folder") == //
            // Conservative shallow scan of launch dir using sensible-evidence filter; names unioned into loaded for reporting.
            if (MentionsCurrentFolderOrDirectory(input))
            {
                var fromDir = state.TryLoadSensibleFilesFromLaunchDirectory(maxFiles: 25, perFileMaxBytes: GrillSessionState.MaxAutoLoadBytes);
                foreach (var name in fromDir)
                {
                    if (!loaded.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)))
                        loaded.Add(name);
                }
            }

            return new PathLoadResult(candidates, loaded, notFound, skipped, tooLarge);
        }

        internal static string ResolvePath(string candidate, string launchDirectory)
        {
            var path = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(launchDirectory, candidate);
            return Path.GetFullPath(path);
        }

        private static string TrimTrailingPunctuation(string value)
        {
            return value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'');
        }

        private static bool LooksLikeFilePathCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (candidate.Contains('\\', StringComparison.Ordinal) || candidate.Contains('/', StringComparison.Ordinal))
                return HasFileExtension(Path.GetFileName(candidate.TrimEnd('\\', '/')));

            return HasFileExtension(candidate);
        }

        private static bool HasFileExtension(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var dot = fileName.LastIndexOf('.');
            if (dot <= 0 || dot >= fileName.Length - 1)
                return false;

            var ext = fileName[(dot + 1)..];
            return ext.Length is >= 2 and <= 10 && ext.All(char.IsLetterOrDigit);
        }

        private static readonly Regex QuotedPathRegex = new(
            @"[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WindowsAbsolutePathRegex = new(
            @"[A-Za-z]:\\[^""\s]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RelativePathRegex = new(
            @"\.{1,2}[\\/](?:[\w.-]+[\\/])*[\w.-]+\.[\w]{2,10}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SimpleFileNameRegex = new(
            @"(?<![\\/:\w])(?<![A-Za-z]:)[\w][\w.-]*\.[\w]{2,10}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex LoadIntentVerbRegex = new(
            @"\b(?:look\s+at|read|load|check|use|analyz(?:e|ing)|inspect|open|review|examine)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ExplicitPathSyntaxRegex = new(
            @"(?:[A-Za-z]:\\|\.{1,2}[\\/])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // == Folder/directory intent for "analyze each of these files in this folder" NL case (no concrete foo.log tokens needed) == //
        private static readonly Regex FolderOrDirectoryIntentRegex = new(
            @"\b(?:this\s+(?:folder|directory|dir)|the\s+files?\s+(?:in\s+(?:this|the|current)\s+)?(?:folder|directory|dir|here)|these\s+files?|all\s+files?\s+(?:in|here|the\s+folder)|current\s+dir(?:ectory)?|folder\s+(?:contents?|files?))\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // == Public helper for tests and dir-load gating (conservative text-ish evidence files only; skips vcs/build/binary) == //
        public static bool IsSensibleEvidenceFile(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return false;

            var name = Path.GetFileName(pathOrName);
            if (name.StartsWith(".", StringComparison.Ordinal))
                return false;

            var lowerFull = pathOrName.ToLowerInvariant().Replace('/', '\\');
            if (lowerFull.Contains("\\.git\\") || lowerFull.Contains("\\bin\\") || lowerFull.Contains("\\obj\\") ||
                lowerFull.Contains("node_modules") || lowerFull.Contains("\\.vs\\") || lowerFull.Contains("\\debug\\") ||
                lowerFull.Contains("\\release\\"))
                return false;

            var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                return false;

            return ext is "log" or "txt" or "json" or "csv" or "yaml" or "yml" or "syslog" or "config" or "conf" or "ini" or "md" or "cs" or "ps1" or "sh" or "xml" or "properties" or "toml";
        }

        // == Internal for load decision (public for direct test of the signal) == //
        public static bool MentionsCurrentFolderOrDirectory(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;
            return FolderOrDirectoryIntentRegex.IsMatch(input);
        }
    }

    public sealed record PathLoadResult(
        IReadOnlyList<string> ExtractedCandidates,
        IReadOnlyList<string> LoadedFileNames,
        IReadOnlyList<string> NotFoundPaths,
        IReadOnlyList<string> SkippedAlreadyLoaded,
        IReadOnlyList<string> SkippedTooLarge)
    {
        public static PathLoadResult Empty { get; } = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public sealed record EvidenceLoadOutcome(bool Loaded, string? FileName, string? SkipReason);

    public sealed record IntentResult(
        bool IsExplicitCommand,
        string? CommandName,
        IReadOnlyList<string> Arguments,
        string? SuggestedSymptom,
        string? FreeText);

    public sealed record DiagnoseCommandArgs(string? Symptom, bool Offline);

    public sealed record DiagnoseFlagParseResult(DiagnoseCommandArgs Args, string? Remainder);

    public sealed record DiagnoseIntentResult(bool IsDiagnoseIntent, string? UtteranceRemainder, DiagnoseCommandArgs Args);

    public sealed record ResolvedDiagnoseInput(string Symptom, bool UsedGenericSymptomFallback);
}
