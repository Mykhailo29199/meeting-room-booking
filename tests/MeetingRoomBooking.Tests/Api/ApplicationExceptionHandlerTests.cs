using System.Text.Json;
using MeetingRoomBooking.Api.Errors;
using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRoomBooking.Tests.Api;

public class ApplicationExceptionHandlerTests
{
    public static TheoryData<Exception, int> MappedExceptions => new()
    {
        { new DomainException("Outside opening hours."), 400 },
        { new NotFoundException("The resource does not exist."), 404 },
        { new ForbiddenException("You can only cancel your own bookings."), 403 },
        { new ConflictException("Someone else has just booked this time."), 409 },
        { new ConcurrencyConflictException(new Exception("stale")), 409 },
    };

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public async Task Application_outcomes_become_problem_details_with_the_user_message(Exception exception, int expectedStatus)
    {
        var (handled, context) = await HandleAsync(exception);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedStatus, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(exception.Message, body.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [MemberData(nameof(UnexpectedExceptions))]
    public async Task Unexpected_exceptions_are_left_to_the_generic_500_handler(Exception exception)
    {
        // Includes a raw database conflict: services must translate it into
        // ConflictException first; if one ever leaks, that is a bug, and it
        // must not be silently reported as a normal conflict.
        var (handled, context) = await HandleAsync(exception);

        Assert.False(handled);
        Assert.Equal(0, context.Response.Body.Length);
    }

    public static TheoryData<Exception> UnexpectedExceptions => new()
    {
        new InvalidOperationException("bug"),
        new UniqueConstraintViolationException(new Exception("raw database error")),
    };

    private static async Task<(bool Handled, HttpContext Context)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var handler = new ApplicationExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        return (handled, context);
    }
}
