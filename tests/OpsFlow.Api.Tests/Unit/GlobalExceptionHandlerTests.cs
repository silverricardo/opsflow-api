using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsFlow.Api.Infrastructure.Errors;

namespace OpsFlow.Api.Tests.Unit;

[TestFixture]
public class GlobalExceptionHandlerTests
{
    [Test]
    public async Task ConcurrencyException_IsMappedTo412WithEtagMismatchCode()
    {
        var (problem, httpContext) = await HandleAsync(new DbUpdateConcurrencyException("lost the race"));

        Assert.Multiple(() =>
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status412PreconditionFailed));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ErrorCodes.ETagMismatch));
        });
    }

    [Test]
    public async Task UnexpectedException_IsMappedTo500WithoutInternals()
    {
        var (problem, httpContext) = await HandleAsync(new InvalidOperationException("connection string Password=secret"));

        Assert.Multiple(() =>
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ErrorCodes.ServerError));
            Assert.That(problem.ProblemDetails.Detail, Is.Null, "the exception message is never returned");
            Assert.That(problem.Exception, Is.Null);
        });
    }

    private static async Task<(ProblemDetailsContext Problem, HttpContext HttpContext)> HandleAsync(Exception exception)
    {
        ProblemDetailsContext? written = null;
        var problemDetails = new Mock<IProblemDetailsService>();
        problemDetails
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(context => written = context)
            .ReturnsAsync(true);
        var httpContext = new DefaultHttpContext();
        var handler = new GlobalExceptionHandler(problemDetails.Object, NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.That(handled, Is.True);
        problemDetails.Verify(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()), Times.Once);
        return (written!, httpContext);
    }
}
