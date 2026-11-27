using System.Security.Claims;

using HomeBudget.Api.Domain;
using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Advances;
using HomeBudget.Api.Domain.Attachments;
using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Dashboard;
using HomeBudget.Api.Domain.Family;
using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Domain.Notifications;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Reconciliations;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Reports;
using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Domain.Savings;
using HomeBudget.Api.Domain.Scenarios;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Domain.Split;
using HomeBudget.Api.Domain.Tags;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Data;

/// <param name="http">
/// Requête en cours : sa session fixe les comptes visibles (R8). Sans requête (tâches de fond, démarrage,
/// outils), tous les comptes sont visibles.
/// </param>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http = null)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    /// <summary>
    /// Lève le filtre des comptes privés pour ce contexte : réservé aux traitements qui portent sur toute
    /// la base (sauvegarde et restauration par un administrateur, contrôles d'intégrité, fusion).
    /// </summary>
    public bool IgnoreVisibility { get; set; }

    /// <summary>Vrai quand aucun filtre de visibilité ne s'applique (lu par les filtres de requête globaux).</summary>
    public bool SeesAllAccounts => IgnoreVisibility || (ActAsViewer is null && http?.HttpContext is null);

    /// <summary>
    /// Tâche de fond qui calcule pour un utilisateur (alertes, résumé) : ses comptes privés et les comptes partagés
    /// sont visibles, comme dans sa session (R8).
    /// </summary>
    public Guid? ActAsViewer { get; set; }

    /// <summary>Utilisateur de la session ; <see cref="Guid.Empty"/> hors session (seuls les comptes partagés sont visibles).</summary>
    public Guid ViewerId => ActAsViewer
        ?? (Guid.TryParse(http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty);

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

    public DbSet<PeriodBudget> PeriodBudgets => Set<PeriodBudget>();

    public DbSet<Provision> Provisions => Set<Provision>();

    public DbSet<TransactionChange> TransactionChanges => Set<TransactionChange>();

    public DbSet<Scenario> Scenarios => Set<Scenario>();

    public DbSet<ScenarioEvent> ScenarioEvents => Set<ScenarioEvent>();

    public DbSet<AccountValuation> AccountValuations => Set<AccountValuation>();

    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();

    public DbSet<Loan> Loans => Set<Loan>();

    public DbSet<MemberShare> MemberShares => Set<MemberShare>();

    public DbSet<SavedReport> SavedReports => Set<SavedReport>();

    public DbSet<ChildcareDeclaration> ChildcareDeclarations => Set<ChildcareDeclaration>();

    public DbSet<Recurrence> Recurrences => Set<Recurrence>();

    public DbSet<RecurrenceOccurrence> RecurrenceOccurrences => Set<RecurrenceOccurrence>();

    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<LabelRule> LabelRules => Set<LabelRule>();

    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();

    public DbSet<Advance> Advances => Set<Advance>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    public DbSet<DashboardLayout> DashboardLayouts => Set<DashboardLayout>();

    public DbSet<SentAlert> SentAlerts => Set<SentAlert>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Webhook> Webhooks => Set<Webhook>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

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

        builder.Entity<HouseholdSettings>(e =>
        {
            ConfigureAudit(e);
            e.Property(s => s.EnvelopeSince).HasMaxLength(7);
        });

        builder.Entity<CategoryBudget>(e =>
        {
            ConfigureAudit(e);
            e.HasOne<Category>().WithMany().HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => b.CategoryId).IsUnique();
        });

        builder.Entity<PeriodBudget>(e =>
        {
            ConfigureAudit(e);
            e.Property(b => b.PeriodKey).HasMaxLength(7);
            e.HasOne<Category>().WithMany().HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => new { b.PeriodKey, b.CategoryId }).IsUnique();
        });

        builder.Entity<Provision>(e =>
        {
            ConfigureAudit(e);
            e.Property(p => p.Label).HasMaxLength(Provision.MaxLabelLength);
            e.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Scenario>(e =>
        {
            ConfigureAudit(e);
            e.Property(s => s.Name).HasMaxLength(Scenario.MaxNameLength);
            e.HasMany(s => s.Events).WithOne().HasForeignKey(v => v.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScenarioEvent>(e =>
        {
            ConfigureAudit(e);
            e.Property(v => v.Label).HasMaxLength(ScenarioEvent.MaxLabelLength);
            // Événements fictifs (F5) : ils disparaissent avec leur compte, et perdent seulement leur catégorie indicative.
            e.HasOne<Account>().WithMany().HasForeignKey(v => v.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Category>().WithMany().HasForeignKey(v => v.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(v => v.ScenarioId);
            e.HasQueryFilter(v => SeesAllAccounts || Accounts.Any(a => a.Id == v.AccountId));
        });

        builder.Entity<AccountValuation>(e =>
        {
            ConfigureAudit(e);
            e.Property(v => v.Note).HasMaxLength(AccountValuation.MaxNoteLength);
            // Les valorisations (A7) disparaissent avec leur compte, et suivent sa visibilité (R8).
            e.HasOne<Account>().WithMany().HasForeignKey(v => v.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => new { v.AccountId, v.Date }).IsUnique();
            e.HasQueryFilter(v => SeesAllAccounts || Accounts.Any(a => a.Id == v.AccountId));
        });

        builder.Entity<Loan>(e =>
        {
            ConfigureAudit(e);
            // Taux en pourcentage à 4 décimales : stocké en dix-millièmes, et non en centimes comme les montants.
            e.Property(l => l.AnnualRate).HasConversion(new ScaledDecimalConverter(Loan.RateDecimals));
            // Les conditions du crédit (A8) disparaissent avec leur compte, et suivent sa visibilité (R8).
            e.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.AccountId).IsUnique();
            e.HasQueryFilter(l => SeesAllAccounts || Accounts.Any(a => a.Id == l.AccountId));
        });

        builder.Entity<MemberShare>(e =>
        {
            ConfigureAudit(e);
            e.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.UserId).IsUnique();
        });

        builder.Entity<SavedReport>(e =>
        {
            ConfigureAudit(e);
            e.Property(r => r.Name).HasMaxLength(SavedReport.MaxNameLength);
            e.PrimitiveCollection(r => r.Kinds).ElementType().HasConversion<string>();
        });

        builder.Entity<ChildcareDeclaration>(e =>
        {
            ConfigureAudit(e);
            // Le salarié est un tiers : il ne peut pas disparaître tant qu'une déclaration le cite.
            e.HasOne<Payee>().WithMany().HasForeignKey(d => d.PayeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => new { d.Year, d.Month, d.PayeeId }).IsUnique();
        });

        builder.Entity<SavingsGoal>(e =>
        {
            ConfigureAudit(e);
            e.Property(g => g.Name).HasMaxLength(SavingsGoal.MaxNameLength);
            e.Property(g => g.Color).HasMaxLength(16);
            e.Property(g => g.Icon).HasMaxLength(40);
            // Un compte ou une catégorie rattachés à un objectif (J1) ne se suppriment pas : on les archive.
            e.HasOne<Account>().WithMany().HasForeignKey(g => g.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(g => g.CategoryId).OnDelete(DeleteBehavior.Restrict);
            // Un objectif rattaché à un compte privé n'est visible que de son propriétaire (R8).
            e.HasQueryFilter(g => SeesAllAccounts || g.AccountId == null || Accounts.Any(a => a.Id == g.AccountId));
        });

        builder.Entity<TransactionChange>(e =>
        {
            e.Property(c => c.UserName).HasMaxLength(AuditLog.MaxUserNameLength);
            e.HasOne<Transaction>().WithMany().HasForeignKey(c => c.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.TransactionId, c.At });
            // L'historique suit la visibilité de son opération (R8).
            e.HasQueryFilter(c => SeesAllAccounts || Transactions.Any(t => t.Id == c.TransactionId));
        });

        builder.Entity<Account>(e =>
        {
            ConfigureAudit(e);
            e.Property(a => a.Name).HasMaxLength(100);
            e.Property(a => a.Color).HasMaxLength(16);
            e.Property(a => a.Icon).HasMaxLength(40);
            e.HasOne<AppUser>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(a => a.HolderId).OnDelete(DeleteBehavior.SetNull);
            // R8 (DM5) : un compte privé n'est visible que de son propriétaire. Les filtres des opérations,
            // lignes, récurrences, échéances, rapprochements et imports s'appuient sur celui-ci.
            e.HasQueryFilter(a => SeesAllAccounts || a.Visibility == AccountVisibility.Shared || a.OwnerId == ViewerId);
        });

        builder.Entity<AppUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(AppUser.MaxDisplayNameLength);
            e.Ignore(u => u.Name);
        });

        builder.Entity<Invitation>(e =>
        {
            ConfigureAudit(e);
            e.Property(i => i.UserName).HasMaxLength(64);
            e.Property(i => i.DisplayName).HasMaxLength(AppUser.MaxDisplayNameLength);
            e.Property(i => i.Role).HasMaxLength(16);
            e.Property(i => i.TokenHash).HasMaxLength(64);
            e.HasIndex(i => i.TokenHash).IsUnique();
        });

        // Notifications (M1, M2) : données propres à chaque utilisateur, supprimées avec lui.
        builder.Entity<PushSubscription>(e =>
        {
            e.Property(p => p.Endpoint).HasMaxLength(PushSubscription.MaxEndpointLength);
            e.Property(p => p.P256dh).HasMaxLength(PushSubscription.MaxKeyLength);
            e.Property(p => p.Auth).HasMaxLength(PushSubscription.MaxKeyLength);
            e.Property(p => p.Device).HasMaxLength(PushSubscription.MaxDeviceLength);
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.Endpoint).IsUnique();
            e.HasIndex(p => p.UserId);
        });

        builder.Entity<NotificationPreference>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.UserId).IsUnique();
        });

        builder.Entity<DashboardLayout>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.UserId).IsUnique();
        });

        builder.Entity<SentAlert>(e =>
        {
            e.Property(a => a.ObjectKey).HasMaxLength(SentAlert.MaxKeyLength);
            e.Property(a => a.PeriodKey).HasMaxLength(SentAlert.MaxKeyLength);
            e.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            // Une alerte (type, objet, période) ne part qu'une fois par utilisateur.
            e.HasIndex(a => new { a.UserId, a.Kind, a.ObjectKey, a.PeriodKey }).IsUnique();
        });

        builder.Entity<Attachment>(e =>
        {
            ConfigureAudit(e);
            e.Property(a => a.FileName).HasMaxLength(Attachment.MaxFileNameLength);
            e.Property(a => a.ContentType).HasMaxLength(Attachment.MaxContentTypeLength);
            e.Property(a => a.Sha256).HasMaxLength(Attachment.HashLength);
            // Les pièces jointes (B7) disparaissent avec leur opération et suivent sa visibilité (R8) ;
            // le fichier, partagé par empreinte, est effacé quand plus aucune ne l'utilise.
            e.HasOne<Transaction>().WithMany().HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.TransactionId);
            e.HasIndex(a => a.Sha256);
            e.HasQueryFilter(a => SeesAllAccounts || Transactions.Any(t => t.Id == a.TransactionId));
        });

        // API publique (I5) : jetons personnels et webhooks, supprimés avec leur utilisateur.
        builder.Entity<ApiToken>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(ApiToken.MaxNameLength);
            e.Property(t => t.Hash).HasMaxLength(64);
            e.Property(t => t.Prefix).HasMaxLength(ApiToken.PrefixLength);
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.Hash).IsUnique();
            e.HasIndex(t => t.UserId);
        });

        // Sessions de connexion (N8), supprimées avec leur utilisateur. Dates en ticks UTC : SQLite ne compare ni ne
        // trie les DateTimeOffset.
        builder.Entity<UserSession>(e =>
        {
            e.Property(s => s.IpAddress).HasMaxLength(UserSession.MaxIpLength);
            e.Property(s => s.UserAgent).HasMaxLength(UserSession.MaxUserAgentLength);
            e.Property(s => s.SecurityStamp).HasMaxLength(256);
            e.Property(s => s.CreatedAt).HasConversion(UtcTicks);
            e.Property(s => s.LastSeenAt).HasConversion(UtcTicks);
            e.Property(s => s.RevokedAt).HasConversion(UtcTicks);
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.UserId, s.LastSeenAt });
        });

        builder.Entity<Webhook>(e =>
        {
            ConfigureAudit(e);
            e.Property(w => w.Name).HasMaxLength(Webhook.MaxNameLength);
            e.Property(w => w.Url).HasMaxLength(Webhook.MaxUrlLength);
            e.Property(w => w.ProtectedSecret).HasMaxLength(1000);
            e.HasOne<AppUser>().WithMany().HasForeignKey(w => w.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebhookDelivery>(e =>
        {
            e.Property(d => d.Event).HasMaxLength(32);
            e.Property(d => d.LastError).HasMaxLength(WebhookDelivery.MaxErrorLength);
            // SQLite ne compare ni ne trie les DateTimeOffset : les dates de la file sont stockées en ticks UTC.
            e.Property(d => d.CreatedAt).HasConversion(UtcTicks);
            e.Property(d => d.NextAttemptAt).HasConversion(UtcTicks);
            e.Property(d => d.CompletedAt).HasConversion(UtcTicks);
            e.HasOne<Webhook>().WithMany().HasForeignKey(d => d.WebhookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.Status, d.NextAttemptAt });
            e.HasIndex(d => new { d.WebhookId, d.CreatedAt });
        });

        builder.Entity<AuditLog>(e =>
        {
            e.Property(l => l.UserName).HasMaxLength(AuditLog.MaxUserNameLength);
            e.Property(l => l.Target).HasMaxLength(AuditLog.MaxTargetLength);
            e.Property(l => l.Details).HasMaxLength(AuditLog.MaxDetailsLength);
            e.Property(l => l.IpAddress).HasMaxLength(45);
            e.HasIndex(l => l.Action);
            e.HasIndex(l => l.UserId);
        });

        builder.Entity<Category>(e =>
        {
            ConfigureAudit(e);
            e.Property(c => c.Name).HasMaxLength(60);
            e.Property(c => c.Color).HasMaxLength(16);
            e.Property(c => c.Icon).HasMaxLength(40);
            e.Property(c => c.CarryOverSince).HasMaxLength(7);
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
            e.HasQueryFilter(t => SeesAllAccounts || Accounts.Any(a => a.Id == t.AccountId));
        });

        builder.Entity<Reconciliation>(e =>
        {
            ConfigureAudit(e);
            e.HasQueryFilter(r => SeesAllAccounts || Accounts.Any(a => a.Id == r.AccountId));
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
            e.HasQueryFilter(l => SeesAllAccounts || Transactions.Any(t => t.Id == l.TransactionId));
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
            // Un virement récurrent vers un compte privé reste invisible des autres membres.
            e.HasQueryFilter(r => SeesAllAccounts
                || (Accounts.Any(a => a.Id == r.AccountId) && (r.ToAccountId == null || Accounts.Any(a => a.Id == r.ToAccountId))));
        });

        builder.Entity<RecurrenceOccurrence>(e =>
        {
            ConfigureAudit(e);
            // Idempotence de la génération (R6) : une échéance n'existe qu'une fois.
            e.HasIndex(o => new { o.RecurrenceId, o.ScheduledDate }).IsUnique();
            e.HasIndex(o => new { o.Status, o.DueDate });
            e.HasQueryFilter(o => SeesAllAccounts || Recurrences.Any(r => r.Id == o.RecurrenceId));
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
            e.HasQueryFilter(b => SeesAllAccounts || Accounts.Any(a => a.Id == b.AccountId));
        });
    }

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long> UtcTicks =
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    private static void ConfigureAudit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : AuditableEntity =>
        e.Property(x => x.Version).IsConcurrencyToken();
}
