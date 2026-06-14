// == ISpecification<T> + Composite (UL) (from Diagram 7 + SystemsDesign §11; Expression for EF later, IsSatisfiedBy for in-mem) == //

using System;
using System.Linq.Expressions;

namespace Vigil.Domain.Abstractions;

/// <summary>
/// Specification pattern (Composite (UL)) for querying past Diagnoses.
/// IsSatisfiedBy is used for in-memory repositories today.
/// ToExpression returns Expression<Func> that can be translated by EF Core later
/// (the reason we avoid naive Expression.Invoke in the composites - see design note).
/// </summary>
public interface ISpecification<T>
{
    Expression<Func<T, bool>> ToExpression();
    bool IsSatisfiedBy(T candidate);
    ISpecification<T> And(ISpecification<T> other);
    ISpecification<T> Or(ISpecification<T> other);
    ISpecification<T> Not();
}

public abstract class Specification<T> : ISpecification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();

    public bool IsSatisfiedBy(T candidate)
    {
        // Compile only for in-memory use (the v1 path). EF path uses the Expression directly.
        var predicate = ToExpression().Compile();
        return predicate(candidate);
    }

    public ISpecification<T> And(ISpecification<T> other)
    {
        return new AndSpecification<T>(this, other);
    }

    public ISpecification<T> Or(ISpecification<T> other)
    {
        return new OrSpecification<T>(this, other);
    }

    public ISpecification<T> Not()
    {
        return new NotSpecification<T>(this);
    }
}

// Composite implementations. Note the parameter rebinding pattern (using Expression.Parameter + Invoke)
// is the naive form; a real visitor rebinder would be needed for full EF composability across
// multiple specifications. For now sufficient for in-memory + future swap behind the seam.
internal sealed class AndSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public AndSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var leftExpr = _left.ToExpression();
        var rightExpr = _right.ToExpression();

        var param = Expression.Parameter(typeof(T));
        var body = Expression.AndAlso(
            Expression.Invoke(leftExpr, param),
            Expression.Invoke(rightExpr, param));

        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}

internal sealed class OrSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var leftExpr = _left.ToExpression();
        var rightExpr = _right.ToExpression();

        var param = Expression.Parameter(typeof(T));
        var body = Expression.OrElse(
            Expression.Invoke(leftExpr, param),
            Expression.Invoke(rightExpr, param));

        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}

internal sealed class NotSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _spec;

    public NotSpecification(ISpecification<T> spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var expr = _spec.ToExpression();
        var param = Expression.Parameter(typeof(T));
        var body = Expression.Not(Expression.Invoke(expr, param));
        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}
