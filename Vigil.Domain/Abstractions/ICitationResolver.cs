// == ICitationResolver (from Diagram 6 + §6) == //

using Vigil.Domain.Entities;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Domain.Abstractions;

public interface ICitationResolver
{
    bool Resolve(Citation citation, EvidenceBundle bundle);
}
