using Microsoft.AspNetCore.Http;

namespace Nucleus.Shared.Exceptions;

public abstract class DomainException : Exception
{
    public int StatusCode { get; }

    protected DomainException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    protected DomainException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message, StatusCodes.Status404NotFound)
    {
    }

    public NotFoundException(string resourceType, object resourceId)
        : base($"{resourceType} with ID '{resourceId}' was not found", StatusCodes.Status404NotFound)
    {
    }
}

public class BadRequestException : DomainException
{
    public BadRequestException(string message) : base(message, StatusCodes.Status400BadRequest)
    {
    }

    public BadRequestException(string message, Exception innerException)
        : base(message, StatusCodes.Status400BadRequest, innerException)
    {
    }
}

public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message, StatusCodes.Status409Conflict)
    {
    }
}

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message) : base(message, StatusCodes.Status401Unauthorized)
    {
    }

    public UnauthorizedException() : base("User is not authenticated", StatusCodes.Status401Unauthorized)
    {
    }
}

public class ServiceUnavailableException : DomainException
{
    public ServiceUnavailableException(string message)
        : base(message, StatusCodes.Status503ServiceUnavailable)
    {
    }

    public ServiceUnavailableException(string message, Exception innerException)
        : base(message, StatusCodes.Status503ServiceUnavailable, innerException)
    {
    }
}
