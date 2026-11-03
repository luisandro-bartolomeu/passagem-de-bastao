using System.Security.Claims;
using System.Threading.RateLimiting;
using Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.Middlewares;

public static class RateLimitingExtensions
{
    public const string GeneralPolicy = "GeneralPolicy";
    public const string StrictPolicy = "StrictPolicy";

    public static IServiceCollection AddCustomRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (!context.HttpContext.Response.HasStarted)
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/problem+json";

                    var problemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too Many Requests",
                        Detail = "Limite de requisições excedido. Por favor, tente novamente mais tarde.",
                        Instance = context.HttpContext.Request.Path
                    };

                    var retryAfterSeconds = 60;
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter.TotalSeconds > 0)
                    {
                        retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                    }

                    context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                    problemDetails.Extensions["retryAfterSeconds"] = retryAfterSeconds;

                    var problemDetailsService = context.HttpContext.RequestServices.GetService<IProblemDetailsService>();
                    if (problemDetailsService != null && await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = problemDetails
                    }))
                    {
                        return;
                    }

                    await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
                }
            };

            // Global/General Limiter for all incoming requests
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var optionsValue = context.RequestServices.GetService<IOptions<RateLimitOptions>>()?.Value
                    ?? new RateLimitOptions();

                var partitionKey = GetPartitionKey(context, "global");

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = optionsValue.GeneralPermitLimit,
                        Window = TimeSpan.FromSeconds(optionsValue.GeneralWindowSeconds),
                        SegmentsPerWindow = 4,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Strict Policy for critical routes (/api/v1/auth and POST /api/v1/materials)
            options.AddPolicy(StrictPolicy, context =>
            {
                var optionsValue = context.RequestServices.GetService<IOptions<RateLimitOptions>>()?.Value
                    ?? new RateLimitOptions();

                var partitionKey = GetPartitionKey(context, "strict");

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = optionsValue.StrictPermitLimit,
                        Window = TimeSpan.FromSeconds(optionsValue.StrictWindowSeconds),
                        SegmentsPerWindow = 2,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // General Policy for explicit route assignment if needed
            options.AddPolicy(GeneralPolicy, context =>
            {
                var optionsValue = context.RequestServices.GetService<IOptions<RateLimitOptions>>()?.Value
                    ?? new RateLimitOptions();

                var partitionKey = GetPartitionKey(context, "general");

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = optionsValue.GeneralPermitLimit,
                        Window = TimeSpan.FromSeconds(optionsValue.GeneralWindowSeconds),
                        SegmentsPerWindow = 4,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }

    private static string GetPartitionKey(HttpContext context, string prefix)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"{prefix}_user_{userId}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ip))
        {
            ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "127.0.0.1";
        }

        return $"{prefix}_ip_{ip}";
    }
}
