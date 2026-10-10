using FluentValidation;

namespace OpsFlow.Api.Features.Common;

/// <summary>Offset pagination parameters (ADR-008). Bound from the query string with [AsParameters].</summary>
public sealed record PageQuery(int? Page, int? PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Upper bound for <see cref="Page"/>, so the offset can never overflow.</summary>
    public const int MaxPage = 100_000;

    public int PageNumber => Page ?? 1;

    public int Size => PageSize ?? DefaultPageSize;

    public int Skip => (PageNumber - 1) * Size;
}

public sealed class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, PageQuery.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageQuery.MaxPageSize);
    }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public static class PagedResponse
{
    public static PagedResponse<T> Create<T>(IReadOnlyList<T> items, PageQuery query, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new(items, query.PageNumber, query.Size, totalCount, (int)Math.Ceiling(totalCount / (double)query.Size));
    }
}
