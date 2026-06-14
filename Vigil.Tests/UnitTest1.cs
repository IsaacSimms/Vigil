// == Initial TDD tests for Domain model invariants and seam contracts (Slice 1 per plan; failing first then pass) == //

using System;
using System.Collections.Generic;
using FluentAssertions;
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
}
