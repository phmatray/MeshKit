using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeshKit.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Order>(order =>
        {
            order.HasIndex(o => o.StripeSessionId).IsUnique();
            order.HasIndex(o => o.UserId);
            order.Property(o => o.PackSlug).HasMaxLength(128);
            order.Property(o => o.StripeSessionId).HasMaxLength(256);
            order.Property(o => o.Currency).HasMaxLength(3);
        });

        builder.Entity<Entitlement>(entitlement =>
        {
            entitlement.HasIndex(e => new { e.UserId, e.PackSlug }).IsUnique();
            entitlement.Property(e => e.PackSlug).HasMaxLength(128);
        });
    }
}
