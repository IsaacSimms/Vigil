// == SimpleCitationResolver (ICitationResolver impl; checks EvidenceArtifactId exists in bundle per §6) == //

using System;
using System.Linq;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Application.Coordinators;

/// <summary>
/// Basic resolver for the validation gate. A citation resolves if its EvidenceArtifactId 
/// matches one of the artifacts in the current EvidenceBundle.
/// Snippet is for humans only; not used here.
/// </summary>
public class SimpleCitationResolver : ICitationResolver
{
    public bool Resolve(Citation citation, EvidenceBundle bundle)
    {
        if (citation == null || bundle == null) return false;
        return bundle.Artifacts.Any(a => a.Id == citation.EvidenceArtifactId);
    }
}
