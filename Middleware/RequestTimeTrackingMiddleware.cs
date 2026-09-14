using System.Diagnostics;

namespace PizzaStore.Middleware;

public class RequestTimeTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimeTrackingMiddleware> _logger;

    public RequestTimeTrackingMiddleware(RequestDelegate next, ILogger<RequestTimeTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Incoming HTTP {Method} {Path} from {RemoteIp}",
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress);

        // Append custom header before response stream writes begin
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Response-Time-Ms"] = stopwatch.ElapsedMilliseconds.ToString();
            return Task.CompletedTask;
        });

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
