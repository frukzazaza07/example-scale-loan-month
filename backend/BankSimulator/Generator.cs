using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BankSimulator;
public sealed class BankSender(IServiceScopeFactory scopes, IHttpClientFactory clients, IConfiguration config)
{
    public async Task Send(BankTransaction transaction, double lag, bool recovery, CancellationToken ct)
    {
        for (var retry = 0; retry < 5; retry++)
        {
            var attempt = new DeliveryAttempt { RunId = transaction.RunId, BankTransactionId = transaction.BankTransactionId, SchedulingLagMs = lag, Recovery = recovery };
            var clock = Stopwatch.StartNew(); double retryAfter = 0;
            try
            {
                var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(DemoAuth.Secret(config,"BANK_SECRET")), Encoding.UTF8.GetBytes(stamp + "." + transaction.Body)));
                using var request = new HttpRequestMessage(HttpMethod.Post, (config["WEBHOOK_URL"] ?? "http://localhost:5101") + "/webhooks/bank/payments");
                request.Content = new StringContent(transaction.Body, Encoding.UTF8,"application/json");
                request.Headers.Add("X-Bank-Timestamp",stamp); request.Headers.Add("X-Bank-Signature", transaction.InvalidSignature && !recovery ? new string('0',64) : signature);
                request.Headers.Add("X-Correlation-ID", transaction.BankTransactionId);
                using var response = await clients.CreateClient("bank").SendAsync(request,ct); attempt.ResponseCode = (int)response.StatusCode;
                retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 0;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { attempt.Timeout = true; attempt.Error = "RequestTimeout"; }
            catch (HttpRequestException) { attempt.Error = "NetworkFailure"; }
            attempt.DurationMs = clock.Elapsed.TotalMilliseconds;
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<SimulatorDb>();
            db.Attempts.Add(attempt); var row = await db.Transactions.SingleAsync(x => x.Id == transaction.Id,ct); row.LastSentAt = attempt.StartedAt;
            var retryable = attempt.ResponseCode is null or 429 or >= 500;
            if (!retryable || retry == 4) row.CompletedCopies++;
            await db.SaveChangesAsync(ct);
            if (!retryable || retry == 4) return;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(Math.Max(retryAfter, Math.Pow(2,retry)+Random.Shared.NextDouble()),0.1,60)),ct);
        }
    }
}
public sealed class Generator(IServiceScopeFactory scopes, BankSender sender, IConfiguration config, ILogger<Generator> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<SimulatorDb>();
                var run = await db.Runs.AsNoTracking().Where(x => x.Status == "Running").OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
                if (run is not null)
                {
                    // A dedicated live PostgreSQL session owns the generator. Connection loss cancels work;
                    // another replica can resume only after PostgreSQL releases the lock.
                    await using var ownership = new NpgsqlConnection(Hosting.Connection(config)); await ownership.OpenAsync(ct);
                    await using var claim = new NpgsqlCommand("SELECT pg_try_advisory_lock(72602)",ownership);
                    if ((bool)(await claim.ExecuteScalarAsync(ct))!)
                    {
                        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        await Run(run,db,ownership,runCancellation);
                    }
                }
                await Task.Delay(1000,ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { log.LogWarning("Simulator dependency unavailable: {Kind}",e.GetType().Name); await Task.Delay(3000,ct); }
        }
    }
    private async Task Run(SimulationRun run, SimulatorDb db, NpgsqlConnection ownership, CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token; var cfg = JsonSerializer.Deserialize<RunConfig>(run.ConfigJson,Contracts.Json)!;
        var rows = await db.Transactions.AsNoTracking().Where(x => x.RunId == run.Id && !x.Missing && x.CompletedCopies < x.PlannedCopies).OrderBy(x => x.Sequence).ToListAsync(ct);
        var tasks = new List<Task>(); var clock = Stopwatch.StartNew(); var index = 0; var maxLag = 0d;
        var startSequence = rows.FirstOrDefault()?.Sequence ?? 0;
        try
        {
            foreach (var item in rows)
            {
                if (index++ % cfg.TargetRps == 0)
                {
                    await using var ping = new NpgsqlCommand("SELECT 1",ownership); await ping.ExecuteScalarAsync(ct);
                    if (!await db.Runs.AnyAsync(x => x.Id == run.Id && x.Status == "Running",ct)) { cancellation.Cancel(); break; }
                }
                var due = TimeSpan.FromSeconds((item.Sequence - startSequence)/(double)cfg.TargetRps);
                var wait = due - clock.Elapsed; if (wait > TimeSpan.Zero) await Task.Delay(wait,ct);
                tasks.RemoveAll(x => x.IsCompletedSuccessfully);
                if (tasks.Count >= cfg.Concurrency) { var finished = await Task.WhenAny(tasks); await finished; tasks.Remove(finished); }
                var lag = Math.Max(0,(clock.Elapsed-due).TotalMilliseconds); maxLag = Math.Max(maxLag,lag);
                tasks.Add(SendCopies(item,lag,ct));
            }
            await Task.WhenAll(tasks);
            var saved = await db.Runs.SingleAsync(x => x.Id == run.Id,CancellationToken.None);
            if (saved.Status == "Running") saved.Status = "Completed"; saved.FinishedAt = DateTimeOffset.UtcNow; saved.MaximumLagMs = Math.Max(saved.MaximumLagMs,maxLag); await db.SaveChangesAsync(CancellationToken.None);
        }
        finally { cancellation.Cancel(); try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { } }
    }
    private async Task SendCopies(BankTransaction item, double lag, CancellationToken ct)
    {
        for (var copy = item.CompletedCopies; copy < item.PlannedCopies; copy++) await sender.Send(item,lag,false,ct);
    }
}
