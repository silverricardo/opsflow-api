using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Common;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

/// <summary>Filters for <c>GET /requests</c>. Dates are calendar days in UTC; both bounds are inclusive.</summary>
public sealed record ListRequestsQuery(
    int? Page,
    int? PageSize,
    string? Sort,
    RequestStatus? Status,
    RequestCategory? Category,
    decimal? MinAmount,
    decimal? MaxAmount,
    DateOnly? CreatedFrom,
    DateOnly? CreatedTo,
    string? Q)
{
    public const int SearchMaxLength = 100;

    public PageQuery Paging => new(Page, PageSize);
}

public sealed record RequestSummaryResponse(
    long Id,
    string Reference,
    string Title,
    RequestCategory Category,
    decimal Amount,
    RequestStatus Status,
    UserSummary Requester,
    int CurrentRound,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class ListRequestsQueryValidator : AbstractValidator<ListRequestsQuery>
{
    public ListRequestsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageQuery.MaxPageSize);
        RuleFor(x => x.Sort)
            .Must(RequestSorting.IsSupported)
            .WithMessage($"Sort must be one of: {string.Join(", ", RequestSorting.Fields)} (prefix with '-' for descending).");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.MinAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxAmount)
            .GreaterThanOrEqualTo(x => x.MinAmount ?? 0)
            .WithMessage("maxAmount must be greater than or equal to minAmount.");
        RuleFor(x => x.CreatedTo)
            .GreaterThanOrEqualTo(x => x.CreatedFrom)
            .When(x => x.CreatedFrom is not null && x.CreatedTo is not null)
            .WithMessage("createdTo must be on or after createdFrom.");
        RuleFor(x => x.Q).MaximumLength(ListRequestsQuery.SearchMaxLength);
    }
}

/// <summary>Whitelisted sort fields (NFR-07). The ID is always the tiebreaker, so paging is stable.</summary>
internal static class RequestSorting
{
    public static readonly string[] Fields = ["createdAt", "updatedAt", "amount", "reference"];

    public static bool IsSupported(string? sort) =>
        sort is null || Fields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase);

    public static IQueryable<Request> ApplySort(this IQueryable<Request> query, string? sort)
    {
        var descending = sort is null || sort.StartsWith('-');
        var field = (sort ?? "-createdAt").TrimStart('-').ToUpperInvariant();

        return (field, descending) switch
        {
            ("UPDATEDAT", false) => query.OrderBy(r => r.UpdatedAt).ThenBy(r => r.Id),
            ("UPDATEDAT", true) => query.OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Id),
            ("AMOUNT", false) => query.OrderBy(r => r.Amount).ThenBy(r => r.Id),
            ("AMOUNT", true) => query.OrderByDescending(r => r.Amount).ThenByDescending(r => r.Id),
            ("REFERENCE", false) => query.OrderBy(r => r.Id),
            ("REFERENCE", true) => query.OrderByDescending(r => r.Id), // the reference is derived from the ID
            (_, false) => query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
            (_, true) => query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
        };
    }
}

public static class ListRequestsEndpoint
{
    public static RouteHandlerBuilder MapListRequests(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/", ListAsync)
            .WithValidation<ListRequestsQuery>()
            .WithName("ListRequests")
            .WithSummary("Requests visible to the caller, filtered, sorted and paged.");

    private static async Task<Ok<PagedResponse<RequestSummaryResponse>>> ListAsync(
        [AsParameters] ListRequestsQuery query,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        CancellationToken cancellationToken)
    {
        var requests = db.Requests.VisibleTo(principal.ToActor(), db);

        if (query.Status is { } status)
        {
            requests = requests.Where(r => r.Status == status);
        }

        if (query.Category is { } category)
        {
            requests = requests.Where(r => r.Category == category);
        }

        if (query.MinAmount is { } min)
        {
            requests = requests.Where(r => r.Amount >= min);
        }

        if (query.MaxAmount is { } max)
        {
            requests = requests.Where(r => r.Amount <= max);
        }

        if (query.CreatedFrom is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            requests = requests.Where(r => r.CreatedAt >= start);
        }

        if (query.CreatedTo is { } to)
        {
            var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            requests = requests.Where(r => r.CreatedAt < endExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            // Reference prefix is index-friendly; title "contains" is a scan, acceptable at this scale (see design.md §7).
            var term = query.Q.Trim();
            requests = requests.Where(r => r.Reference.StartsWith(term) || r.Title.Contains(term));
        }

        var paging = query.Paging;
        var totalCount = await requests.CountAsync(cancellationToken);
        var items = await requests
            .ApplySort(query.Sort)
            .Skip(paging.Skip)
            .Take(paging.Size)
            .Select(r => new RequestSummaryResponse(
                r.Id,
                r.Reference,
                r.Title,
                r.Category,
                r.Amount,
                r.Status,
                db.Users.Where(u => u.Id == r.RequesterId).Select(u => new UserSummary(u.Id, u.DisplayName)).First(),
                r.CurrentRound,
                r.CreatedAt,
                r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResponse.Create(items, paging, totalCount));
    }
}
