namespace Reelshelf.Exceptions;

/// <summary>
/// Thrown when saving a clip would push the owner past their storage limit.
/// </summary>
public class StorageQuotaExceededException(string message)
    : DomainException(message, StatusCodes.Status403Forbidden);
