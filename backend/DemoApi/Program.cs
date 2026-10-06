using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shared;
using DemoApi;

var builder = WebApplication.CreateBuilder(args); builder.AddDemo("DemoApi"); _ = DemoAuth.Secret(builder.Configuration,"AUTH_SECRET");
builder.Services.AddSingleton<Telemetry>(); builder.Services.AddHttpClient("telemetry",c => c.Timeout = TimeSpan.FromSeconds(3));
var app = builder.Build(); app.UseDemo();
app.Use(async (ctx,next) => {
    if (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsDelete(ctx.Request.Method)) { var origin = ctx.Request.Headers.Origin.FirstOrDefault(); if (origin is not null && (!Uri.TryCreate(origin,UriKind.Absolute,out var uri) || uri.Authority != ctx.Request.Host.Value)) { ctx.Response.StatusCode = 403; return; } }
    await next();
});
app.MapGet("/health/ready",() => Results.Ok(new { status = "ready" }));
app.MapPost("/api/v1/auth/login", (LoginRequest login,IConfiguration config,HttpContext ctx) =>
{
    if (login.Role is not ("Admin" or "Observer")) return Results.Unauthorized();
    var expected = config[login.Role == "Admin" ? "ADMIN_PASSWORD" : "OBSERVER_PASSWORD"];
    if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(login.Password)),SHA256.HashData(Encoding.UTF8.GetBytes(expected)))) return Results.Unauthorized();
    ctx.Response.Cookies.Append("lms_session",DemoAuth.Issue(config,login.Role.ToLowerInvariant(),login.Role),new CookieOptions { HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(8), Path = "/" });
    return Results.Ok(new { role = login.Role });
});
app.MapPost("/api/v1/auth/logout",(HttpContext ctx) => { ctx.Response.Cookies.Delete("lms_session"); return Results.Ok(); });
app.MapGet("/api/v1/auth/me",(HttpContext ctx) => Results.Ok(new { role = ctx.User.IsInRole("Admin") ? "Admin" : "Observer" })).RequireAuthorization();
var api = app.MapGroup("/api/v1").RequireAuthorization();
foreach (var (route,service) in new[] { ("loans","LOAN_URL"),("loans/{reference}","LOAN_URL"),("payments","LOAN_URL"),("payments/{transactionId}","LOAN_URL"),("receipts","WEBHOOK_URL"),("notifications","NOTIFICATION_URL"),("presets","SIMULATOR_URL"),("runs","SIMULATOR_URL"),("runs/{id}","SIMULATOR_URL"),("runs/{id}/settlement","SIMULATOR_URL"),("audit","SIMULATOR_URL"),("run-application/{runId}","LOAN_URL") })
    api.MapGet("/"+route,async (HttpContext ctx,IHttpClientFactory factory,IConfiguration config) => await Proxy(ctx,factory,config,service));
foreach (var (route,service) in new[] { ("runs","SIMULATOR_URL"),("runs/{id}/stop","SIMULATOR_URL"),("transactions/{transactionId}/redeliver","SIMULATOR_URL"),("payments/{transactionId}/resolve","LOAN_URL"),("payments/{transactionId}/replay","LOAN_URL"),("failures/{id}/replay","LOAN_URL") })
    api.MapPost("/"+route,async (HttpContext ctx,IHttpClientFactory factory,IConfiguration config) => await Proxy(ctx,factory,config,service)).RequireAuthorization("Admin");
api.MapGet("/overview",async (IHttpClientFactory factory,IConfiguration config,CancellationToken ct) => {
    var client = factory.CreateClient("services");
    var tasks = new[] { Fetch(client,config["LOAN_URL"]+"/api/v1/summary",ct), Fetch(client,config["WEBHOOK_URL"]+"/api/v1/summary",ct), Fetch(client,config["SIMULATOR_URL"]+"/api/v1/runs",ct) };
    var data = await Task.WhenAll(tasks); return Results.Ok(new { loans = data[0], webhook = data[1], runs = data[2], refreshedAt = DateTimeOffset.UtcNow, businessDate = "2026-10-31", runtimeMode = config["RUNTIME_MODE"] ?? "Compose" });
});
api.MapGet("/failures",async (IHttpClientFactory factory,IConfiguration config,CancellationToken ct) => {
    var client = factory.CreateClient("services"); var tasks = new[] { Fetch(client,config["LOAN_URL"]+"/api/v1/failures",ct),Fetch(client,config["WEBHOOK_URL"]+"/api/v1/failures",ct),Fetch(client,config["SIMULATOR_URL"]+"/api/v1/audit",ct) }; var data = await Task.WhenAll(tasks); return new { loan = data[0],webhook = data[1],simulatorAudit = data[2] };
});
api.MapGet("/metrics",async (Telemetry telemetry,CancellationToken ct) => await telemetry.Read(ct));
api.MapGet("/payment-journeys",async (IHttpClientFactory factory,IConfiguration config,int? page,CancellationToken ct) => {
    var client=factory.CreateClient("services");var receipts=await Fetch(client,$"{config["WEBHOOK_URL"]}/api/v1/transactions?page={Math.Max(1,page??1)}&pageSize=50",ct);
    if(receipts?["items"] is not JsonArray items) return Results.Problem("Receipt source unavailable",statusCode:503);
    var ids=items.Select(x=>x!["bankTransactionId"]!.GetValue<string>()).ToArray();
    var query=string.Join("&",ids.Select(id=>"transactionId="+Uri.EscapeDataString(id)));
    var payments=await Fetch(client,$"{config["LOAN_URL"]}/api/v1/payment-batch?{query}",ct);
    if(payments is not JsonArray results) return Results.Problem("Loan processing source unavailable",statusCode:503);
    return Results.Ok(new { receipts,payments=new {total=receipts["total"]!.GetValue<int>(),items=results} });
});
api.MapGet("/reconciliation/{runId:guid}",async (Guid runId,IHttpClientFactory factory,IConfiguration config,int? graceSeconds,CancellationToken ct) =>
{
    var client = factory.CreateClient("services"); var cutoff = DateTimeOffset.UtcNow; var grace = Math.Clamp(graceSeconds ?? 30,5,300); var rows = new List<object>(); var counts = new Dictionary<string,int>(); var totals = new Dictionary<string,long>(); var page = 1;
    var run = await Fetch(client,$"{config["SIMULATOR_URL"]}/api/v1/runs/{runId}",ct);
    var created = run?["run"]?["createdAt"]?.GetValue<DateTimeOffset>() ?? cutoff;
    while (true)
    {
        var settlement = await Fetch(client,$"{config["SIMULATOR_URL"]}/api/v1/runs/{runId}/settlement?page={page}&pageSize=50",ct);
        if (settlement?["items"] is not JsonArray bankRows) return Results.Problem("Settlement source unavailable",statusCode:503);
        if (bankRows.Count == 0) break;
        var ids = bankRows.Select(x => x!["bankTransactionId"]!.GetValue<string>()).ToArray();
        var query = string.Join("&",ids.Select(id => "transactionId="+Uri.EscapeDataString(id)));
        var applied = await Fetch(client,$"{config["LOAN_URL"]}/api/v1/settlement?{query}",ct);
        if (applied is not JsonArray payments) return Results.Problem("Loan settlement source unavailable",statusCode:503);
        var byId = payments.ToDictionary(x => x!["bankTransactionId"]!.GetValue<string>());
        var receiptData = await Fetch(client,$"{config["WEBHOOK_URL"]}/api/v1/settlement?{query}",ct);
        if (receiptData is not JsonArray receipts) return Results.Problem("Receipt source unavailable",statusCode:503);
        var received = receipts.Where(r => r!["receivedAt"]!.GetValue<DateTimeOffset>() <= cutoff).Select(r => r!["bankTransactionId"]!.GetValue<string>()).ToHashSet();
        foreach (var bank in bankRows)
        {
            var id = bank!["bankTransactionId"]!.GetValue<string>(); var amount = bank["amountMinor"]!.GetValue<string>(); var status = "NotYetApplied";
            if (byId.TryGetValue(id,out var p)) status = p!["amountMinor"]!.GetValue<string>() != amount ? "AmountConflict" : p["status"]!.GetValue<string>() == "Unmatched" ? "UnmatchedReference" : p["status"]!.GetValue<string>() is "Applied" or "AppliedWithCredit" ? (p["appliedAt"]!.GetValue<DateTimeOffset>() <= cutoff ? "Matched" : "NotYetApplied") : "OutcomeConflict";
            else
            {
                var sent = bank["lastSentAt"]?.GetValue<DateTimeOffset>();
                if (!received.Contains(id) && cutoff-(sent ?? created) > TimeSpan.FromSeconds(grace)) status = "MissingReceipt";
            }
            counts[status] = counts.GetValueOrDefault(status)+1; totals[status] = checked(totals.GetValueOrDefault(status)+long.Parse(amount,CultureInfo.InvariantCulture));
            // Return a bounded actionable sample; counts/totals always include the complete report.
            if (rows.Count < 500) rows.Add(new { bankTransactionId = id,paymentReference = bank["paymentReference"]!.GetValue<string>(),amountMinor = amount,status });
        }
        page++;
    }
    return Results.Ok(new { runId,cutoff,graceSeconds = grace,counts,totals = totals.ToDictionary(x => x.Key,x => Contracts.Money(x.Value)),items = rows,sampleLimit = 500 });
});
app.Run();
static async Task Proxy(HttpContext ctx,IHttpClientFactory factory,IConfiguration config,string service)
{
    try
    {
        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method),(config[service] ?? throw new InvalidOperationException("Service URL missing"))+ctx.Request.Path+ctx.Request.QueryString);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",ctx.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ","",StringComparison.Ordinal) ?? ctx.Request.Cookies["lms_session"]);
        request.Headers.Add("X-Correlation-ID",(string)ctx.Items["CorrelationId"]!);
        if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding")) { request.Content = new StreamContent(ctx.Request.Body); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
        using var response = await factory.CreateClient("services").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ctx.RequestAborted);
        ctx.Response.StatusCode = (int)response.StatusCode; ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        await response.Content.CopyToAsync(ctx.Response.Body,ctx.RequestAborted);
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ctx.RequestAborted.IsCancellationRequested) { ctx.Response.StatusCode = 503; await ctx.Response.WriteAsJsonAsync(new { title = "Service unavailable",status = 503 },ctx.RequestAborted); }
}
static async Task<JsonNode?> Fetch(HttpClient client,string url,CancellationToken ct)
{
    try { using var response = await client.GetAsync(url,ct); return response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) : new JsonObject { ["unavailable"] = true }; }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return new JsonObject { ["unavailable"] = true }; }
}
public record LoginRequest(string Role,string Password);
public partial class Program;
