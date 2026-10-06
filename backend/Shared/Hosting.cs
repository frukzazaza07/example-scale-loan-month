using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shared;

public static class Hosting
{
    public static void AddDemo(this WebApplicationBuilder b, string name)
    {
        b.Logging.ClearProviders(); b.Logging.AddJsonConsole(); b.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        b.Services.AddProblemDetails(); b.Services.AddOpenApi();
        b.Services.AddAuthentication("Demo").AddScheme<AuthenticationSchemeOptions, DemoAuth>("Demo", _ => { });
        b.Services.AddAuthorization(o => { o.AddPolicy("Admin", p => p.RequireRole("Admin")); });
        b.Services.AddSingleton<RuntimeMetrics>();
        b.Services.AddHttpClient("services", c => c.Timeout = TimeSpan.FromSeconds(10)).ConfigureHttpClient((sp, c) => c.DefaultRequestHeaders.Authorization = new("Bearer", DemoAuth.Issue(sp.GetRequiredService<IConfiguration>(), "service", "Admin")));
        b.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(name)).WithTracing(t => { t.AddSource("MonthEndLms").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation(); if (!string.IsNullOrEmpty(b.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) t.AddOtlpExporter(); });
    }
    public static void UseDemo(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.Use(async (ctx, next) =>
        {
            var supplied = ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault();
            var id = supplied is { Length: > 0 and <= 128 } && supplied.All(c => char.IsLetterOrDigit(c) || c is '-' or '_') ? supplied : Guid.NewGuid().ToString("N");
            ctx.Items["CorrelationId"] = id; ctx.Response.Headers["X-Correlation-ID"] = id;
            var start = Stopwatch.GetTimestamp();
            await next();
            app.Services.GetRequiredService<RuntimeMetrics>().Observe(ctx.Request.Path, ctx.Response.StatusCode, Stopwatch.GetElapsedTime(start).TotalSeconds);
        });
        app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/metrics", (RuntimeMetrics m) => Results.Text(m.Render(), "text/plain; version=0.0.4"));
        app.MapOpenApi().RequireAuthorization();
    }
    public static string Connection(IConfiguration c) => c["DATABASE_URL"] ?? "Host=localhost;Port=5432;Database=lms;Username=lms;Password=lms;Maximum Pool Size=5";
    public static async Task<bool> MigrateIfRequested<T>(WebApplication app, string[] args) where T : DbContext
    {
        if (!args.Contains("--migrate")) return false;
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<T>().Database.MigrateAsync();
        return true;
    }
    public static void Readiness<T>(this WebApplication app) where T : DbContext => app.MapGet("/health/ready", async (T db, CancellationToken ct) => {
        try { return await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException) { return Results.StatusCode(503); }
    });
}

public sealed class DemoAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public static string Secret(IConfiguration c, string name)
    {
        var value = c[name];
        if (string.IsNullOrWhiteSpace(value) || value.Length < 24) throw new InvalidOperationException($"{name} must contain at least 24 characters.");
        return value;
    }
    public static string Issue(IConfiguration c, string subject, string role)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{subject}|{role}|{DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds()}"));
        return payload + "." + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret(c, "AUTH_SECRET")), Encoding.UTF8.GetBytes(payload)));
    }
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.Ordinal) ?? Request.Cookies["lms_session"];
        if (token is null) return Task.FromResult(AuthenticateResult.NoResult());
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret(config, "AUTH_SECRET")), Encoding.UTF8.GetBytes(parts[0])))) throw new FormatException();
            var data = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])).Split('|');
            if (data.Length != 3 || long.Parse(data[2], CultureInfo.InvariantCulture) < DateTimeOffset.UtcNow.ToUnixTimeSeconds() || data[1] is not ("Admin" or "Observer")) throw new FormatException();
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, data[0]), new Claim(ClaimTypes.Role, data[1])], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
        catch (Exception e) when (e is FormatException or OverflowException) { return Task.FromResult(AuthenticateResult.Fail("Invalid session")); }
    }
}

public sealed class RuntimeMetrics
{
    private long requests, accepted; private double seconds; private readonly object gate = new();
    private readonly Histogram acceptance = new("lms_webhook_acceptance_seconds");
    private readonly Histogram application = new("lms_payment_application_seconds");
    public void Applied(double latency) { lock(gate) application.Observe(Math.Max(0,latency)); }
    public void Observe(string path, int status, double elapsed) { if (path is "/metrics" or "/health/live" or "/health/ready") return; lock (gate) { requests++; seconds += elapsed; if (path == "/webhooks/bank/payments" && status == 200) { accepted++; acceptance.Observe(elapsed); } } }
    public string Render() { lock (gate) return FormattableString.Invariant($"# TYPE lms_http_requests_total counter\nlms_http_requests_total {requests}\n# TYPE lms_webhook_accepted_total counter\nlms_webhook_accepted_total {accepted}\n# TYPE lms_http_duration_seconds_sum counter\nlms_http_duration_seconds_sum {seconds}\n") + acceptance.Render() + application.Render(); }
    private sealed class Histogram(string name)
    {
        private readonly double[] bounds = [.005,.01,.025,.05,.1,.25,.5,1,2,5,10,30,60,300];
        private readonly long[] buckets = new long[14]; private long count; private double sum;
        public void Observe(double value) { count++;sum+=value;for(var i=0;i<bounds.Length;i++)if(value<=bounds[i])buckets[i]++; }
        public string Render() { var text=new StringBuilder($"# TYPE {name} histogram\n");for(var i=0;i<bounds.Length;i++)text.Append(FormattableString.Invariant($"{name}_bucket{{le=\"{bounds[i]}\"}} {buckets[i]}\n"));text.Append(FormattableString.Invariant($"{name}_bucket{{le=\"+Inf\"}} {count}\n{name}_count {count}\n{name}_sum {sum}\n"));return text.ToString(); }
    }
}

