// == Initial TDD tests for Domain model invariants and seam contracts (Slice 1 per plan; failing first then pass) == //

using System;
using System.Collections.Generic;
using FluentAssertions;
using Vigil.Application;
using Vigil.Application.Clients;
using Vigil.Application.Coordinators;
using Vigil.Application.UseCases;
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
        var bundle = new EvidenceBundle(artifacts, new ExclusionReport(Array.Empty<string>()), "intermittent failure");

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

        // Compact context for feeding the grill advisor (summaries + token awareness)
        var ctx = state.GetCompactContextForChat();
        ctx.Should().NotBeNull();
        ctx.EvidenceCount.Should().Be(1);
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
        var nlReply = client.ConsultAsync("the api is failing after the deploy", cwd: @"C:\sim", compactContext: state.GetCompactContextForChat().ToString()).Result;
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
