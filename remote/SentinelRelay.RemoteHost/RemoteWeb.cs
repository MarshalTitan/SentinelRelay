using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteHost;

public static class RemoteWeb
{
    public static WebApplication Create(RemoteAuthority authority, string tailnetHost, string tailnetUser, int port = 32179)
    {
        var allowedUsers = tailnetUser.Split(',').ToHashSet(StringComparer.Ordinal);
        if (!Uri.CheckHostName(tailnetHost).Equals(UriHostNameType.Dns) || !tailnetHost.EndsWith(".ts.net", StringComparison.Ordinal) ||
            tailnetHost.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) ||
            allowedUsers.Count is < 1 or > 8 || allowedUsers.Any(user => string.IsNullOrWhiteSpace(user) || user.Length > 254 || user.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))))
            throw new InvalidOperationException("invalid_tailnet_configuration");
        // Do not inherit URL bindings, forwarded headers, request logs, or command-line config.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 4096;
            options.Limits.MaxConcurrentConnections = 16;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("host", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddFixedWindowLimiter("pair", limiter =>
            { limiter.PermitLimit = 10; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var request = context.Request;
            // Serve terminates HTTPS on the same PC and supplies the verified login header.
            // Loopback is the explicit trusted proxy boundary; local user processes are trusted.
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
                request.Host.Value != tailnetHost || !allowedUsers.Contains(request.Headers["Tailscale-User-Login"].ToString()) ||
                (request.Headers.Origin.Count > 0 && request.Headers.Origin.ToString() != "https://" + tailnetHost))
            { context.Response.StatusCode = 403; return; }
            if (request.QueryString.HasValue || request.Method != "POST" || !request.HasJsonContentType())
            { context.Response.StatusCode = 400; return; }
            if (request.ContentLength > 4096)
            { context.Response.StatusCode = 413; return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            context.RequestAborted = timeout.Token;
            try { await next(context); }
            catch (Exception e) when (e is JsonException or BadHttpRequestException or InvalidDataException or IOException or OperationCanceledException)
            {
                if (!context.Response.HasStarted)
                {
                    var stateFailure = e is IOException and not BadHttpRequestException;
                    context.Response.StatusCode = e is BadHttpRequestException bad ? bad.StatusCode : stateFailure ? 503 : 400;
                    await context.Response.WriteAsJsonAsync(new Reply(false, stateFailure ? "state_unavailable" : "invalid_request"), Protocol.Json, CancellationToken.None);
                }
            }
        });
        app.UseRateLimiter();
        static string? Bearer(HttpRequest request)
        {
            var value = request.Headers.Authorization.ToString();
            return value.StartsWith("Bearer ", StringComparison.Ordinal) && Protocol.IsSecret(value[7..]) ? value[7..] : null;
        }
        static IResult Respond(Reply reply) => Results.Json(reply, Protocol.Json, statusCode: reply.Success ? 200 : reply.Code switch
        {
            "unauthorized" or "pairing_rejected" => 401,
            "remote_disabled" or "instance_not_authorized" or "control_not_authorized" => 403,
            "bridge_unavailable" => 503,
            _ => 400,
        });
        app.MapPost("/remote/v1/pair", async (HttpRequest request) => Respond(authority.Enroll(
            await request.ReadFromJsonAsync<Enrollment>(Protocol.Json, request.HttpContext.RequestAborted) ?? throw new JsonException())))
            .RequireRateLimiting("pair");
        app.MapPost("/remote/v1/session", async (HttpRequest request) => Respond(authority.OpenSession(Bearer(request),
            await request.ReadFromJsonAsync<SessionRequest>(Protocol.Json, request.HttpContext.RequestAborted) ?? throw new JsonException())));
        app.MapPost("/remote/v1/request", async (HttpRequest request) => Respond(authority.Handle(Bearer(request),
            await request.ReadFromJsonAsync<RemoteRequest>(Protocol.Json, request.HttpContext.RequestAborted) ?? throw new JsonException())));
        return app;
    }
}
