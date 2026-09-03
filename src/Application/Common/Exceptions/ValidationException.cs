using FluentValidation.Results;

namespace DataMart.Application.Common.Exceptions;

public enum ErrorCode
{
    USER_FIXABLE,      // Validation errors, missing data, user can correct
    SYSTEM_ERROR,      // DB deadlock, connection failure, infrastructure
    TRANSIENT_ERROR,   // Temporary network/DB issues, retryable
    PERMANENT_ERROR    // Unrecoverable (corrupt file, schema mismatch)
}

public class AppValidationException : Exception
{
    public AppValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
        ErrorCode = ErrorCode.USER_FIXABLE;
    }

    public AppValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
        ErrorCode = ErrorCode.USER_FIXABLE;
    }

    public AppValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
    }

    public AppValidationException(IEnumerable<ValidationFailure> failures, ErrorCode errorCode)
        : this(failures)
    {
        ErrorCode = errorCode;
    }

    public IDictionary<string, string[]> Errors { get; }

    public ErrorCode ErrorCode { get; }
}