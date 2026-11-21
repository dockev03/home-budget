namespace HomeBudget.Api.Infrastructure.Configuration;

/// <summary>Configuration applicative, section « HB » (variables d'environnement HB__*).</summary>
public sealed class HomeBudgetOptions
{
    public const string Section = "HB";

    /// <summary>Dossier des données : base SQLite, clés de protection, pièces jointes, sauvegardes.</summary>
    public string DataPath { get; set; } = "/data";

    /// <summary>URL publique de l'application (liens dans les e-mails et notifications).</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Réseaux (CIDR, séparés par des virgules) des reverse proxies autorisés à fournir X-Forwarded-*.</summary>
    public string? KnownProxyNetwork { get; set; }

    /// <summary>
    /// Jeton de premier lancement imposé (automatisation, tests E2E). S'il est vide, un jeton aléatoire
    /// est généré à chaque démarrage tant qu'aucun utilisateur n'existe, et écrit dans les logs.
    /// </summary>
    public string? SetupToken { get; set; }

    public MetricsOptions Metrics { get; set; } = new();

    public SecurityOptions Security { get; set; } = new();

    public BackupOptions Backup { get; set; } = new();

    public string DatabasePath => Path.Combine(DataPath, "homebudget.db");

    public string KeysPath => Path.Combine(DataPath, "keys");

    public string BackupsPath => Path.Combine(DataPath, "backups");
}

public sealed class BackupOptions
{
    /// <summary>Sauvegarde automatique quotidienne de la base (O3).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Heure locale (0 à 23) de la sauvegarde quotidienne.</summary>
    public int Hour { get; set; } = 3;

    public int KeepDaily { get; set; } = 7;

    public int KeepWeekly { get; set; } = 4;

    public int KeepMonthly { get; set; } = 12;
}

public sealed class MetricsOptions
{
    /// <summary>Réseaux (CIDR) autorisés à interroger /metrics. Par défaut : boucle locale uniquement.</summary>
    public string? AllowedNetwork { get; set; }
}

public sealed class SecurityOptions
{
    /// <summary>Nombre de requêtes d'authentification autorisées par minute et par adresse IP.</summary>
    public int AuthRequestsPerMinute { get; set; } = 10;

    /// <summary>
    /// Autorise les cookies sur HTTP non chiffré (développement local, tests E2E sans proxy).
    /// Ne jamais activer en production : derrière le NPM, la requête arrive en HTTPS.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }
}
