using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Api.Errors;

/// <summary>
/// The one place where outcomes of the inner layers become HTTP responses.
///
/// Application and Domain know nothing about HTTP: they throw exceptions that
/// describe what happened. This handler maps each of those to a status code
/// and an RFC 9457 problem-details body whose <c>detail</c> is the exception's
/// user-facing message. Any other exception is a genuine bug: it is not
/// handled here, so the default handler returns a generic 500 without
/// leaking internals.
/// </summary>
public sealed class ApplicationExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public ApplicationExceptionHandler(IProblemDetailsService problemDetails) => _problemDetails = problemDetails;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = Map(exception);
        if (status is null)
            return false;

        httpContext.Response.StatusCode = status.Value;
        var problem = exception is ValidationException validation
            ? new HttpValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = title;
        problem.Detail = exception.Message;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    internal static (int? Status, string? Title) Map(Exception exception) => exception switch
    {
        DomainException => (StatusCodes.Status400BadRequest, "The request breaks a business rule."),
        ValidationException => (StatusCodes.Status400BadRequest, "Some fields are invalid."),
        AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Sign-in failed."),
        NotFoundException => (StatusCodes.Status404NotFound, "Not found."),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Not allowed."),
        // A lost race — the slot was just taken, or the resource was just edited
        // by someone else. Never a server error.
        ConflictException or ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Conflict."),
        _ => (null, null)
    };
}
