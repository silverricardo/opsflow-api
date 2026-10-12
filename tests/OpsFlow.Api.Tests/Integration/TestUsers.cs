using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Tests.Integration;

internal sealed class TestUser(string login, string displayName, UserRole role, Func<TestUser?>? manager = null, bool inactive = false)
{
    public string Email { get; } = $"{login}@tests.local";

    public string DisplayName { get; } = displayName;

    public UserRole Role { get; } = role;

    public TestUser? Manager => manager?.Invoke();

    public bool Inactive { get; } = inactive;

    /// <summary>Assigned when the users are seeded.</summary>
    public int Id { get; set; }

    public override string ToString() => Email;
}

/// <summary>The organisation used by integration tests. Seeded once per test run.</summary>
internal static class TestUsers
{
    public const string Password = "Integration-Tests-Password-1";

    public static readonly TestUser Director = new("director", "Test Director", UserRole.Director);
    public static readonly TestUser Director2 = new("director2", "Test Director Two", UserRole.Director);
    public static readonly TestUser Admin = new("admin", "Test Admin", UserRole.Admin);
    public static readonly TestUser Manager1 = new("manager1", "Test Manager One", UserRole.Manager, () => Director);
    public static readonly TestUser Manager2 = new("manager2", "Test Manager Two", UserRole.Manager, () => Director);
    public static readonly TestUser Finance1 = new("finance1", "Test Finance One", UserRole.Finance, () => Director);
    public static readonly TestUser Finance2 = new("finance2", "Test Finance Two", UserRole.Finance, () => Director);
    public static readonly TestUser Employee1 = new("employee1", "Test Employee One", UserRole.Employee, () => Manager1);
    public static readonly TestUser Employee2 = new("employee2", "Test Employee Two", UserRole.Employee, () => Manager1);
    public static readonly TestUser Employee3 = new("employee3", "Test Employee Three", UserRole.Employee, () => Manager2);
    public static readonly TestUser InactiveEmployee = new("inactive", "Test Inactive", UserRole.Employee, () => Manager1, inactive: true);
    public static readonly TestUser InactiveManager = new("inactive.manager", "Test Inactive Manager", UserRole.Manager, () => Director, inactive: true);
    public static readonly TestUser OrphanedEmployee = new("orphaned", "Test Orphaned Employee", UserRole.Employee, () => InactiveManager);

    /// <summary>Managers are listed before their reports so IDs exist when referenced.</summary>
    public static readonly IReadOnlyList<TestUser> All =
    [
        Director, Director2, Admin, Manager1, Manager2, Finance1, Finance2, Employee1, Employee2, Employee3, InactiveEmployee,
        InactiveManager, OrphanedEmployee,
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        foreach (var testUser in All)
        {
            var user = new User(testUser.Email, testUser.DisplayName, testUser.Role, testUser.Manager?.Id, DateTimeOffset.UtcNow);
            user.SetPasswordHash(hasher.HashPassword(user, Password));
            if (testUser.Inactive)
            {
                user.Deactivate();
            }

            db.Users.Add(user);
            await db.SaveChangesAsync();
            testUser.Id = user.Id;
        }
    }
}
