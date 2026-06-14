// == IDiagnosisRepository (from Diagram 6 + SystemsDesign §11) == //

using System.Threading.Tasks;
using Vigil.Domain.Entities;
using Vigil.Domain.Abstractions; // for ISpecification

namespace Vigil.Domain.Abstractions;

/// <summary>
/// Repository (UL) over storage. v1 = in-memory. Later EF + SQLite is a drop-in swap.
/// Enables institutional memory (query past diagnoses by category/resource/severity via Specification + Composite).
/// </summary>
public interface IDiagnosisRepository
{
    Task SaveAsync(Diagnosis diagnosis);
    Task<Diagnosis[]> QueryAsync(ISpecification<Diagnosis> spec);
}
