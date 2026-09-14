namespace PizzaStore.Middleware;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseRequestTimeTracking(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestTimeTrackingMiddleware>();
    }

    public static IApplicationBuilder UseCustomExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
