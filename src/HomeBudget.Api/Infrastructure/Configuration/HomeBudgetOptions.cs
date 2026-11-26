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

    public RecurrenceOptions Recurrences { get; set; } = new();

    public PushOptions Push { get; set; } = new();

    public SmtpOptions Smtp { get; set; } = new();

    public NotificationOptions Notifications { get; set; } = new();

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

public sealed class RecurrenceOptions
{
    /// <summary>Traitement automatique des échéances (au démarrage puis chaque jour).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Heure locale (0 à 23) du traitement quotidien.</summary>
    public int Hour { get; set; }
}

/// <summary>Notifications Web Push (M1) : désactivées tant que les deux clés VAPID ne sont pas renseignées.</summary>
public sealed class PushOptions
{
    /// <summary>Clé publique VAPID (point P-256 non compressé, base64url). Générer avec <c>--generate-vapid-keys</c>.</summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>Clé privée VAPID (32 octets, base64url). Secret : uniquement en variable d'environnement.</summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary>Contact transmis aux services push : « mailto:… » ou URL HTTPS. Par défaut, l'URL publique.</summary>
    public string? Subject { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(VapidPublicKey) && !string.IsNullOrWhiteSpace(VapidPrivateKey);
}

public enum SmtpSecurity
{
    /// <summary>SSL dès la connexion sur le port 465, STARTTLS sinon.</summary>
    Auto,

    /// <summary>SSL dès la connexion (port 465).</summary>
    SslOnConnect,

    /// <summary>STARTTLS obligatoire (port 587, Gmail).</summary>
    StartTls,

    /// <summary>Aucun chiffrement : serveur local uniquement.</summary>
    None,
}

/// <summary>Envoi d'e-mails (M2) : désactivé tant que l'hôte et l'expéditeur ne sont pas renseignés.</summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    public string? User { get; set; }

    /// <summary>Mot de passe (pour Gmail, un mot de passe d'application). Secret : uniquement en variable d'environnement.</summary>
    public string? Password { get; set; }

    /// <summary>Expéditeur, par exemple « Home Budget &lt;budget@exemple.fr&gt; ».</summary>
    public string? From { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

public sealed class NotificationOptions
{
    /// <summary>Vérification des alertes et envoi des résumés (sans effet si ni le push ni l'e-mail ne sont configurés).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Intervalle entre deux vérifications des alertes, en minutes.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Heure locale (0 à 23) à partir de laquelle partent les alertes « échéance demain » et le résumé.</summary>
    public int Hour { get; set; } = 8;
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
