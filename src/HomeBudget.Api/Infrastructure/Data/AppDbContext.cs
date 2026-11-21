using HomeBudget.Api.Domain;
using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<HouseholdSettings> HouseholdSettings => Set<HouseholdSettings>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Payee> Payees => Set<Payee>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionLine> TransactionLines => Set<TransactionLine>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Tous les montants sont stockés en centimes (voir DM1 dans docs/ARCHITECTURE.md).
        configurationBuilder.Properties<decimal>().HaveConversion<CentsConverter>();
        // Les énumérations sont stockées en texte, lisible et robuste aux réordonnancements.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identifiants UUID v7 attribués par le code : une ligne ajoutée à une opération existante
        // doit être insérée, pas prise pour une ligne existante à mettre à jour.
        foreach (var entity in builder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entity.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }

        builder.Entity<Account>(e =>
        {
            ConfigureAudit(e);
            e.Property(a => a.Name).HasMaxLength(100);
            e.Property(a => a.Color).HasMaxLength(16);
            e.Property(a => a.Icon).HasMaxLength(40);
            e.HasOne<AppUser>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Category>(e =>
        {
            ConfigureAudit(e);
            e.Property(c => c.Name).HasMaxLength(60);
            e.Property(c => c.Color).HasMaxLength(16);
            e.Property(c => c.Icon).HasMaxLength(40);
            e.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Payee>(e =>
        {
            ConfigureAudit(e);
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.NormalizedName).HasMaxLength(100);
            e.HasIndex(p => p.NormalizedName).IsUnique();
        });

        builder.Entity<Transaction>(e =>
        {
            ConfigureAudit(e);
            e.Property(t => t.Label).HasMaxLength(200);
            e.Property(t => t.Notes).HasMaxLength(2000);
            e.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Payee>().WithMany().HasForeignKey(t => t.PayeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.AccountId, t.Date });
            e.HasIndex(t => t.Date);
            e.HasIndex(t => t.TransferId);
        });

        builder.Entity<TransactionLine>(e =>
        {
            e.Property(l => l.Memo).HasMaxLength(200);
            e.HasOne<Category>().WithMany().HasForeignKey(l => l.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAudit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : AuditableEntity =>
        e.Property(x => x.Version).IsConcurrencyToken();
}
