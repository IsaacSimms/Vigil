// == InMemoryDiagnosisRepository (v1 impl per §11; EF/SQLite later swap behind seam) == //

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;

namespace Vigil.Infrastructure.Repositories;

/// <summary>
/// Simple in-memory store for Diagnoses. Enables persistence and querying for institutional memory in v1.
/// Thread-unsafe for demo; real would use concurrent or DB.
/// </summary>
public class InMemoryDiagnosisRepository : IDiagnosisRepository
{
    private readonly List<Diagnosis> _store = new();

    public Task SaveAsync(Diagnosis diagnosis)
    {
        if (diagnosis == null) return Task.CompletedTask;
        _store.Add(diagnosis);
        return Task.CompletedTask;
    }

    public Task<Diagnosis[]> QueryAsync(ISpecification<Diagnosis> spec)
    {
        if (spec == null) return Task.FromResult(Array.Empty<Diagnosis>());
        var results = _store.Where(spec.IsSatisfiedBy).ToArray();
        return Task.FromResult(results);
    }
}
