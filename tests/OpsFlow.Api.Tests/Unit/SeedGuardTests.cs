using Microsoft.Extensions.Hosting;
using Moq;
using OpsFlow.Api.Persistence.Seeding;

namespace OpsFlow.Api.Tests.Unit;

[TestFixture]
public class SeedGuardTests
{
    private const string StrongPassword = "Long-Enough-Password-1";

    [Test]
    public void EnsureSeedingAllowed_InProduction_Throws() =>
        Assert.That(
            () => SeedGuard.EnsureSeedingAllowed(Environment(Environments.Production), Options(StrongPassword)),
            Throws.InvalidOperationException.With.Message.Contains("Production"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("short-pw")]
    public void EnsureSeedingAllowed_WithoutStrongDemoPassword_Throws(string? password) =>
        Assert.That(
            () => SeedGuard.EnsureSeedingAllowed(Environment(Environments.Development), Options(password)),
            Throws.InvalidOperationException.With.Message.Contains("DemoPassword"));

    [TestCase("Development")]
    [TestCase("Docker")]
    public void EnsureSeedingAllowed_OutsideProductionWithStrongPassword_DoesNotThrow(string environmentName) =>
        Assert.That(() => SeedGuard.EnsureSeedingAllowed(Environment(environmentName), Options(StrongPassword)), Throws.Nothing);

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }

    private static SeedOptions Options(string? password) => new() { Enabled = true, DemoPassword = password };
}
