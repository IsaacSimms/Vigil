// == IVigilClient Seam (UL) - Transport / presentation-to-core seam (from Diagram 6 + SystemsDesign §3) == //

using System.Collections.Generic;
using System.Threading.Tasks;
using Vigil.Domain.Entities;
using Vigil.Domain.Models;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// The load-bearing seam between presentation and core (Dependency Inversion proof in headline Diagram 6).
/// InProcessVigilClient is the default (wires DiagnoseUseCase + all collaborators).
/// HttpVigilClient (over optional Api) is designed-for but not built in v1.
/// Presentation must do nothing but gather + call + render.
/// </summary>
public interface IVigilClient
{
    Task<Diagnosis> DiagnoseAsync(DiagnoseRequest request);
    Task<Diagnosis[]> QueryHistoryAsync(ISpecification<Diagnosis> spec);
}
