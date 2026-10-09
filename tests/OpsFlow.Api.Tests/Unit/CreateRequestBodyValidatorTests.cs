using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Tests.Unit;

[TestFixture]
public class CreateRequestBodyValidatorTests
{
    private readonly CreateRequestBodyValidator _validator = new();

    [Test]
    public void Validate_ValidBody_Passes() =>
        Assert.That(_validator.Validate(new CreateRequestBody("Laptop", null, RequestCategory.Purchase, 999.99m)).IsValid, Is.True);

    [TestCase(null, "Title")]
    [TestCase("  ab  ", "Title")]
    public void Validate_InvalidTitle_ReportsTitle(string? title, string property) =>
        AssertSingleError(new CreateRequestBody(title, null, RequestCategory.Purchase, 10m), property);

    [TestCase(null)]
    [TestCase("0")]
    [TestCase("1000000.01")]
    [TestCase("1.001")]
    public void Validate_InvalidAmount_ReportsAmount(decimal? amount) =>
        AssertSingleError(new CreateRequestBody("Laptop", null, RequestCategory.Purchase, amount), "Amount");

    [Test]
    public void Validate_UndefinedCategory_ReportsCategory() =>
        AssertSingleError(new CreateRequestBody("Laptop", null, (RequestCategory)42, 10m), "Category");

    private void AssertSingleError(CreateRequestBody body, string property)
    {
        var result = _validator.Validate(body);
        Assert.That(result.Errors.Select(e => e.PropertyName), Is.EqualTo(new[] { property }));
    }
}
