// == DiagnosisQuery (from Diagram 6 + SystemsDesign §11) == //

using System.Threading.Tasks;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;

namespace Vigil.Application.UseCases;

/// <summary>
/// Query past diagnoses using Specification + Composite (§11).
/// Enables institutional memory. In-memory impl in Infra for v1.
/// </summary>
public class DiagnosisQuery
{
    private readonly IDiagnosisRepository _repository;

    public DiagnosisQuery(IDiagnosisRepository repository)
    {
        _repository = repository;
    }

    public async Task<Diagnosis[]> Execute(ISpecification<Diagnosis> spec)
    {
        return await _repository.QueryAsync(spec);
    }
}
