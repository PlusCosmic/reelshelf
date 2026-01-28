using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nucleus.Shared.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DomainException domainException)
        {
            _logger.LogWarning(domainException, "Domain exception occurred: {Message}", domainException.Message);

            httpContext.Response.StatusCode = domainException.StatusCode;
            httpContext.Response.ContentType = "application/json";

            var problemDetails = new ProblemDetails
            {
                Status = domainException.StatusCode,
                Title = GetTitle(domainException),
                Detail = domainException.Message,
                Instance = httpContext.Request.Path
            };

            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            return true;
        }

        // Log unexpected exceptions but don't expose details to client
        _logger.LogError(exception, "An unexpected error occurred");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";

        var hostEnvironment = httpContext.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        var genericProblemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred while processing your request",
            Detail = hostEnvironment?.IsDevelopment() == true
                ? exception.Message
                : "Please try again later or contact support if the problem persists",
            Instance = httpContext.Request.Path
        };

        await httpContext.Response.WriteAsJsonAsync(genericProblemDetails, cancellationToken);
        return true;
    }

    private static string GetTitle(DomainException exception)
    {
        return exception switch
        {
            NotFoundException => "Resource Not Found",
            BadRequestException => "Bad Request",
            ConflictException => "Conflict",
            UnauthorizedException => "Unauthorized",
            ServiceUnavailableException => "Service Unavailable",
            _ => "An error occurred"
        };
    }
}
