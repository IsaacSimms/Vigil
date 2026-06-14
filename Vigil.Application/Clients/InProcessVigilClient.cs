// == InProcessVigilClient (default transport impl per §3 and Diagram 6) == //

using System.Threading.Tasks;
using Vigil.Application.UseCases;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Application.Clients;

/// <summary>
/// In-process implementation of the presentation-to-core seam. Wires the UseCase directly.
/// Default for CLI/GUI. Http version over Api is optional/deferred.
/// </summary>
public class InProcessVigilClient : IVigilClient
{
    private readonly DiagnoseUseCase _diagnoseUseCase;
    // Query stub for v1 skeleton (full in later phase)

    public InProcessVigilClient(DiagnoseUseCase diagnoseUseCase, object? query = null)
    {
        _diagnoseUseCase = diagnoseUseCase;
    }

    public async Task<Diagnosis> DiagnoseAsync(DiagnoseRequest request)
    {
        return await _diagnoseUseCase.Execute(request);
    }

    public Task<Diagnosis[]> QueryHistoryAsync(ISpecification<Diagnosis> spec)
    {
        // Stub for v1
        return Task.FromResult(Array.Empty<Diagnosis>());
    }
}
