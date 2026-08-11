namespace Buddy.Proxy;

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        DeepSeekOptions deepSeek = builder.Configuration
            .GetSection(DeepSeekOptions.SectionName)
            .Get<DeepSeekOptions>() ?? new DeepSeekOptions();
        ProxyOptions proxy = builder.Configuration
            .GetSection(ProxyOptions.SectionName)
            .Get<ProxyOptions>() ?? new ProxyOptions();
        deepSeek.Validate();
        proxy.Validate(builder.Environment.ContentRootPath);

        builder.Services.AddSingleton(deepSeek);
        builder.Services.AddSingleton(proxy);
        builder.Services.AddSingleton<ProxyKeyHasher>();
        builder.Services.AddSingleton<ProxyDatabase>();
        builder.Services.AddSingleton<ProxyAuthentication>();
        builder.Services.AddSingleton<ClientRequestLock>();
        builder.Services.AddHttpClient<DeepSeekGateway>(
            client =>
            {
                client.BaseAddress = deepSeek.GetBaseUri();
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddRateLimiter(
            options =>
            {
                options.AddPolicy(
                    "proxy-api",
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString()
                            ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }));
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.ContentType =
                        "application/json; charset=utf-8";
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new
                        {
                            error = new
                            {
                                message = "Too many proxy requests. Try again in one minute.",
                                type = "buddy_proxy_error",
                                param = (string?)null,
                                code = ProxyErrorCodes.RateLimited,
                            },
                        },
                        cancellationToken)
                        .ConfigureAwait(false);
                };
            });

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseRateLimiter();
        ProxyDatabase database = app.Services.GetRequiredService<ProxyDatabase>();
        await database.InitializeAsync().ConfigureAwait(false);

        if (args.Length > 0
            && string.Equals(args[0], "admin", StringComparison.Ordinal))
        {
            return await AdminCommand
                .RunAsync(app.Services, args.Skip(1).ToArray())
                .ConfigureAwait(false);
        }

        app.MapBuddyProxy();
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
