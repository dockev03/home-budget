using HomeBudget.Api.Domain;
using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Advances;
using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Reconciliations;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Domain.Tags;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    /// <summary>
    /// Restauration d'une sauvegarde : les auteurs, dates et versions des entités ajoutées sont conservés
    /// au lieu d'être renseignés par <see cref="AuditInterceptor"/>.
    /// </summary>
    public bool PreserveAuditFields { get; set; }

    public DbSet<HouseholdSettings> HouseholdSettings => Set<HouseholdSettings>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Payee> Payees => Set<Payee>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionLine> TransactionLines => Set<TransactionLine>();

    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();

    public DbSet<Recurrence> Recurrences => Set<Recurrence>();

    public DbSet<RecurrenceOccurrence> RecurrenceOccurrences => Set<RecurrenceOccurrence>();

    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<LabelRule> LabelRules => Set<LabelRule>();

    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();

    public DbSet<Advance> Advances => Set<Advance>();

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

        builder.Entity<HouseholdSettings>(ConfigureAudit);

        builder.Entity<CategoryBudget>(e =>
        {
            ConfigureAudit(e);
            e.HasOne<Category>().WithMany().HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => b.CategoryId).IsUnique();
        });

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
            e.Property(t => t.RawLabel).HasMaxLength(500);
            e.Property(t => t.ImportHash).HasMaxLength(100);
            // Un relevé réimporté ne crée pas de doublon (H3), même lors de deux imports simultanés.
            e.HasIndex(t => new { t.AccountId, t.ImportHash }).IsUnique().HasFilter("\"ImportHash\" IS NOT NULL");
            e.HasOne<ImportBatch>().WithMany().HasForeignKey(t => t.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
            // Un rapprochement ne se supprime qu'en déverrouillant d'abord ses opérations.
            e.HasOne<Reconciliation>().WithMany().HasForeignKey(t => t.ReconciliationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.ReconciliationId);
            // Supprimer la dépense ou l'avance défait le lien, sans toucher au revenu.
            e.HasOne<Transaction>().WithMany().HasForeignKey(t => t.ReimbursesId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => t.ReimbursesId);
            e.HasOne<Advance>().WithMany().HasForeignKey(t => t.AdvanceId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => t.AdvanceId);
            e.Ignore(t => t.IsReconciled);
        });

        builder.Entity<Reconciliation>(e =>
        {
            ConfigureAudit(e);
            e.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => new { r.AccountId, r.Date }).IsUnique();
        });

        builder.Entity<Advance>(e =>
        {
            ConfigureAudit(e);
            e.Property(a => a.Label).HasMaxLength(Advance.MaxLabelLength);
            e.Property(a => a.Notes).HasMaxLength(2000);
        });

        builder.Entity<TransactionLine>(e =>
        {
            e.Property(l => l.Memo).HasMaxLength(200);
            e.HasOne<Category>().WithMany().HasForeignKey(l => l.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(l => l.Tags).WithMany().UsingEntity<Dictionary<string, object>>(
                "TransactionLineTag",
                r => r.HasOne<Tag>().WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                l => l.HasOne<TransactionLine>().WithMany().HasForeignKey("TransactionLineId").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("TransactionLineId", "TagId");
                    j.HasIndex("TagId");
                });
        });

        builder.Entity<Tag>(e =>
        {
            ConfigureAudit(e);
            e.Property(t => t.Name).HasMaxLength(Tag.MaxNameLength);
            e.Property(t => t.NormalizedName).HasMaxLength(Tag.MaxNameLength);
            e.HasIndex(t => t.NormalizedName).IsUnique();
        });

        builder.Entity<LabelRule>(e =>
        {
            ConfigureAudit(e);
            e.Property(r => r.Key).HasMaxLength(LabelKey.MaxLength);
            e.HasIndex(r => r.Key).IsUnique();
            // Une catégorie ou un tiers supprimé vide la règle au lieu de bloquer la suppression.
            e.HasOne<Payee>().WithMany().HasForeignKey(r => r.PayeeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Recurrence>(e =>
        {
            ConfigureAudit(e);
            e.Property(r => r.Label).HasMaxLength(200);
            e.Property(r => r.Notes).HasMaxLength(2000);
            e.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(r => r.ToAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Payee>().WithMany().HasForeignKey(r => r.PayeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Occurrences).WithOne().HasForeignKey(o => o.RecurrenceId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(r => r.Rule);
            e.Ignore(r => r.States);
            e.Ignore(r => r.AmountTolerance);
        });

        builder.Entity<RecurrenceOccurrence>(e =>
        {
            ConfigureAudit(e);
            // Idempotence de la génération (R6) : une échéance n'existe qu'une fois.
            e.HasIndex(o => new { o.RecurrenceId, o.ScheduledDate }).IsUnique();
            e.HasIndex(o => new { o.Status, o.DueDate });
            // L'opération générée peut être supprimée : l'échéance reste, pour ne pas être régénérée.
            e.HasOne<Transaction>().WithMany().HasForeignKey(o => o.TransactionId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ImportProfile>(e =>
        {
            ConfigureAudit(e);
            e.Property(p => p.Name).HasMaxLength(60);
            e.Property(p => p.DateFormat).HasMaxLength(16);
            e.HasIndex(p => p.Name).IsUnique();
            e.Ignore(p => p.Options);
            // Profil de l'export CSV de La Banque Postale (Q3) : « Date;Libellé;Montant(EUROS) ».
            var installedAt = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
            e.HasData(new ImportProfile
            {
                Id = ImportProfile.BanquePostaleId,
                Name = "La Banque Postale (CSV)",
                Delimiter = ';',
                DateFormat = "dd/MM/yyyy",
                DecimalSeparator = ',',
                DateColumn = 1,
                LabelColumn = 2,
                AmountColumn = 3,
                CreatedAt = installedAt,
                UpdatedAt = installedAt,
                Version = 1,
            });
        });

        builder.Entity<ImportBatch>(e =>
        {
            ConfigureAudit(e);
            e.Property(b => b.FileName).HasMaxLength(200);
            e.HasOne<Account>().WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAudit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : AuditableEntity =>
        e.Property(x => x.Version).IsConcurrencyToken();
}
