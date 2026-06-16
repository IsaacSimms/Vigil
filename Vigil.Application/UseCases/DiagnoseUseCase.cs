// == DiagnoseUseCase (from Diagram 6 headline + SystemsDesign §5) == //

using System.Collections.Generic;
using System.Threading.Tasks;
using Vigil.Application.Coordinators;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;

namespace Vigil.Application.UseCases;

/// <summary>
/// Orchestrates the full diagnose pipeline (gather -> interpret -> assemble -> redact -> analyze -> validate -> persist).
/// Depends only on Domain abstractions (Dependency Inversion). All collaborators injected at composition root.
/// The stochastic step (IDiagnosisAnalyzer) is isolated; deterministic gate (DiagnosisValidator) follows.
/// Supports --offline (force heuristic, zero cost) per §3/§5.
/// </summary>
public class DiagnoseUseCase
{
    private readonly IArtifactInterpreterSelector _selector;
    private readonly EvidenceAssembler _assembler;
    private readonly IRedactor _redactor;
    private readonly IDiagnosisAnalyzer _modelAnalyzer;
    private readonly IDiagnosisAnalyzer _heuristicAnalyzer;
    private readonly DiagnosisValidator _validator;
    private readonly IDiagnosisRepository _repository;

    public DiagnoseUseCase(
        IArtifactInterpreterSelector selector,
        EvidenceAssembler assembler,
        IRedactor redactor,
        IDiagnosisAnalyzer modelAnalyzer,
        IDiagnosisAnalyzer heuristicAnalyzer,
        DiagnosisValidator validator,
        IDiagnosisRepository repository)
    {
        _selector = selector;
        _assembler = assembler;
        _redactor = redactor;
        _modelAnalyzer = modelAnalyzer;
        _heuristicAnalyzer = heuristicAnalyzer;
        _validator = validator;
        _repository = repository;
    }

    public async Task<Diagnosis> Execute(DiagnoseRequest request)
    {
        // 1. Interpret (via selector)
        var artifacts = new List<EvidenceArtifact>();
        foreach (var source in request.Sources)
        {
            var interpreter = _selector.Select(source);
            artifacts.AddRange(interpreter.Interpret(source));
        }

        // 2. Assemble + rank + cap
        var bundle = _assembler.Assemble(artifacts, request.Hints);

        // 3. Redact before egress
        var redacted = _redactor.Redact(bundle);

        // 4. Analyze (seam) - choose based on offline flag
        var effectiveAnalyzer = request.Offline ? _heuristicAnalyzer : _modelAnalyzer;
        var result = await effectiveAnalyzer.AnalyzeAsync(redacted, request.Hints?.Symptom);

        RawDiagnosis raw;
        if (result.IsSuccess && result.Diagnosis != null)
        {
            raw = result.Diagnosis;
        }
        else
        {
            // Fallback decision lives in use case (per §9). Call heuristic explicitly.
            var fallbackResult = await _heuristicAnalyzer.AnalyzeAsync(redacted, request.Hints?.Symptom);
            raw = fallbackResult.IsSuccess && fallbackResult.Diagnosis != null 
                ? fallbackResult.Diagnosis 
                : new RawDiagnosis("Heuristic fallback", new List<CandidateCause>());
        }

        // 5. Validate (deterministic gate §6)
        var tier = result.IsSuccess ? result.Tier : AnalyzerTier.Heuristic;
        var validated = _validator.Validate(raw, redacted, tier);

        // 6. Persist
        await _repository.SaveAsync(validated.Diagnosis);

        return validated.Diagnosis;
    }
}
