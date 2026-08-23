using MeshKit.Web.Catalog;
using MeshKit.Web.Data;
using MeshKit.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Samples;

/// <summary>
/// Sends the one follow-up email a sample downloader opted into, once the delay has passed. One email per
/// user and pack, never to someone who already bought the pack, and only while a promotion code is configured.
/// </summary>
public sealed class SampleFollowUpService(
    ApplicationDbContext db,
    ICatalogService catalog,
    IEmailQueue emails,
    IOptions<MeshKitOptions> options,
    ILogger<SampleFollowUpService> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Processes every due opt-in; returns how many emails were queued.</summary>
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.SampleFollowUp;
        if (!settings.Enabled)
        {
            return 0;
        }

        var now = _time.GetUtcNow();
        var cutoff = now - TimeSpan.FromHours(settings.DelayHours);
        // SQLite cannot order or compare DateTimeOffset in SQL; the unsent opt-in set is small, so filter in memory.
        var due = (await db.SampleDownloads
                .Where(d => d.FollowUpOptIn && d.FollowUpSentAt == null)
                .ToListAsync(cancellationToken))
            .Where(d => d.DownloadedAt <= cutoff)
            .OrderBy(d => d.DownloadedAt)
            .ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        foreach (var group in due.GroupBy(d => (d.UserId, d.PackSlug)))
        {
            var (userId, packSlug) = group.Key;
            var pack = catalog.Find(packSlug);
            var owns = await db.Entitlements.AnyAsync(e => e.UserId == userId && e.PackSlug == packSlug, cancellationToken);
            var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

            if (pack?.SampleModel is { } sample && !owns && !string.IsNullOrEmpty(email))
            {
                emails.Enqueue(EmailTemplates.SampleFollowUp(
                    email, pack.Name, sample.Name, options.Value.PublicBaseUrl.TrimEnd('/'), packSlug, settings.PromotionCode!, settings.DiscountLabel));
                sent++;
            }
            else
            {
                logger.LogInformation("Sample follow-up for {User}/{Pack} skipped: {Reason}", userId, packSlug,
                    owns ? "already bought" : pack?.SampleModel is null ? "pack or sample gone" : "no email on file");
            }

            // Either way this (user, pack) is settled: never re-evaluated, never emailed twice.
            foreach (var row in group)
            {
                row.FollowUpSentAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }
}

/// <summary>Runs <see cref="SampleFollowUpService.SendDueAsync"/> every 15 minutes. Fails open: an exception is logged and the next tick retries.</summary>
public sealed class SampleFollowUpWorker(IServiceScopeFactory scopes, ILogger<SampleFollowUpWorker> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider ?? TimeProvider.System);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var sent = await scope.ServiceProvider.GetRequiredService<SampleFollowUpService>().SendDueAsync(stoppingToken);
                    if (sent > 0)
                    {
                        logger.LogInformation("Queued {Count} sample follow-up email(s)", sent);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Sample follow-up pass failed; will retry next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
