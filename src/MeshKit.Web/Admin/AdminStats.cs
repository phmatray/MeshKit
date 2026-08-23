using MeshKit.Web.Catalog;
using MeshKit.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace MeshKit.Web.Admin;

/// <summary>Everything the owner dashboard shows, read in one pass. No new data — the tables the store already keeps.</summary>
public sealed record AdminStats(
    int Accounts,
    int PaidOrders,
    int PendingOrders,
    IReadOnlyDictionary<string, long> RevenueByCurrency,
    int SampleDownloads,
    int SampleUsers,
    int SampleOptIns,
    int FollowUpsSent,
    int SampleUsersWhoBought,
    IReadOnlyList<PackRow> Packs,
    IReadOnlyList<OrderRow> RecentOrders,
    IReadOnlyList<SampleRow> TriedNotBought);

public sealed record PackRow(string Slug, string Name, int Paid, long Revenue, string Currency, int Samples, int SampleUsers, int SampleUsersWhoBought);

public sealed record OrderRow(DateTimeOffset CreatedAt, string Email, string PackSlug, long Amount, string Currency, OrderStatus Status);

public sealed record SampleRow(DateTimeOffset DownloadedAt, string Email, string PackSlug, string ModelSlug, bool OptIn, DateTimeOffset? FollowUpSentAt);

public sealed class AdminStatsReader(ApplicationDbContext db, ICatalogService catalog)
{
    public async Task<AdminStats> ReadAsync(CancellationToken cancellationToken)
    {
        var users = await db.Users.Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Id, u => u.Email ?? "(no email)", cancellationToken);
        var orders = await db.Orders.ToListAsync(cancellationToken);
        var entitlements = await db.Entitlements.Select(e => new { e.UserId, e.PackSlug }).ToListAsync(cancellationToken);
        var samples = await db.SampleDownloads.ToListAsync(cancellationToken);

        var paid = orders.Where(o => o.Status == OrderStatus.Paid).ToList();
        var owned = entitlements.Select(e => (e.UserId, e.PackSlug)).ToHashSet();
        var sampleUsers = samples.Select(s => s.UserId).Distinct().ToList();

        var packs = catalog.Sellable.Select(p =>
        {
            var packPaid = paid.Where(o => o.PackSlug == p.Slug).ToList();
            var packSamples = samples.Where(s => s.PackSlug == p.Slug).ToList();
            var packSampleUsers = packSamples.Select(s => s.UserId).Distinct().ToList();
            return new PackRow(
                p.Slug, p.Name, packPaid.Count, packPaid.Sum(o => o.AmountTotal), p.Price.Currency,
                packSamples.Count, packSampleUsers.Count, packSampleUsers.Count(u => owned.Contains((u, p.Slug))));
        }).ToList();

        string Email(string userId) => users.GetValueOrDefault(userId, "(deleted account)");

        return new AdminStats(
            Accounts: users.Count,
            PaidOrders: paid.Count,
            PendingOrders: orders.Count(o => o.Status == OrderStatus.Pending),
            RevenueByCurrency: paid.GroupBy(o => o.Currency).ToDictionary(g => g.Key, g => g.Sum(o => o.AmountTotal)),
            SampleDownloads: samples.Count,
            SampleUsers: sampleUsers.Count,
            SampleOptIns: samples.Count(s => s.FollowUpOptIn),
            FollowUpsSent: samples.Count(s => s.FollowUpOptIn && s.FollowUpSentAt is not null),
            SampleUsersWhoBought: sampleUsers.Count(u => entitlements.Any(e => e.UserId == u)),
            Packs: packs,
            RecentOrders: orders.OrderByDescending(o => o.CreatedAt).Take(20)
                .Select(o => new OrderRow(o.CreatedAt, Email(o.UserId), o.PackSlug, o.AmountTotal, o.Currency, o.Status)).ToList(),
            TriedNotBought: samples
                .GroupBy(s => (s.UserId, s.PackSlug))
                .Where(g => !owned.Contains(g.Key))
                .Select(g => g.OrderByDescending(s => s.DownloadedAt).First())
                .OrderByDescending(s => s.DownloadedAt).Take(50)
                .Select(s => new SampleRow(s.DownloadedAt, Email(s.UserId), s.PackSlug, s.ModelSlug, s.FollowUpOptIn, s.FollowUpSentAt)).ToList());
    }
}
