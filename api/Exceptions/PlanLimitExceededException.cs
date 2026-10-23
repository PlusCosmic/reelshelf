namespace Reelshelf.Exceptions;

/// <summary>The account's plan doesn't allow this clip (ADR-0007); the message says why and what to do.</summary>
public class PlanLimitExceededException(string message)
    : DomainException(message, StatusCodes.Status403Forbidden);
