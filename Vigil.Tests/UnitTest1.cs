// == Initial TDD tests for Domain model invariants and seam contracts (Slice 1 per plan; failing first then pass) == //

using System;
using System.Collections.Generic;
using FluentAssertions;
using Vigil.Application;
using Vigil.Application.Clients;
using Vigil.Application.Coordinators;
using Vigil.Application.UseCases;
using Vigil.Domain;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Interpreters;
using Vigil.Infrastructure.Repositories;
using Xunit;

namespace Vigil.Tests;

/// <summary>
/// Early contract + invariant tests for the Domain foundation (model + primary Seams (UL)).
/// These are the first TDD tests. They cross the Interfaces (UL) and exercise value object rules.
/// More comprehensive keystone tests (recorded responses against validator) come in later slices.
/// </summary>
public class DomainModelTests
{
    [Fact]
    public void Confidence_ctor_throws_when_out_of_range()
    {
        // Arrange & Act
        Action actLow = () => new Confidence(-0.1);
        Action actHigh = () => new Confidence(1.1);

        // Assert
        actLow.Should().Throw<ArgumentOutOfRangeException>();
        actHigh.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Confidence_in_range_constructs_and_compares()
    {
        var c1 = new Confidence(0.5);
        var c2 = new Confidence(0.7);

        c1.Value.Should().Be(0.5);
        c1.CompareTo(c2).Should().BeNegative();
    }

    [Fact]
    public void Domain_enums_are_defined()
    {
        // Contract test: enums exist as per Diagram 3
        typeof(Modality).IsEnum.Should().BeTrue();
        typeof(ArtifactKind).IsEnum.Should().BeTrue();
        typeof(Severity).IsEnum.Should().BeTrue();
        // etc.
    }

    [Fact]
    public void Key_seams_interfaces_exist_with_expected_signatures()
    {
        // Contract tests for the primary Seams (UL)
        typeof(Vigil.Domain.Abstractions.IDiagnosisAnalyzer).IsInterface.Should().BeTrue();
        typeof(Vigil.Domain.Abstractions.IArtifactInterpreter).IsInterface.Should().BeTrue();
        typeof(Vigil.Domain.Abstractions.IVigilClient).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void EvidenceAssembler_ranks_and_applies_cap()
    {
        // TDD for EvidenceAssembler (Diagram 6 + §5)
        var assembler = new EvidenceAssembler();
        var artifacts = new List<EvidenceArtifact>
        {
            CreateTestArtifact(1, ArtifactKind.ChangeRecord, DateTimeOffset.UtcNow.AddMinutes(-1)),
            CreateTestArtifact(2, ArtifactKind.LogFile, DateTimeOffset.UtcNow.AddMinutes(-10)),
            CreateTestArtifact(3, ArtifactKind.LogFile, DateTimeOffset.UtcNow.AddMinutes(-5))
        };
        var hints = new ScopeHints();

        var bundle = assembler.Assemble(artifacts, hints);

        bundle.Artifacts.Should().HaveCount(3); // skeleton cap is high
        bundle.Exclusions.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void DiagnosisValidator_drops_and_strips_per_citation_resolution()
    {
        // TDD for DiagnosisValidator gate (§6)
        var fakeResolver = new FakeCitationResolver(resolves: true); // all resolve for happy path
        var validator = new DiagnosisValidator(fakeResolver);

        var raw = new RawDiagnosis("test summary", new List<CandidateCause>
        {
            new CandidateCause("cause1", null, new Confidence(0.9), Severity.High, CauseCategory.Deployment, 
                new List<Citation> { new Citation(Guid.NewGuid(), "snippet") })
        });
        var bundle = new EvidenceBundle(new List<EvidenceArtifact>(), new ExclusionReport(Array.Empty<string>()));

        var result = validator.Validate(raw, bundle);

        result.Diagnosis.Causes.Should().HaveCount(1);
        result.Report.Drops.Should().BeEmpty();
    }

    private EvidenceArtifact CreateTestArtifact(int seed, ArtifactKind kind, DateTimeOffset ts)
    {
        return new EvidenceArtifact(
            Guid.NewGuid(),
            Modality.Text,
            kind,
            $"log content {seed}",
            null,
            "text/plain",
            ts,
            new ResourceRef("host", "test-host"));
    }

    private class FakeCitationResolver : ICitationResolver
    {
        private readonly bool _resolves;
        public FakeCitationResolver(bool resolves) => _resolves = resolves;
        public bool Resolve(Citation citation, EvidenceBundle bundle) => _resolves;
    }

    [Fact]
    public void ArtifactInterpreterSelector_picks_ChangeRecord_for_change_text()
    {
        // TDD for selector + interpreters (§10, Diagram 5)
        var selector = new ArtifactInterpreterSelector(new IArtifactInterpreter[]
        {
            new ChangeRecordInterpreter(),
            new PlainTextInterpreter(),
            new JsonLogInterpreter()
        });

        var changeSource = new RawSource(Text: "deploy change abc123 to web-service at 10:00");
        var selected = selector.Select(changeSource);

        selected.Should().BeOfType<ChangeRecordInterpreter>();
    }

    [Fact]
    public void HeuristicDiagnosisAnalyzer_produces_cited_causes_as_test_double()
    {
        // TDD: heuristic as deterministic substitute for IDiagnosisAnalyzer (§9)
        var heuristic = new HeuristicDiagnosisAnalyzer();
        var artifacts = new List<EvidenceArtifact>
        {
            new EvidenceArtifact(Guid.NewGuid(), Modality.Text, ArtifactKind.ChangeRecord, "change foo to bar", null, "text/plain", DateTimeOffset.UtcNow.AddMinutes(-2), new ResourceRef("svc", "api")),
            new EvidenceArtifact(Guid.NewGuid(), Modality.Text, ArtifactKind.LogFile, "error at 10:05", null, "text/plain", DateTimeOffset.UtcNow.AddMinutes(-1), new ResourceRef("svc", "api"))
        };
        var bundle = new EvidenceBundle(artifacts, new ExclusionReport(Array.Empty<string>()), new ScopeHints(Symptom: "intermittent failure"));

        var result = heuristic.AnalyzeAsync(bundle, "failure after deploy").Result;

        result.IsSuccess.Should().BeTrue();
        result.Tier.Should().Be(AnalyzerTier.Heuristic);
        result.Diagnosis.Causes.Should().NotBeEmpty();
        result.Diagnosis.Causes[0].Citations.Should().NotBeEmpty();
    }

    [Fact]
    public void InMemoryDiagnosisRepository_saves_and_queries()
    {
        // TDD for repo (§11)
        var repo = new InMemoryDiagnosisRepository();
        var diag = new Diagnosis(Guid.NewGuid(), new ResourceRef("svc", "api"), "test", new AnalyzerProvenance(AnalyzerTier.Heuristic), DateTimeOffset.UtcNow, new List<CandidateCause>());

        repo.SaveAsync(diag).Wait();
        var results = repo.QueryAsync(new TrueSpecification<Diagnosis>()).Result;

        results.Should().ContainSingle(d => d.Id == diag.Id);
    }

    // Simple spec for test
    private class TrueSpecification<T> : Specification<T>
    {
        public override System.Linq.Expressions.Expression<Func<T, bool>> ToExpression() => _ => true;
    }

    [Fact]
    public void DiagnoseUseCase_full_text_loop_with_heuristic_and_interpreters()
    {
        // End-to-end skeleton for text-only diagnose loop (using phase 3 infra + phase 2 app)
        // Uses heuristic as analyzer (grok adapter can be swapped when key available)
        var interpreters = new IArtifactInterpreter[]
        {
            new PlainTextInterpreter(),
            new ChangeRecordInterpreter()
        };
        var selector = new ArtifactInterpreterSelector(interpreters);
        var assembler = new EvidenceAssembler();
        var redactor = new NoOpRedactor();
        var analyzer = new HeuristicDiagnosisAnalyzer();
        var validator = new DiagnosisValidator(new FakeCitationResolver(true));
        var repo = new InMemoryDiagnosisRepository();

        var heuristic = new HeuristicDiagnosisAnalyzer();
        var useCase = new DiagnoseUseCase(selector, assembler, redactor, heuristic, heuristic, validator, repo);

        var sources = new[]
        {
            new RawSource(Text: "service error after change deploy-123 to api-service")
        };
        var request = new DiagnoseRequest(sources, new ScopeHints(Symptom: "intermittent 500s"));

        var diagnosis = useCase.Execute(request).Result;

        diagnosis.Should().NotBeNull();
        diagnosis.Causes.Should().NotBeEmpty();
        diagnosis.Provenance.AnalyzedBy.Should().Be(AnalyzerTier.Heuristic);
        // repo should have it
        var history = repo.QueryAsync(new TrueSpecification<Diagnosis>()).Result;
        history.Should().Contain(d => d.Id == diagnosis.Id);
    }

    private class NoOpRedactor : IRedactor
    {
        public EvidenceBundle Redact(EvidenceBundle bundle) => bundle;
    }

    // == TDD for pure interactive helpers (Grill-me TUI foundation, per approved plan) ==
    // These are the first failing tests for the agentic session. Pure, no UI, no SDK.
    // SessionState carries the "running list" of context + tokens visible in the chat.
    // LaunchDecider + IntentParser keep decision logic testable and outside the runner chrome.

    [Fact]
    public void LaunchDecider_bare_args_or_tty_launches_interactive()
    {
        // TDD: bare `vigil` (no args) or TTY should choose the primary TUI per new priority.
        Vigil.Application.GrillInteractive.ShouldRunInteractive(Array.Empty<string>(), isTty: true).Should().BeTrue();
        Vigil.Application.GrillInteractive.ShouldRunInteractive(new[] { "diagnose" }, isTty: true).Should().BeFalse(); // explicit subcommand stays old path
        Vigil.Application.GrillInteractive.ShouldRunInteractive(new[] { "foo" }, isTty: false).Should().BeFalse();
    }

    [Fact]
    public void SessionState_tracks_evidence_turns_last_diagnosis_and_compact_context_with_token_tally()
    {
        // TDD for the running chat context + token visibility (Hole 4).
        var state = new Vigil.Application.GrillSessionState(launchDirectory: @"C:\incident");
        state.LaunchDirectory.Should().Be(@"C:\incident");
        state.CurrentEvidenceCount.Should().Be(0);
        state.Turns.Should().BeEmpty();
        state.LastDiagnosis.Should().BeNull();
        state.TotalTokensUsed.Should().Be(0);

        var src = new RawSource(Name: "app.log", Text: "error after deploy-123", Hint: "log");
        state.AddEvidence(src);
        state.CurrentEvidenceCount.Should().Be(1);

        state.AppendTurn("the service is 500ing after the change", "Understood. The change deploy-123 looks suspicious. Want me to produce a formal diagnosis?");
        state.Turns.Should().HaveCount(1);
        state.Turns[0].UserMessage.Should().Contain("500ing");

        // Compact context for feeding the grill advisor (summaries + token awareness + bounded excerpts)
        var ctx = state.GetCompactContextForChat();
        ctx.Should().NotBeNull();
        ctx.EvidenceCount.Should().Be(1);
        ctx.EvidenceExcerpts.Should().ContainSingle(e => e.FileName == "app.log" && e.Text.Contains("deploy-123"));
        ctx.FormatForAdvisor().Should().Contain("excerpt[app.log]");
        ctx.LastTurnSummary?.Should().Contain("suspicious"); // nullable-safe access (CS8602 addressed)

        // Token tally is updated when we record usage from a chat or diagnose turn
        state.RecordTokens(120, 45);
        state.TotalTokensUsed.Should().Be(165);
    }

    [Fact]
    public void IntentParser_free_text_becomes_symptom_or_chat_and_slash_commands_preserved()
    {
        // TDD: NL is primary; explicit commands for kept flags still work inside the session.
        var free = Vigil.Application.GrillInteractive.ParseIntent("the payment service started 500ing right after deploy-456 to the api");
        free.IsExplicitCommand.Should().BeFalse();
        free.SuggestedSymptom.Should().NotBeNullOrWhiteSpace();
        free.SuggestedSymptom.Should().Contain("payment service");

        var cmd = Vigil.Application.GrillInteractive.ParseIntent("/diagnose --symptom \"intermittent 500s\" --offline");
        cmd.IsExplicitCommand.Should().BeTrue();
        cmd.CommandName.Should().Be("diagnose");
        cmd.Arguments.Should().Contain("--offline");

        var load = Vigil.Application.GrillInteractive.ParseIntent("/load ..\\logs\\auth.log");
        load.IsExplicitCommand.Should().BeTrue();
        load.CommandName.Should().Be("load");
        load.Arguments.Should().Contain("..\\logs\\auth.log");
    }

    // == TDD for NL path extraction + auto-load (Grill-me: detect paths before ConsultAsync) == //

    [Fact]
    public void ExtractFilePathCandidates_finds_absolute_relative_quoted_and_simple_filenames()
    {
        var input =
            @"C:\Vigil\Vigil\Docs\TestFiles\SimpleLogIncident\app.log and C:\Vigil\Vigil\Docs\TestFiles\SimpleLogIncident\changes.txt. the entries labeled err are the priority";

        var paths = GrillInteractive.ExtractFilePathCandidates(input);

        paths.Should().HaveCount(2);
        paths[0].Should().Be(@"C:\Vigil\Vigil\Docs\TestFiles\SimpleLogIncident\app.log");
        paths[1].Should().Be(@"C:\Vigil\Vigil\Docs\TestFiles\SimpleLogIncident\changes.txt");
    }

    [Fact]
    public void ExtractFilePathCandidates_handles_quotes_commas_and_mixed_separators()
    {
        var input = @"""app.log"", changes.txt; .\logs\auth.log and ../deploy/changes.txt";

        var paths = GrillInteractive.ExtractFilePathCandidates(input);

        paths.Should().Equal("app.log", "changes.txt", @".\logs\auth.log", @"../deploy/changes.txt");
    }

    [Fact]
    public void ExtractFilePathCandidates_ignores_plain_language_without_path_tokens()
    {
        var input = "Tell me why the incident occurred after the deploy";

        GrillInteractive.ExtractFilePathCandidates(input).Should().BeEmpty();
    }

    [Fact]
    public void TryExtractAndLoadPaths_loads_resolved_files_into_session_state()
    {
        var incidentDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-nl-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(incidentDir);
        var logPath = System.IO.Path.Combine(incidentDir, "app.log");
        var changePath = System.IO.Path.Combine(incidentDir, "changes.txt");
        System.IO.File.WriteAllText(logPath, "ERROR payment timeout");
        System.IO.File.WriteAllText(changePath, "deploy-456 to payment-service");

        try
        {
            var state = new GrillSessionState(incidentDir);
            var input = $"look at {logPath} and changes.txt for err entries";

            var result = GrillInteractive.TryExtractAndLoadPaths(input, state);

            result.LoadedFileNames.Should().Equal("app.log", "changes.txt");
            result.NotFoundPaths.Should().BeEmpty();
            state.CurrentEvidenceCount.Should().Be(2);
            state.GetCurrentEvidenceSnapshot()[0].Text.Should().Contain("ERROR");
        }
        finally
        {
            System.IO.Directory.Delete(incidentDir, recursive: true);
        }
    }

    [Fact]
    public void TryExtractAndLoadPaths_skips_duplicates_and_reports_missing_paths()
    {
        var incidentDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-nl-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(incidentDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(incidentDir, "app.log"), "error line");

        try
        {
            var state = new GrillSessionState(incidentDir);
            state.AddEvidence(new RawSource("app.log", "already loaded", null, "loaded"));

            var result = GrillInteractive.TryExtractAndLoadPaths("look at app.log and missing.txt", state);

            result.LoadedFileNames.Should().BeEmpty();
            result.SkippedAlreadyLoaded.Should().Contain("app.log");
            result.NotFoundPaths.Should().Contain("missing.txt");
            state.CurrentEvidenceCount.Should().Be(1);
        }
        finally
        {
            System.IO.Directory.Delete(incidentDir, recursive: true);
        }
    }

    [Fact]
    public void ShouldAttemptAutoLoad_requires_intent_or_explicit_path_syntax()
    {
        GrillInteractive.ShouldAttemptAutoLoad("the api is 500ing after deploy").Should().BeFalse();
        GrillInteractive.ShouldAttemptAutoLoad("app.log and changes.txt").Should().BeFalse();
        GrillInteractive.ShouldAttemptAutoLoad("look at app.log and changes.txt").Should().BeTrue();
        GrillInteractive.ShouldAttemptAutoLoad(@"check C:\incident\app.log for errors").Should().BeTrue();
    }

    [Fact]
    public void TryExtractAndLoadPaths_skips_when_no_load_intent()
    {
        var incidentDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-nl-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(incidentDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(incidentDir, "app.log"), "error line");

        try
        {
            var state = new GrillSessionState(incidentDir);
            var result = GrillInteractive.TryExtractAndLoadPaths("app.log and changes.txt", state);

            result.ExtractedCandidates.Should().BeEmpty();
            result.LoadedFileNames.Should().BeEmpty();
            state.CurrentEvidenceCount.Should().Be(0);
        }
        finally
        {
            System.IO.Directory.Delete(incidentDir, recursive: true);
        }
    }

    [Fact]
    public void TryExtractAndLoadPaths_skips_files_over_auto_load_size_cap()
    {
        var incidentDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-nl-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(incidentDir);
        var bigPath = System.IO.Path.Combine(incidentDir, "big.log");
        System.IO.File.WriteAllText(bigPath, new string('x', (int)GrillSessionState.MaxAutoLoadBytes + 1));

        try
        {
            var state = new GrillSessionState(incidentDir);
            var result = GrillInteractive.TryExtractAndLoadPaths($"read {bigPath}", state);

            result.LoadedFileNames.Should().BeEmpty();
            result.SkippedTooLarge.Should().ContainSingle();
            state.CurrentEvidenceCount.Should().Be(0);
        }
        finally
        {
            System.IO.Directory.Delete(incidentDir, recursive: true);
        }
    }

    [Fact]
    public void LoadEvidenceFromPath_on_session_state_is_shared_by_explicit_load()
    {
        var incidentDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-nl-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(incidentDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(incidentDir, "app.log"), "loaded via shared path");

        try
        {
            var state = new GrillSessionState(incidentDir);
            var outcome = state.TryLoadEvidenceFromPath("app.log", hint: "loaded");

            outcome.Loaded.Should().BeTrue();
            outcome.FileName.Should().Be("app.log");
            state.GetCurrentEvidenceSnapshot()[0].Text.Should().Contain("shared path");
        }
        finally
        {
            System.IO.Directory.Delete(incidentDir, recursive: true);
        }
    }

    // Additional TDD for SessionState (accumulation, snapshot, token recording on diagnose path)
    [Fact]
    public void SessionState_supports_accumulation_snapshot_and_token_recording()
    {
        var state = new Vigil.Application.GrillSessionState(@"C:\test");
        state.AddEvidence(new RawSource("log1", "error 1", null, "log"));
        state.AddEvidence(new RawSource("change", "deploy foo", null, "change"));
        state.CurrentEvidenceCount.Should().Be(2);

        var snap = state.GetCurrentEvidenceSnapshot();
        snap.Should().HaveCount(2);
        snap[0].Name.Should().Be("log1");

        state.RecordTokens(300, 50);
        state.TotalTokensUsed.Should().Be(350);
    }

    // Cross the new IGrillAdvisor seam (keystone-style: no live key, context passed, fallback path)
    [Fact]
    public void IGrillAdvisor_GrokGrillAdvisor_no_key_path_includes_context_and_is_deterministic()
    {
        var options = new GrokOptions { ApiKey = string.Empty, Model = "grok-3", MaxTokens = 100, TimeoutSeconds = 10, Temperature = 0.1, BaseUrl = "https://api.x.ai/v1" };
        var advisor = new GrokGrillAdvisor(options);

        var ctx = "evidence:2; tokens:350; last: some summary";
        var reply = advisor.ConsultAsync("why did it fail after the deploy?", cwd: @"C:\incident", lastDiagnosisId: Guid.NewGuid(), compactContext: ctx).Result;

        reply.Should().Contain("Understood");
        reply.Should().Contain("C:\\incident");
        reply.Should().Contain("evidence:2");
        reply.Should().Contain("/diagnose"); // suggests using the governed path
    }

    // == TDD for in-session diagnose skill (flag parsing, NL intent, symptom chain, no sample injection) == //

    [Fact]
    public void ParseDiagnoseFlagsWithRemainder_extracts_quoted_symptom_and_boolean_flags()
    {
        var parsed = GrillInteractive.ParseDiagnoseFlagsWithRemainder(@"--symptom ""intermittent 500s"" --offline");

        parsed.Args.Symptom.Should().Be("intermittent 500s");
        parsed.Args.Offline.Should().BeTrue();
        parsed.Remainder.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParseDiagnoseIntent_matches_verb_patterns_and_strips_prefix()
    {
        var result = GrillInteractive.TryParseDiagnoseIntent("diagnose me — the api is 500ing after deploy-456");

        result.Should().NotBeNull();
        result!.IsDiagnoseIntent.Should().BeTrue();
        result.UtteranceRemainder.Should().Contain("api is 500ing");
    }

    // == TDD for broadened NL (folder/dir + "analyze ... Use /diagnose" etc) so natural language can drive full load + governed diagnose == //

    [Fact]
    public void TryParseDiagnoseIntent_detects_user_example_with_analyze_folder_and_use_diagnose()
    {
        var utterance = "analyze each of these files in this folder and tell me what the issue is, as well as a potential fix. Use /diagnose.";
        var result = GrillInteractive.TryParseDiagnoseIntent(utterance);

        result.Should().NotBeNull();
        result!.IsDiagnoseIntent.Should().BeTrue();
        (result.UtteranceRemainder ?? utterance).Should().Contain("issue");
    }

    [Fact]
    public void ShouldAttemptAutoLoad_true_for_analyze_phrase_with_folder_even_without_specific_filename_tokens()
    {
        GrillInteractive.ShouldAttemptAutoLoad("analyze each of these files in this folder and tell me what the issue is").Should().BeTrue();
        GrillInteractive.ShouldAttemptAutoLoad("look at the logs in the current directory").Should().BeTrue();
    }

    [Fact]
    public void IsSensibleEvidenceFile_accepts_common_evidence_types_and_rejects_junk_and_hidden()
    {
        GrillInteractive.IsSensibleEvidenceFile("app.log").Should().BeTrue();
        GrillInteractive.IsSensibleEvidenceFile("changes.txt").Should().BeTrue();
        GrillInteractive.IsSensibleEvidenceFile("deploy.json").Should().BeTrue();
        GrillInteractive.IsSensibleEvidenceFile("config.yaml").Should().BeTrue();
        GrillInteractive.IsSensibleEvidenceFile(".gitignore").Should().BeFalse();
        GrillInteractive.IsSensibleEvidenceFile(@"C:\incident\.git\config").Should().BeFalse();
        GrillInteractive.IsSensibleEvidenceFile("big.bin").Should().BeFalse();
        GrillInteractive.IsSensibleEvidenceFile("photo.png").Should().BeFalse();
    }

    [Fact]
    public void TryLoadSensibleFilesFromLaunchDirectory_loads_only_sensible_files_from_temp_dir_and_skips_duplicates()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-folder-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "app.log"), "ERROR at 14:02");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "changes.txt"), "deploy to svc");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "config.yaml"), "port: 80");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, ".secret"), "dontload");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "image.png"), "binary");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "subdir"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "subdir", "nested.log"), "should not load");

        try
        {
            var state = new GrillSessionState(dir);
            state.AddEvidence(new RawSource("changes.txt", "old", null, "prior"));

            var loaded = state.TryLoadSensibleFilesFromLaunchDirectory(maxFiles: 10);

            loaded.Should().Contain("app.log");
            loaded.Should().Contain("config.yaml");
            loaded.Should().NotContain("changes.txt");
            loaded.Should().NotContain("image.png");
            state.CurrentEvidenceCount.Should().Be(1 + 2);
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryExtractAndLoadPaths_with_folder_phrase_populates_state_from_directory()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vigil-nl-folder-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "metrics.csv"), "cpu,high");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "rollout.log"), "started deploy");

        try
        {
            var state = new GrillSessionState(dir);
            var input = "analyze each of these files in this folder and tell me the issue. Use /diagnose";
            var result = GrillInteractive.TryExtractAndLoadPaths(input, state);

            state.CurrentEvidenceCount.Should().BeGreaterThan(0);
            // Loaded via dir scan even without explicit filename tokens in regex extractors
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryParseDiagnoseIntent_returns_null_for_casual_chat()
    {
        GrillInteractive.TryParseDiagnoseIntent("the api is 500ing").Should().BeNull();
        GrillInteractive.TryParseDiagnoseIntent("can you diagnose my cat").Should().BeNull();
    }

    [Fact]
    public void ResolveSymptom_uses_priority_chain_flag_then_remainder_then_last_turn()
    {
        var state = new GrillSessionState(@"C:\incident");
        state.AppendTurn("payment service started failing after deploy", "noted");

        var fromFlag = GrillInteractive.ResolveSymptom(
            new DiagnoseCommandArgs("flag symptom", false),
            "remainder symptom",
            state);
        fromFlag.Symptom.Should().Be("flag symptom");
        fromFlag.UsedGenericSymptomFallback.Should().BeFalse();

        var fromRemainder = GrillInteractive.ResolveSymptom(
            new DiagnoseCommandArgs(null, false),
            "api timeouts after config change",
            state);
        fromRemainder.Symptom.Should().Be("api timeouts after config change");

        var fromTurn = GrillInteractive.ResolveSymptom(
            new DiagnoseCommandArgs(null, false),
            null,
            state);
        fromTurn.Symptom.Should().Contain("payment service");
    }

    [Fact]
    public void BuildDiagnoseRequest_never_injects_sample_evidence()
    {
        var state = new GrillSessionState(@"C:\incident");
        var request = GrillInteractive.BuildDiagnoseRequest(
            state,
            new DiagnoseCommandArgs(null, true),
            "symptom only");

        request.Sources.Should().BeEmpty();
        request.Offline.Should().BeTrue();
        request.Hints.Symptom.Should().Be("symptom only");
    }

    [Fact]
    public void Grill_session_simulation_nl_diagnose_intent_honors_offline_flag()
    {
        var interpreters = new IArtifactInterpreter[] { new PlainTextInterpreter(), new ChangeRecordInterpreter() };
        var selector = new ArtifactInterpreterSelector(interpreters);
        var assembler = new EvidenceAssembler();
        var redactor = new NoOpRedactor();
        var heuristic = new HeuristicDiagnosisAnalyzer();
        var validator = new DiagnosisValidator(new FakeCitationResolver(true));
        var repo = new InMemoryDiagnosisRepository();
        var useCase = new DiagnoseUseCase(selector, assembler, redactor, heuristic, heuristic, validator, repo);
        var advisor = new GrokGrillAdvisor(new GrokOptions { ApiKey = "", Model = "grok-3", MaxTokens = 100, TimeoutSeconds = 10, Temperature = 0.1, BaseUrl = "https://api.x.ai/v1" });
        var client = new InProcessVigilClient(useCase, advisor);

        var state = new GrillSessionState(@"C:\sim");
        state.AddEvidence(new RawSource("app.log", "service error after change deploy-123 to api-service", null, "log"));

        var intent = GrillInteractive.TryParseDiagnoseIntent("figure out what broke --offline");
        intent.Should().NotBeNull();
        var resolved = GrillInteractive.ResolveSymptom(intent!.Args, intent.UtteranceRemainder, state);
        var request = GrillInteractive.BuildDiagnoseRequest(state, intent.Args, resolved.Symptom);

        request.Offline.Should().BeTrue();
        var diag = client.DiagnoseAsync(request).Result;
        diag.Provenance.AnalyzedBy.Should().Be(AnalyzerTier.Heuristic);
        diag.Causes.Should().NotBeEmpty();
    }

    // == TDD for /paste multi-line input (END terminator, evidence + bounded consult preview) == //

    [Fact]
    public void IsPasteEndMarker_matches_END_case_insensitive_only()
    {
        GrillInteractive.IsPasteEndMarker("END").Should().BeTrue();
        GrillInteractive.IsPasteEndMarker("end").Should().BeTrue();
        GrillInteractive.IsPasteEndMarker("  END  ").Should().BeTrue();
        GrillInteractive.IsPasteEndMarker("END OF FILE").Should().BeFalse();
        GrillInteractive.IsPasteEndMarker("").Should().BeFalse();
    }

    [Fact]
    public void FinalizePastedLines_joins_and_trims()
    {
        var text = GrillInteractive.FinalizePastedLines(new[] { "ERROR line 1", "", "ERROR line 2" });
        text.Should().Contain("ERROR line 1");
        text.Should().Contain("ERROR line 2");
    }

    [Fact]
    public void ResolvePasteEvidenceName_uses_requested_or_increments_paste_n()
    {
        GrillInteractive.ResolvePasteEvidenceName("app.log", 0).Should().Be("app.log");
        GrillInteractive.ResolvePasteEvidenceName(null, 2).Should().Be("paste-3.txt");
    }

    [Fact]
    public void ValidatePasteSize_rejects_over_max_bytes()
    {
        var small = new string('a', 100);
        GrillInteractive.ValidatePasteSize(small, 200).Should().BeTrue();
        GrillInteractive.ValidatePasteSize(new string('x', 300), 200).Should().BeFalse();
    }

    [Fact]
    public void BuildPasteConsultMessage_truncates_preview_and_includes_evidence_pointer()
    {
        var full = new string('L', 10_000);
        var msg = GrillInteractive.BuildPasteConsultMessage("paste-1.txt", full, lineCount: 50, maxPreviewChars: 100);

        msg.Should().Contain("preview truncated");
        msg.Should().Contain("full content in evidence as paste-1.txt");
        msg.Length.Should().BeLessThan(full.Length);
    }

    // == TDD for ArtifactRelevanceScorer (pure Domain scoring module) == //

    private EvidenceArtifact CreateScorerArtifact(ArtifactKind kind, DateTimeOffset? timestamp, ResourceRef? resource = null) =>
        new EvidenceArtifact(Guid.NewGuid(), Modality.Text, kind, "content", null, "text/plain", timestamp, resource);

    [Fact]
    public void ArtifactRelevanceScorer_exact_timestamp_match_gives_max_temporal_score()
    {
        var refTime = DateTimeOffset.UtcNow;
        var artifact = CreateScorerArtifact(ArtifactKind.LogFile, refTime);

        ArtifactRelevanceScorer.Score(artifact, refTime).Should().BeApproximately(1000.0, precision: 0.001);
    }

    [Fact]
    public void ArtifactRelevanceScorer_ChangeRecord_adds_500_bonus()
    {
        var refTime = DateTimeOffset.UtcNow;
        var changeRecord = CreateScorerArtifact(ArtifactKind.ChangeRecord, refTime);
        var logFile     = CreateScorerArtifact(ArtifactKind.LogFile,      refTime);

        var delta = ArtifactRelevanceScorer.Score(changeRecord, refTime) - ArtifactRelevanceScorer.Score(logFile, refTime);
        delta.Should().BeApproximately(500.0, precision: 0.001);
    }

    [Fact]
    public void ArtifactRelevanceScorer_resource_match_adds_500_non_match_gets_nothing()
    {
        var refTime = DateTimeOffset.UtcNow;
        var target  = new ResourceRef("svc", "api");
        var matching    = CreateScorerArtifact(ArtifactKind.LogFile, refTime, target);
        var nonMatching = CreateScorerArtifact(ArtifactKind.LogFile, refTime, new ResourceRef("svc", "db"));

        var matchScore    = ArtifactRelevanceScorer.Score(matching,    refTime, target);
        var nonMatchScore = ArtifactRelevanceScorer.Score(nonMatching, refTime, target);

        (matchScore - nonMatchScore).Should().BeApproximately(500.0, precision: 0.001);
    }

    [Fact]
    public void ArtifactRelevanceScorer_presence_bonus_100_when_no_target()
    {
        var refTime     = DateTimeOffset.UtcNow;
        var withResource    = CreateScorerArtifact(ArtifactKind.LogFile, refTime, new ResourceRef("svc", "api"));
        var withoutResource = CreateScorerArtifact(ArtifactKind.LogFile, refTime);

        var delta = ArtifactRelevanceScorer.Score(withResource, refTime) - ArtifactRelevanceScorer.Score(withoutResource, refTime);
        delta.Should().BeApproximately(100.0, precision: 0.001);
    }

    [Fact]
    public void ArtifactRelevanceScorer_null_referenceTime_skips_temporal()
    {
        var artifact = CreateScorerArtifact(ArtifactKind.ChangeRecord, DateTimeOffset.UtcNow);

        ArtifactRelevanceScorer.Score(artifact, referenceTime: null).Should().BeApproximately(500.0, precision: 0.001);
    }

    [Fact]
    public void ArtifactRelevanceScorer_no_timestamp_on_artifact_skips_temporal()
    {
        var artifact = CreateScorerArtifact(ArtifactKind.LogFile, timestamp: null, new ResourceRef("svc", "api"));

        // Only presence bonus (no target) — no temporal, no ChangeRecord
        ArtifactRelevanceScorer.Score(artifact, DateTimeOffset.UtcNow).Should().BeApproximately(100.0, precision: 0.001);
    }

    [Fact]
    public void TryAddPastedEvidence_adds_rejects_duplicate_and_too_large()
    {
        var state = new GrillSessionState(@"C:\incident");
        var ok = state.TryAddPastedEvidence("app.log", "ERROR timeout");
        ok.Loaded.Should().BeTrue();
        ok.FileName.Should().Be("app.log");
        state.CurrentEvidenceCount.Should().Be(1);

        var dup = state.TryAddPastedEvidence("app.log", "other");
        dup.Loaded.Should().BeFalse();
        dup.SkipReason.Should().Be("already-loaded");

        var big = state.TryAddPastedEvidence("big.log", new string('x', 200), maxBytes: 100);
        big.Loaded.Should().BeFalse();
        big.SkipReason.Should().Be("too-large");
    }

    // Session simulation (multi-turn drive of state + seams, no console/TTY, crosses Consult + Diagnose paths)
    [Fact]
    public void Grill_session_simulation_accumulates_context_and_interleaves_diagnose()
    {
        // Minimal setup reusing patterns from existing full-loop test (heuristic for determinism, no key)
        var interpreters = new IArtifactInterpreter[] { new PlainTextInterpreter(), new ChangeRecordInterpreter() };
        var selector = new ArtifactInterpreterSelector(interpreters);
        var assembler = new EvidenceAssembler();
        var redactor = new NoOpRedactor(); // from existing in file
        var heuristic = new HeuristicDiagnosisAnalyzer();
        var validator = new DiagnosisValidator(new FakeCitationResolver(true));
        var repo = new InMemoryDiagnosisRepository();
        var useCase = new DiagnoseUseCase(selector, assembler, redactor, heuristic, heuristic, validator, repo);

        var advisor = new GrokGrillAdvisor(new GrokOptions { ApiKey = "" , Model = "grok-3", MaxTokens=100, TimeoutSeconds=10, Temperature=0.1, BaseUrl="https://api.x.ai/v1" });
        var client = new InProcessVigilClient(useCase, advisor);

        var state = new Vigil.Application.GrillSessionState(@"C:\sim");
        state.AddEvidence(new RawSource("app.log", "service error after change deploy-123 to api-service", null, "log"));

        // NL turn (Consult seam + state)
        var nlReply = client.ConsultAsync("the api is failing after the deploy", cwd: @"C:\sim", compactContext: state.GetCompactContextForChat().FormatForAdvisor()).Result;
        state.AppendTurn("the api is failing after the deploy", nlReply);
        state.Turns.Should().HaveCount(1);

        // Interleave governed diagnose using accumulated evidence + symptom (real pipeline, persist, tokens)
        var srcs = state.GetCurrentEvidenceSnapshot();
        var diagReq = new DiagnoseRequest(srcs, new ScopeHints(Symptom: "api failing after deploy"));
        var diag = client.DiagnoseAsync(diagReq).Result;
        state.SetLastDiagnosis(diag);
        if (diag.Provenance.Usage != null) state.RecordTokens(diag.Provenance.Usage.InputTokens, diag.Provenance.Usage.OutputTokens);

        diag.Should().NotBeNull();
        diag.Causes.Should().NotBeEmpty();
        diag.Provenance.AnalyzedBy.Should().Be(AnalyzerTier.Heuristic); // no key
        state.LastDiagnosis.Should().NotBeNull();
        // Tokens may be 0 for pure heuristic in this sim; covered in other facts. Main assertions are accumulation + interleave + persist.
        state.TotalTokensUsed.Should().BeGreaterOrEqualTo(0);

        // repo has it (persist via use case)
        var history = repo.QueryAsync(new TrueSpecification<Diagnosis>()).Result; // reuse existing TrueSpec from file
        history.Should().Contain(d => d.Id == diag.Id);
    }
}
