using DataMart.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace DataMart.Application.Common.Behaviours;

public class UnhandledExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;

    public UnhandledExceptionBehaviour(ILogger<TRequest> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;
            var errorCode = ClassifyException(ex);

            _logger.LogError(ex, "DataMart Request: Unhandled Exception for Request {Name} ErrorCode={ErrorCode} {@Request}", requestName, errorCode, request);

            throw;
        }
    }

    private static ErrorCode ClassifyException(Exception ex) => ex switch
    {
        AppValidationException vx => vx.ErrorCode,
        DbException dx => dx.HResult == -2146232060 || dx.Message.Contains("57P01") || dx.Message.Contains("40001") 
            ? ErrorCode.TRANSIENT_ERROR 
            : ErrorCode.SYSTEM_ERROR,
        TimeoutException => ErrorCode.TRANSIENT_ERROR,
        TaskCanceledException => ErrorCode.TRANSIENT_ERROR,
        _ => ErrorCode.SYSTEM_ERROR
    };
}
