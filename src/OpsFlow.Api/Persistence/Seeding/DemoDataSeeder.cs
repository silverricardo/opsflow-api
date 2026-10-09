using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        await AddUserAsync("admin", "Alex Admin", UserRole.Admin, null);
        var manager1 = await AddUserAsync("manager1", "Rita Costa", UserRole.Manager, director.Id);
        var manager2 = await AddUserAsync("manager2", "Tiago Almeida", UserRole.Manager, director.Id);
        await AddUserAsync("finance", "Inês Moreira", UserRole.Finance, director.Id);
        var employee1 = await AddUserAsync("employee1", "Ana Santos", UserRole.Employee, manager1.Id);
        var employee2 = await AddUserAsync("employee2", "Bruno Ferreira", UserRole.Employee, manager1.Id);
        var employee3 = await AddUserAsync("employee3", "Carla Mendes", UserRole.Employee, manager2.Id);
        await AddUserAsync("employee4", "Diogo Rocha", UserRole.Employee, manager2.Id);

        var policy = new ApprovalPolicy(await db.ApprovalPolicyRules.AsNoTracking().ToListAsync(cancellationToken));

        AddRequest(employee1, manager1, "Ergonomic office chair", RequestCategory.Purchase, 349.90m, submit: false);
        AddRequest(employee1, manager1, "Team offsite catering", RequestCategory.Expense, 1_850m, submit: true);
        AddRequest(employee2, manager1, "Conference tickets (3 people)", RequestCategory.Expense, 7_200m, submit: true);
        AddRequest(employee3, manager2, "Server rack upgrade", RequestCategory.Purchase, 64_000m, submit: true);
        AddRequest(employee3, manager2, "Taxi to client site", RequestCategory.Expense, 42.50m, submit: false);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo data seeded: 9 users, 5 requests.");

        async Task<User> AddUserAsync(string login, string displayName, UserRole role, int? managerId)
        {
            var user = new User($"{login}@{EmailDomain}", displayName, role, managerId, now);
            user.SetPasswordHash(passwordHasher.HashPassword(user, demoPassword));
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }

        void AddRequest(User requester, User manager, string title, RequestCategory category, decimal amount, bool submit)
        {
            var actor = new Actor(requester.Id, requester.Role);
            var request = Request.Create(actor, title, description: null, category, amount, now).Value;
            if (submit)
            {
                var submitted = request.Submit(actor, manager.Id, policy, now);
                if (!submitted.IsSuccess)
                {
                    throw new InvalidOperationException($"Seed data violates a business rule: {submitted.Error.Code}");
                }
            }

            db.Requests.Add(request);
        }
    }
}
