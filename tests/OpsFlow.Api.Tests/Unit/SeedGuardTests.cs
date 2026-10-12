using Microsoft.Extensions.Hosting;
using Moq;
using OpsFlow.Api.Persistence.Seeding;

namespace OpsFlow.Api.Tests.Unit;

[TestFixture]
public class SeedGuardTests
{
    private const string StrongPassword = "Long-Enough-Password-1";

    [TestCase("Production")]
    [TestCase("Staging")]
    [TestCase("Prod")]
    [TestCase("Docker")]
    public void EnsureSeedingAllowed_OutsideTheAllowList_Throws(string environmentName) =>
        Assert.That(
            () => SeedGuard.EnsureSeedingAllowed(Environment(environmentName), Options(StrongPassword)),
            Throws.InvalidOperationException.With.Message.Contains(environmentName));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("short-pw")]
    public void EnsureSeedingAllowed_WithoutStrongDemoPassword_Throws(string? password) =>
        Assert.That(
            () => SeedGuard.EnsureSeedingAllowed(Environment(Environments.Development), Options(password)),
            Throws.InvalidOperationException.With.Message.Contains("DemoPassword"));

    [TestCase("Development")]
    [TestCase("Testing")]
    [TestCase("performance")]
    public void EnsureSeedingAllowed_InAllowedEnvironmentWithStrongPassword_DoesNotThrow(string environmentName) =>
        Assert.That(() => SeedGuard.EnsureSeedingAllowed(Environment(environmentName), Options(StrongPassword)), Throws.Nothing);

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }

    private static SeedOptions Options(string? password) => new() { Enabled = true, DemoPassword = password };
}
