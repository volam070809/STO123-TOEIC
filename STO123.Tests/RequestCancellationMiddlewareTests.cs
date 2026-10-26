using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using STO123.Middleware;

namespace STO123.Tests;

public sealed class RequestCancellationMiddlewareTests
{
    [Fact]
    public async Task Client_cancellation_is_handled_without_becoming_a_server_error()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var context = new DefaultHttpContext { RequestAborted = source.Token };
        var middleware = new RequestCancellationMiddleware(
            _ => throw new TaskCanceledException("client disconnected"),
            NullLogger<RequestCancellationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task Cancellation_without_client_disconnect_is_not_suppressed()
    {
        var context = new DefaultHttpContext();
        var expected = new TaskCanceledException("database command timeout");
        var middleware = new RequestCancellationMiddleware(_ => throw expected,
            NullLogger<RequestCancellationMiddleware>.Instance);

        var actual = await Assert.ThrowsAsync<TaskCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
    }
}
