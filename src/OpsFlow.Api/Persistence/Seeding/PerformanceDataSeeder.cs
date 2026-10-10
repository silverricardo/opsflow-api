using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Domain.Common;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Seeding;

/// <summary>
/// Bulk data for load tests (perf/README.md): about 230 users and N requests spread over the last
/// 12 months across every status. Everything goes through the domain, so the data obeys the business
/// rules and has realistic audit history. Deterministic (fixed random seed), so runs are comparable.
/// </summary>
public sealed partial class PerformanceDataSeeder(
    OpsFlowDbContext db,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock,
    ILogger<PerformanceDataSeeder> logger)
{
    public const string EmailDomain = "perf.local";
    public const int Directors = 2;
    public const int Managers = 20;
    public const int EmployeesPerManager = 10;
    public const int FinanceUsers = 5;
    private const int BatchSize = 2_000;

    private static readonly string[] Titles =
    [
        "Laptop replacement", "Conference travel", "Office chairs", "Software licences", "Client dinner",
        "Cloud credits", "Training course", "Monitors", "Team offsite", "Printer supplies", "Mobile phones",
        "Consulting services", "Server hardware", "Marketing materials", "Taxi to client site",
    ];

    public async Task SeedAsync(string password, int requestCount, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            LogSkipped(logger);
            return;
        }

        // Bulk batches can exceed the default 30 s on a loaded machine; this is a one-off load job.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var stopwatch = Stopwatch.StartNew();
        var now = clock.GetUtcNow();
        var users = await CreateUsersAsync(password, now, cancellationToken);
        var policy = new ApprovalPolicy(await db.ApprovalPolicyRules.AsNoTracking().ToListAsync(cancellationToken));
        var random = new Random(42);
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var offset = 0; offset < requestCount; offset += BatchSize)
        {
            var batch = new List<(Request Request, Plan Plan, Org Org)>();
            for (var i = offset; i < Math.Min(offset + BatchSize, requestCount); i++)
            {
                var org = users.Employees[random.Next(users.Employees.Count)];
                var createdAt = now.AddMinutes(-random.Next(1, 365 * 24 * 60));
                var amount = Amount(random);
                var request = Request.Create(
                    Actor(org.Employee), Titles[random.Next(Titles.Length)], null,
                    random.Next(4) == 0 ? RequestCategory.Expense : RequestCategory.Purchase, amount, createdAt).Value;

                var plan = PlanFor(random.Next(100));
                if (plan != Plan.Draft)
                {
                    Apply(request.Submit(Actor(org.Employee), Actor(org.Manager), policy, users.Approvers, createdAt.AddMinutes(5)));
                }

                db.Requests.Add(request);
                batch.Add((request, plan, org));
            }

            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(cancellationToken); // steps need IDs before decisions

            foreach (var (request, plan, org) in batch)
            {
                var at = (request.SubmittedAt ?? request.CreatedAt).AddHours(4);
                switch (plan)
                {
                    case Plan.ManagerApproved:
                        DecideAll(request, org, users, at, stopBeforeLast: true);
                        break;
                    case Plan.Fulfilled:
                        DecideAll(request, org, users, at, stopBeforeLast: false);
                        Apply(request.Fulfil(Actor(users.Finance[0]), at.AddDays(2)));
                        break;
                    case Plan.Approved:
                        DecideAll(request, org, users, at, stopBeforeLast: false);
                        break;
                    case Plan.Rejected:
                        Apply(request.Decide(CurrentStepId(request), Actor(org.Manager), Decision.Reject, "Out of budget.", at));
                        break;
                    case Plan.Cancelled:
                        Apply(request.Cancel(Actor(org.Employee), at));
                        break;
                }
            }

            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var done = Math.Min(offset + BatchSize, requestCount);
            LogProgress(logger, done, requestCount);
        }

        var seconds = stopwatch.Elapsed.TotalSeconds;
        LogDone(logger, requestCount, seconds);
    }

    private static decimal Amount(Random random) => random.Next(100) switch
    {
        < 70 => Math.Round((decimal)(random.NextDouble() * 4_900 + 20), 2),     // Manager only
        < 95 => Math.Round((decimal)(random.NextDouble() * 44_000 + 5_001), 2), // + Finance
        _ => Math.Round((decimal)(random.NextDouble() * 200_000 + 50_001), 2),  // + Director
    };

    private static Plan PlanFor(int roll) => roll switch
    {
        < 15 => Plan.Draft,
        < 45 => Plan.InApproval,
        < 55 => Plan.ManagerApproved,
        < 75 => Plan.Fulfilled,
        < 85 => Plan.Approved,
        < 95 => Plan.Rejected,
        _ => Plan.Cancelled,
    };

    /// <summary>Approves the steps in order with eligible users who have not decided in this round.</summary>
    private static void DecideAll(Request request, Org org, Users users, DateTimeOffset at, bool stopBeforeLast)
    {
        var steps = request.CurrentRoundSteps().ToList();
        var count = stopBeforeLast ? Math.Max(steps.Count - 1, 1) : steps.Count;
        foreach (var step in steps.Take(count))
        {
            var decider = step.Kind switch
            {
                StepKind.Manager => org.Manager,
                StepKind.Finance => users.Finance[(int)(request.Id % users.Finance.Count)],
                _ => users.Directors[(int)(request.Id % users.Directors.Count)],
            };
            Apply(request.Decide(step.Id, Actor(decider), Decision.Approve, null, at));
            at = at.AddHours(6);
        }
    }

    private static long CurrentStepId(Request request) =>
        request.CurrentRoundSteps().First(s => s.Status == StepStatus.Pending).Id;

    private async Task<Users> CreateUsersAsync(string password, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One hash reused for every user: PBKDF2 is deliberately slow, and these are throwaway load-test accounts.
        var hash = passwordHasher.HashPassword(new User("x@x", "x", UserRole.Employee, null, now), password);

        async Task<List<User>> AddAsync(IEnumerable<User> users)
        {
            var list = users.ToList();
            foreach (var user in list)
            {
                user.SetPasswordHash(hash);
            }

            db.Users.AddRange(list);
            await db.SaveChangesAsync(cancellationToken);
            return list;
        }

        var admin = await AddAsync([new User($"admin@{EmailDomain}", "Perf Admin", UserRole.Admin, null, now)]);
        var directors = await AddAsync(Enumerable.Range(1, Directors)
            .Select(i => new User($"director{i}@{EmailDomain}", $"Director {i}", UserRole.Director, null, now)));
        var finance = await AddAsync(Enumerable.Range(1, FinanceUsers)
            .Select(i => new User($"finance{i}@{EmailDomain}", $"Finance {i}", UserRole.Finance, directors[0].Id, now)));
        var managers = await AddAsync(Enumerable.Range(1, Managers)
            .Select(i => new User($"manager{i}@{EmailDomain}", $"Manager {i}", UserRole.Manager, directors[i % Directors].Id, now)));
        var employees = new List<Org>();
        foreach (var (manager, index) in managers.Select((m, i) => (m, i)))
        {
            var team = await AddAsync(Enumerable.Range(1, EmployeesPerManager).Select(j =>
            {
                var n = (index * EmployeesPerManager) + j;
                return new User($"employee{n}@{EmailDomain}", $"Employee {n}", UserRole.Employee, manager.Id, now);
            }));
            employees.AddRange(team.Select(e => new Org(e, manager)));
        }

        LogUsers(logger, admin.Count + directors.Count + finance.Count + managers.Count + employees.Count);
        return new Users(employees, finance, directors);
    }

    private static Actor Actor(User user) => new(user.Id, user.Role);

    private static void Apply(Result result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Performance seed violates a business rule: {result.Error.Code}");
        }
    }

    private enum Plan { Draft, InApproval, ManagerApproved, Approved, Fulfilled, Rejected, Cancelled }

    private sealed record Org(User Employee, User Manager);

    private sealed record Users(List<Org> Employees, List<User> Finance, List<User> Directors)
    {
        public IReadOnlyCollection<Actor> Approvers { get; } = [.. Finance.Concat(Directors).Select(Actor)];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database already contains users; performance seeding skipped.")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Performance seed: {Count} users created.")]
    private static partial void LogUsers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Performance seed: {Done}/{Total} requests.")]
    private static partial void LogProgress(ILogger logger, int done, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Performance seed finished: {Count} requests in {Seconds:F0} s.")]
    private static partial void LogDone(ILogger logger, int count, double seconds);
}
