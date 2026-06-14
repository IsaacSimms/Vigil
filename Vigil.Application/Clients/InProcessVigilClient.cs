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
    private readonly IGrillAdvisor _grillAdvisor;

    public InProcessVigilClient(DiagnoseUseCase diagnoseUseCase, IGrillAdvisor grillAdvisor, object? query = null)
    {
        _diagnoseUseCase = diagnoseUseCase;
        _grillAdvisor = grillAdvisor ?? throw new ArgumentNullException(nameof(grillAdvisor));
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

    public Task<string> ConsultAsync(string message, string? cwd = null, Guid? lastDiagnosisId = null, string? compactContext = null)
    {
        return _grillAdvisor.ConsultAsync(message, cwd, lastDiagnosisId, compactContext);
    }
}
