namespace STO123.Middleware;

public sealed class RequestCancellationMiddleware(
    RequestDelegate next,
    ILogger<RequestCancellationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request {Method} {Path} was canceled by the client.",
                context.Request.Method, context.Request.Path);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
    }
}
