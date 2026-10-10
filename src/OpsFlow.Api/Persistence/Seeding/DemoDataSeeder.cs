using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Domain.Common;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Seeding;

/// <summary>
/// Creates a small, realistic organisation for local demos. Requests are created through the domain
/// methods, so seeded data always satisfies the business rules.
/// </summary>
public sealed class DemoDataSeeder(
    OpsFlowDbContext db,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock,
    ILogger<DemoDataSeeder> logger)
{
    public const string EmailDomain = "opsflow.local";

    public async Task SeedAsync(string demoPassword, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Database already contains users; demo seeding skipped.");
            return;
        }

        var now = clock.GetUtcNow();

        // Users are saved level by level because each level references its manager's generated ID.
        var director = await AddUserAsync("director", "Morgan Silva", UserRole.Director, null);
        var director2 = await AddUserAsync("director2", "Sofia Ramos", UserRole.Director, null);
        await AddUserAsync("admin", "Alex Admin", UserRole.Admin, null);
        var manager1 = await AddUserAsync("manager1", "Rita Costa", UserRole.Manager, director.Id);
        var manager2 = await AddUserAsync("manager2", "Tiago Almeida", UserRole.Manager, director.Id);
        await AddUserAsync("finance", "Inês Moreira", UserRole.Finance, director.Id);
        var employee1 = await AddUserAsync("employee1", "Ana Santos", UserRole.Employee, manager1.Id);
        var employee2 = await AddUserAsync("employee2", "Bruno Ferreira", UserRole.Employee, manager1.Id);
        var employee3 = await AddUserAsync("employee3", "Carla Mendes", UserRole.Employee, manager2.Id);
        await AddUserAsync("employee4", "Diogo Rocha", UserRole.Employee, manager2.Id);

        var finance = await db.Users.SingleAsync(u => u.Role == UserRole.Finance, cancellationToken);
        List<Actor> approvers = [Actor(finance), Actor(director), Actor(director2)];
        var policy = new ApprovalPolicy(await db.ApprovalPolicyRules.AsNoTracking().ToListAsync(cancellationToken));

        // One request per interesting state (Approved is shown by fulfilling one), so every screen of a demo has data.
        AddRequest(employee1, manager1, "Ergonomic office chair", RequestCategory.Purchase, 349.90m, submit: false);
        var catering = AddRequest(employee1, manager1, "Team offsite catering", RequestCategory.Expense, 1_850m, submit: true);
        var tickets = AddRequest(employee2, manager1, "Conference tickets (3 people)", RequestCategory.Expense, 7_200m, submit: true);
        AddRequest(employee3, manager2, "Server rack upgrade", RequestCategory.Purchase, 64_000m, submit: true);
        var dinner = AddRequest(employee3, manager2, "Client dinner", RequestCategory.Expense, 380m, submit: true);
        var licences = AddRequest(employee2, manager1, "Design software licences", RequestCategory.Purchase, 2_400m, submit: true);
        var toner = AddRequest(employee3, manager2, "Printer toner", RequestCategory.Purchase, 90m, submit: false);
        await db.SaveChangesAsync(cancellationToken); // steps need IDs before they can be decided

        Decide(catering, manager1, Decision.Approve, "Approved for Q4 offsite.");
        Apply(catering.Fulfil(Actor(finance), now));
        Decide(tickets, manager1, Decision.Approve, null); // now waiting in the Finance pool
        Decide(dinner, manager2, Decision.Reject, "Not a client meeting; use the team budget.");
        Decide(licences, manager1, Decision.RequestChanges, "Please split design and video licences.");
        Apply(toner.Cancel(Actor(employee3), now));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo data seeded: 10 users, 7 requests in six statuses.");

        static Actor Actor(User user) => new(user.Id, user.Role);

        static void Apply(Result result)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Seed data violates a business rule: {result.Error.Code}");
            }
        }

        void Decide(Request request, User decider, Decision decision, string? comment) =>
            Apply(request.Decide(request.CurrentRoundSteps().First(s => s.Status == StepStatus.Pending).Id, Actor(decider), decision, comment, now));

        async Task<User> AddUserAsync(string login, string displayName, UserRole role, int? managerId)
        {
            var user = new User($"{login}@{EmailDomain}", displayName, role, managerId, now);
            user.SetPasswordHash(passwordHasher.HashPassword(user, demoPassword));
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }

        Request AddRequest(User requester, User manager, string title, RequestCategory category, decimal amount, bool submit)
        {
            var request = Request.Create(Actor(requester), title, description: null, category, amount, now).Value;
            if (submit)
            {
                Apply(request.Submit(Actor(requester), Actor(manager), policy, approvers, now));
            }

            db.Requests.Add(request);
            return request;
        }
    }
}
