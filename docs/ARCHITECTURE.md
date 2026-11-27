# Architecture — Home Budget

> Statut : **brouillon à valider** · Version 0.1 · Novembre 2026

## 1. Principes

- **Un seul déployable.** Un conteneur contient l'API ASP.NET Core et la PWA compilée en fichiers statiques. Pas de microservices, ni de bus, ni de cache externe.
- **Code rangé par fonctionnalité** (*vertical slices*). Pas d'architecture en couches multi-projets, pas de MediatR ni de CQRS.
- **Logique métier pure et testée.** Le moteur de prévision, le calcul des récurrences et le calcul des budgets sont des fonctions C# sans dépendance à la base de données.
- **Le contrat d'API est la source de vérité.** L'API publie sa description OpenAPI, et le client TypeScript du front est généré à partir d'elle.
- **Sécurisé par défaut, simple à exploiter.** Toute la configuration passe par des variables d'environnement et les données tiennent dans un seul volume.

## 2. Vue d'ensemble

```
             Internet / LAN
                   │ HTTPS
        ┌──────────▼──────────┐
        │ Nginx Proxy Manager │  (existant : TLS, certificats)
        └──────────┬──────────┘
                   │ HTTP :8080 (réseau Docker interne)
┌──────────────────▼─────────────────────────────────────────┐
│ Conteneur home-budget (utilisateur non root)               │
│                                                            │
│  PWA Vue 3 (wwwroot)    ◄── service worker, Dexie (local)   │
│        │ /api/*  (cookie de session + jeton anti-CSRF)     │
│  ASP.NET Core 10 ─ Minimal APIs ─ Identity ─ EF Core 10    │
│        │                                                   │
│  Tâches de fond : récurrences · alertes · e-mails ·        │
│                   sauvegardes · purge                      │
└────────┬───────────────────────────────────────────────────┘
         │
   volume /data
     ├─ homebudget.db      (SQLite, mode WAL)
     ├─ attachments/       (pièces jointes, nommées par empreinte SHA-256)
     └─ backups/           (instantanés quotidiens avec rotation)
```

## 3. Stack technique

| Couche | Choix | Remarques |
|---|---|---|
| Runtime | .NET 10 (LTS) | Image `mcr.microsoft.com/dotnet/aspnet:10.0`, utilisateur `app` non root |
| API | ASP.NET Core Minimal APIs | Validation intégrée, `TypedResults`, `ProblemDetails` |
| ORM | EF Core 10 + SQLite | Migrations versionnées, appliquées au démarrage |
| Auth | ASP.NET Core Identity (cookies) | Hachage PBKDF2-SHA512, verrouillage, TOTP, rôles |
| Documentation de l'API | OpenAPI intégré + Scalar | Accessible en `/api/docs` à tout utilisateur connecté ; `/openapi/v1.json` aussi avec un jeton d'accès (I5) |
| Observabilité | OpenTelemetry + export Prometheus, HealthChecks | `/metrics` restreint au réseau interne |
| E-mail | MailKit | SMTP configuré par variables d'environnement |
| Push | Web Push (VAPID) | Sans bibliothèque : chiffrement RFC 8291 et jeton VAPID RFC 8292 avec les primitives .NET (ECDH P-256, HKDF, AES-GCM, ES256) |
| Front | Vue 3 + TypeScript + Vite | Composition API, Pinia, Vue Router |
| Interface | Vuetify 3 | Composants mobiles : bouton flottant, menu du bas, tiroirs |
| Graphiques | Apache ECharts (`vue-echarts`) | Courbes, anneau, barres, Sankey, calendrier |
| PWA | `vite-plugin-pwa` (Workbox) | Coquille de l'appli mise en cache, mises à jour |
| Stockage local | Dexie (IndexedDB) et cache Workbox | File d'attente hors ligne (Dexie) ; réponses GET de l'API gardées par le service worker (cache `hb-api`) |
| Client API | `openapi-typescript` + `openapi-fetch` | Généré depuis `/openapi/v1.json` |
| Tests back | xUnit + `WebApplicationFactory` + SQLite en mémoire | Assertions via `Shouldly` (FluentAssertions v8 n'est plus libre de droits) |
| Tests front | Vitest | |
| Tests de bout en bout | Playwright | Lancés contre l'image Docker en CI |

## 4. Organisation du dépôt

```
home-budget/
├─ src/
│  ├─ HomeBudget.Api/
│  │  ├─ Program.cs
│  │  ├─ Domain/              # logique pure : Money, Period, Recurrence, Forecast, Budget
│  │  ├─ Features/            # une fonctionnalité = endpoints + DTO + requêtes
│  │  │  ├─ Accounts/
│  │  │  ├─ Transactions/
│  │  │  ├─ Categories/
│  │  │  ├─ Budgets/
│  │  │  ├─ Recurrences/
│  │  │  ├─ Forecast/
│  │  │  ├─ Dashboard/
│  │  │  ├─ Imports/
│  │  │  ├─ Learning/         # règles apprises
│  │  │  ├─ Attachments/
│  │  │  ├─ Users/  Auth/  Settings/  Backups/  Audit/  Notifications/  Webhooks/
│  │  ├─ Infrastructure/      # DbContext, migrations, intercepteurs, tâches de fond
│  │  └─ wwwroot/             # (généré) PWA compilée
│  └─ HomeBudget.Web/         # Vue 3 + Vite
│     ├─ src/api/             # client généré
│     ├─ src/features/        # même découpage que le back
│     ├─ src/components/
│     └─ src/offline/         # Dexie + file d'attente
├─ tests/
│  ├─ HomeBudget.Tests/       # unitaires (Domain) + intégration (API)
│  └─ e2e/                    # Playwright
├─ docs/
├─ Dockerfile
├─ docker-compose.yml
└─ .github/ (workflows, dependabot.yml)
```

## 5. Modèle de données

### 5.1 Décisions structurantes

| # | Décision | Raison |
|---|---|---|
| DM1 | **Montants en `decimal` à 2 décimales** dans le code et dans l'API, **stockés en centimes (`INTEGER`)** grâce à un convertisseur EF Core | Les calculs sont exacts, et SQLite sait additionner des entiers, alors qu'il ne sait pas additionner des décimaux. C'est invisible pour l'utilisateur et pour le code |
| DM2 | **Identifiants UUID v7** générés par le client ou par le serveur (`Guid.CreateVersion7()`) | Création hors ligne et synchronisation idempotente ; les identifiants restent triables par date |
| DM3 | **Opération = en-tête + lignes** | Ventilation, remboursements et virements traités sans cas particulier |
| DM4 | **Période abstraite** (`Period` : début, fin, libellé) calculée selon le paramètre du foyer | Permet la période d'une paie à l'autre sans réécriture |
| DM5 | **Propriétaire et visibilité sur chaque compte**, appliqués par un filtre de requête global EF Core | Impossible d'oublier un filtre (règle R8) |
| DM6 | **Audit automatique** par des intercepteurs `SaveChanges` : auteur et dates (K2), suppressions (N6), historique des opérations (B13, `TransactionHistoryInterceptor`), événements des webhooks (I5, `WebhookEventInterceptor`) | Aucune voie de modification (saisie, masse, pointage, import, rapprochement) ne peut l'oublier |

Tous les montants sont signés (négatif = sortie). Chaque entité porte `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, ainsi qu'un `Version` pour la concurrence optimiste.

### 5.2 Entités

```
User (Identity)            Id, UserName, Email, Role
HouseholdSettings          PeriodMode, PeriodStartDay, SafetyThreshold, ForecastIncludesIncome, AlertThresholds,
                           ForecastStrategy{Budget,Average}, ForecastAverageMonths   (F7),
                           EmergencyFundAverageMonths   (J3), SplitMode{Equal,IncomeProRata,FixedShares}   (K4),
                           EnvelopeSince? (D7 : période d'activation du mode enveloppes, null = désactivé)

Account                    Id, Name, Type{Checking,Savings,Investment,Cash,Credit}, InitialBalance, InitialDate,
                           OwnerId?, Visibility{Shared,Private}, Color, Icon, IncludeInForecast, IncludeInNetWorth,
                           OverdraftThreshold, IsEmergencyFund (J3), HolderId? (K4, comptes partagés), IsArchived
AccountValuation           AccountId, Date, Value, Note   (A7 ; unique par compte et date ; visibilité du compte, R8)
Loan                       AccountId (unique), Principal, AnnualRate (% à 4 décimales, stocké en dix-millièmes),
                           DurationMonths, FirstPaymentDate, MonthlyInsurance   (A8 ; visibilité du compte, R8)
Reconciliation             AccountId, Date, StatementBalance, Difference, ClosedAt                (A5)

CategoryGroup ─┐
Category       Id, ParentId?, Name, Kind{Income,Fixed,Variable,Savings}, Color, Icon, SortOrder, IsArchived,
               ExcludeFromSplit (K4 : dépenses personnelles, sous-catégories comprises),
               CarryOverSince? (D6 : première période reportée, null = report désactivé ; dépenses seulement),
               FamilyRole{None,Benefit,ChildcareAid,Childcare,Health} (P1, P2, P4 ; hérité du parent si None)
CategoryBudget             CategoryId, Mode{Amount,PercentOfIncome,Auto}, Value             (défaut)
PeriodBudget               CategoryId, PeriodKey, Mode, Value                               (D4 : remplace le défaut)
Provision                  Label, CategoryId, Amount, DueDate, IsAnnual, StartDate          (D11 : parts par période)

Payee                      Id, Name, DefaultCategoryId?, Reimburser{None,PrimaryInsurance,Mutual}   (P4)
LearnedRule                Id, Key (libellé normalisé), PayeeId?, CategoryId?, Hits, LastUsedAt   (I2–I4)
Tag                        Id, Name, Color

Transaction                Id, AccountId, Date, PayeeId?, Label, RawLabel?, Notes, Status{Posted,Pending},
                           IsCleared, ReconciliationId?, TransferId?, RecurrenceId?, OccurrenceDate?,
                           ImportBatchId?, ImportHash?
TransactionLine            Id, TransactionId, CategoryId?, Amount, Memo
TransactionLineTag         LineId, TagId
TransactionChange          TransactionId, At, UserId, UserName, Changes(JSON avant/après)   (B13)
TransactionLink            SourceId, TargetId, Type{Refund,Advance}, ExpectedAmount?   (B11, B12, P4)
Attachment                 Id, TransactionId, FileName, ContentType, Size, Sha256   (B7 ; visibilité de l'opération, R8)

Recurrence                 Id, AccountId, Label, PayeeId?, Frequency{Day,Week,Month,Year}, Interval,
                           StartDate, EndDate?, MaxOccurrences?, Mode{Auto,Confirm}, Tolerance,
                           BusinessDay{None,Next,Previous}, IsSubscription, TransferAccountId?, IsActive
RecurrenceLine             RecurrenceId, CategoryId, Amount
RecurrenceOccurrence       RecurrenceId, Date, State{Generated,Skipped,Matched}, TransactionId?,
                           ExpectedAmount? (P1 : montant de la récurrence figé au traitement de l'échéance)

SavingsGoal                Id, Name, TargetAmount, TargetDate?, AccountId? | CategoryId?, StartDate,
                           Color, Icon, IsArchived   (J1–J4 ; un compte ou une catégorie d'épargne)
Scenario                   Id, Name, IsActive                                              (F5)
ScenarioEvent              ScenarioId, Label, AccountId, CategoryId?, Amount, Date,
                           Frequency{Once,Monthly}, EndDate?   (visibilité du compte, R8)
MemberShare                UserId (unique), Percent   (K4 : parts fixes, 100 % au total)
SavedReport                Id, Name, GroupBy{Category,Subcategory,Payee,Tag,Period,Account},
                           Range{Custom,CurrentPeriod,PreviousPeriod,Last3Periods,Last12Periods,CurrentYear,PreviousYear},
                           From?, To?, AccountIds, CategoryIds, PayeeIds, TagIds, Kinds (listes JSON), IncludeTransfers   (G8)

ImportProfile              Id, Name, Delimiter, DateFormat, DecimalSeparator, DateColumn, LabelColumn, AmountColumn?, DebitColumn?, CreditColumn?   (CSV/TSV ; encodage détecté)
ImportBatch                Id, AccountId, FileName, Format{Csv,Ofx,Qif}, Created, Matched, Duplicates   (date et auteur : CreatedAt, CreatedBy)

AuditLog                   Id, At, UserId, Action, Entity, EntityId, Changes(JSON), Ip
PushSubscription           Id, UserId, Endpoint (unique), P256dh, Auth, Device?, CreatedAt, LastSuccessAt?   (M1, un par appareil)
NotificationPreference     Id, UserId (unique), BudgetAlerts, ForecastAlerts, DueTomorrowAlerts, PendingAlerts,
                           EmailSummary{None,Weekly,Monthly}   (M1, M2 ; sans ligne : toutes les alertes, pas de résumé)
DashboardLayout            Id, UserId (unique), Order (JSON), Hidden (JSON)   (accueil : ordre des blocs et blocs masqués ; sans ligne : disposition par défaut)
SentAlert                  Id, UserId, Kind, ObjectKey, PeriodKey, SentAt   (unique (UserId, Kind, ObjectKey, PeriodKey) ; purgé après 400 jours)
ApiToken                   Id, UserId, Name, Hash (SHA-256), Prefix, Scope{Read,Write}, CreatedAt, ExpiresAt, LastUsedAt   (I5)
Webhook                    Id, Name, Url, ProtectedSecret (Data Protection), Events (JSON), IsActive, OwnerId,
                           ConsecutiveFailures   (I5)
WebhookDelivery            Id, WebhookId, Event, Payload, Status{Pending,Succeeded,Failed}, Attempts, NextAttemptAt,
                           CompletedAt, LastStatusCode, LastError   (I5 : file d'attente, dates en ticks UTC ; gardée 30 jours)
```

## 6. Composants clés

### 6.1 Moteur de prévision (`Domain/Forecast`)

C'est une fonction pure. Elle reçoit en entrée :
- les soldes au jour J ;
- les opérations futures déjà saisies ;
- les échéances prévues ;
- les budgets de chaque période ;
- le réalisé ;
- les événements de scénario ;
- la stratégie de calcul (`Budget` ou `Average(n)`).

Elle produit en sortie : une série quotidienne, le solde de fin de chaque période, la date du premier passage sous le seuil, et le reste à vivre.

- Les scénarios (F5) ne sont que des événements supplémentaires passés en entrée. Ils produisent une série à part (« avec scénarios ») et ne modifient jamais la prévision de base.
- La prévision par moyenne (F7) n'est qu'une autre stratégie. `ForecastService` remplace le budget de chaque catégorie principale par le réalisé moyen des N périodes clôturées. Les échéances restent comptées telles quelles, et la moyenne ne couvre que ce qu'elles laissent.
- Horizon (F4) : la période en cours suit R3, et chaque période suivante part du solde de fin de la précédente.
- Les tests unitaires couvrent les fins de mois, les années bissextiles, les périodes à jour fixe, les remboursements et les budgets en pourcentage.

### 6.2 Récurrences

- **Calcul** (`Domain/Recurrence`) : la k-ième échéance est calculée depuis la date de départ, puis ajustée au jour ouvré (jours fériés français calculés, y compris Pâques).
- **Tâche `RecurrenceWorker`** : elle s'exécute au démarrage puis chaque nuit. Elle génère les échéances échues en mode automatique et place celles en mode à valider dans « À valider ».
- **Idempotence** : elle est garantie par `RecurrenceOccurrence`, avec une contrainte d'unicité sur (récurrence, date).

### 6.3 Règles apprises (I2–I4)

Il n'y a pas d'éditeur de règles. L'appli apprend des corrections :

1. **Normalisation du libellé brut** : passage en majuscules, suppression des préfixes bancaires (`PRLV SEPA`, `CB`, `VIR`…), des dates, des numéros et des références. On obtient une **clé**.
2. **Mémorisation** : quand l'utilisateur choisit ou corrige le tiers ou la catégorie d'une opération, la règle `LearnedRule(clé → tiers, catégorie)` est créée ou renforcée (`Hits++`).
3. **Suggestion** : à la saisie ou à l'import, la règle de la clé exacte est appliquée en priorité. À défaut, on prend le préfixe le plus long, puis le tiers le plus fréquent pour un texte proche.
4. **Proposition rétroactive** : après une correction, l'appli propose d'appliquer la même correction aux N opérations passées qui ont la même clé, avec un aperçu et une confirmation.
5. **Gestion** : une page liste les règles apprises, avec seulement la possibilité d'en supprimer une ; on ne les édite pas.

### 6.4 Hors ligne (L5)

- **Installation (L2)** : `vite-plugin-pwa` génère le manifeste et le service worker (Workbox). La coquille de l'appli (HTML, JS, CSS, icônes) est précachée ; une nouvelle version est proposée par un bandeau « Mettre à jour », sans rechargement forcé. L'enregistrement est fait par le bundle (`src/pwa.ts`) : aucun script inline (CSP).
- **Lecture** : les réponses GET de `/api/*` passent par une stratégie « réseau d'abord » (délai de 6 s) et sont gardées dans le cache `hb-api` (400 entrées, 31 jours), sauf les exports, les sauvegardes et le jeton anti-CSRF. Au démarrage, l'appli précharge le tableau de bord, le budget et le calendrier de la période en cours et des 2 précédentes, ainsi que la prévision. Le cache est vidé à la déconnexion.
- **Saisie** : une opération créée hors ligne (ou dont l'envoi échoue faute de réseau) est ajoutée à une file IndexedDB (Dexie) `{id UUIDv7, userId, request, status}` et s'affiche aussitôt dans **Opérations** avec le badge « en attente ».
- **Rejeu** : au retour du réseau, à l'ouverture de l'appli et à chaque retour sur l'onglet (la Background Sync API n'existe pas sur iOS), la file de l'utilisateur connecté est envoyée par lots de 100 à `POST /api/sync`. L'identifiant généré par l'appareil rend l'envoi idempotent : un élément déjà reçu répond `AlreadyApplied`. Un élément refusé (compte archivé, catégorie supprimée) reste dans la file avec le motif du serveur, que l'utilisateur corrige ou supprime : **le serveur l'emporte et l'utilisateur est prévenu**. Un lot refusé en bloc (400, élément mal formé) est renvoyé élément par élément.
- **Hors ligne, seule la création d'opérations est autorisée** : toute autre modification est refusée immédiatement par le client (`offlineGuard`), avec un message explicite. Les virements ne sont pas mis en file.

### 6.5 Sécurité

- **Session** : cookie `HttpOnly`, `Secure`, `SameSite=Strict`, durée glissante de 30 jours. Jeton anti-CSRF envoyé dans un en-tête.
- **Connexion** :
  - verrouillage Identity (5 échecs → 15 min) ;
  - `RateLimiter` sur `/api/auth/*` ;
  - TOTP avec codes de secours ;
  - la 2FA peut être rendue obligatoire pour les administrateurs.
- **En-têtes** :
  - CSP stricte (aucun script inline) ;
  - `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `frame-ancestors 'none'` ;
  - HSTS fixé par le NPM.
- **Derrière le reverse proxy** : `ForwardedHeaders` est activé, en faisant confiance uniquement au réseau Docker du NPM (`KnownNetworks`).
- **Pièces jointes** (B7, `Features/Attachments`) :
  - liste blanche de types de fichiers (JPEG, PNG, WebP, HEIC, PDF), reconnue sur les premiers octets du fichier, jamais sur l'extension ni le type annoncé ;
  - 10 Mo maximum, 20 par opération ; le fichier est envoyé tel quel dans le corps (pas de multipart) et écrit en flux, avec calcul de l'empreinte ;
  - servies en `Content-Disposition: attachment`, sauf les images JPEG, PNG et WebP, avec `nosniff` et une CSP `default-src 'none'; sandbox` ;
  - jamais exécutées, jamais gardées par le service worker ;
  - un fichier partagé par deux opérations n'est stocké qu'une fois ; il est effacé quand plus aucune pièce jointe ne l'utilise (aussitôt, ou par le nettoyage quotidien après suppression de l'opération).
- **API publique** (I5) : voir §6.10.
- **Secrets** (clés VAPID, SMTP) : uniquement en variables d'environnement. Les clés de protection des données ASP.NET sont stockées dans `/data/keys`.

### 6.6 Sauvegardes (O3, H6)

- **Tâche `BackupWorker`**, chaque nuit :
  1. un instantané cohérent de la base avec `VACUUM INTO '/data/backups/homebudget-AAAAMMJJ.db'` ;
  2. une rotation selon `BACKUP_KEEP_DAILY`, `_WEEKLY` et `_MONTHLY`.
- **Pièces jointes** : elles ne sont jamais modifiées (nommées par empreinte), donc le dossier `attachments/` peut être copié tel quel. La sauvegarde JSON (format 11) en contient la liste ; les fichiers sont exportés à part dans une archive ZIP (`GET /api/backups/attachments`), réimportable (`POST`), qui n'ajoute que les fichiers manquants dont l'empreinte correspond au nom.
- **Recommandation** : synchroniser `/data/backups` et `/data/attachments` vers un NAS ou un stockage hors de la machine (en dehors de l'appli).
- **Export et restauration JSON** depuis l'interface (administrateurs), avec un audit et une confirmation.

### 6.7 Notifications (M1, M2)

- **`AlertWorker`** : toutes les heures (`HB__Notifications__IntervalMinutes`), seulement si le push ou l'e-mail est configuré. Aucune notification de 22 h à `HB__Notifications__Hour` (8 h par défaut) : elles attendent la vérification suivante.
- **`AlertService`** calcule les alertes **pour chaque utilisateur, avec ses seuls comptes visibles** (R8 : `AppDbContext.ActAsViewer`) :
  - budget à 80 % puis à 100 % (seuils du foyer), seulement pour les catégories qui ont un budget ;
  - passage prévu sous le seuil des comptes de la prévision (F3), une fois par période ;
  - échéances de demain (à venir ou à valider reportées), regroupées en une notification ;
  - échéances à valider (E5), regroupées.
- **Une alerte ne part qu'une fois** : chaque envoi est enregistré par (utilisateur, type, objet, période) dans `SentAlert`, avant l'envoi (une panne du service push ne provoque pas de rafale au retour). Une alerte regroupée repart quand un nouvel objet s'y ajoute.
- **Web Push** (`WebPushSender`) : message JSON `{title, body, url, tag}` chiffré en `aes128gcm` (RFC 8291) et signé VAPID ES256 (RFC 8292), sans bibliothèque tierce ; le chiffrement est vérifié par le vecteur de test de la RFC. Un abonnement qui répond 404 ou 410 est supprimé. Le service worker (`public/push-sw.js`) affiche la notification et ouvre la page de l'appli au clic.
- **Adresses push limitées** aux services des navigateurs (`fcm.googleapis.com`, `push.services.mozilla.com`, `push.apple.com`, `notify.windows.com`, en HTTPS sur le port par défaut) : l'API ne peut pas servir à appeler une adresse du réseau interne.
- **Résumé par e-mail** (MailKit, `IEmailSender`) : le lundi pour la semaine écoulée, le 1er pour le mois écoulé. Revenus, dépenses et épargne de la période couverte, position de la période en cours (solde, solde prévu, reste à vivre), budgets en alerte, échéances à valider et des 7 prochains jours. Texte et HTML échappé, sans ressource externe. Un échec d'envoi est retenté à la vérification suivante.
- **Préférences** : par utilisateur (lecture seule compris), dans **Notifications**. L'adresse du résumé est l'e-mail du compte ; elle est modifiée sans changer le tampon de sécurité, pour ne pas fermer les sessions.
- Les abonnements, préférences et alertes envoyées sont propres à chaque utilisateur : ils ne font pas partie de la sauvegarde JSON, comme les comptes utilisateurs.

### 6.8 Épargne et patrimoine (A7, G5, J1–J4)

- **Solde d'un compte valorisé** (A7) : la dernière valorisation (valeur en fin de jour, opérations du jour comprises), plus les opérations datées après elle. Sans valorisation, le solde reste « solde initial + opérations ». Ce calcul vaut pour le solde affiché, le patrimoine net et les objectifs. La prévision et le rapprochement n'en tiennent pas compte : ils portent sur les mouvements réels des comptes courants.
- **Patrimoine net** (G5, `BalanceHistory`, fonction pure) : solde de chaque compte « inclus dans le patrimoine » en fin de chaque période (aujourd'hui pour la période en cours), archivés compris, sur 12, 24 ou 60 périodes. Avant sa date d'ouverture, un compte vaut 0.
- **Objectif d'épargne** (J1, J2, `SavingsGoalPlan`) : l'épargne réunie est le solde du compte rattaché, ou l'opposé du cumul des lignes de la catégorie d'épargne (sous-catégories comprises) depuis `StartDate`. Le versement conseillé répartit le reste à réunir sur les périodes restantes jusqu'à celle de la date cible, la période en cours comprise, arrondi au centime supérieur. L'épargne d'un enfant (J4) est un objectif rattaché à son livret ou à une catégorie dédiée.
- **Épargne de précaution** (J3) : solde des comptes marqués `IsEmergencyFund` (par défaut les comptes d'épargne), divisé par les dépenses fixes et variables moyennes des N dernières périodes clôturées. La moyenne ne remonte pas avant la période de la première opération.
- **Visibilité** (R8) : valorisations et objectifs rattachés à un compte suivent sa visibilité ; le cumul d'une catégorie ne compte que les opérations visibles.

### 6.9 Crédits, calendrier, rapports et répartition (A8, E8, G8, G9, K4)

- **Crédit** (A8, `LoanSchedule`, fonction pure) : mensualités constantes C × t / (1 − (1 + t)^−n), t = taux annuel / 12, arrondies au centime. Les intérêts de chaque mois portent sur le capital restant dû ; la dernière mensualité solde le capital. La k-ième échéance est calculée depuis la première (comme R6). Le capital restant dû à une date compte les mensualités échues ce jour-là. Il est affiché à côté du solde du compte de crédit, qui reste « solde initial + opérations » : il n'entre ni dans le patrimoine ni dans la prévision.
- **Calendrier** (E8) : les échéances de la période à leur date d'effet (jour ouvré E9, report manuel), avec leur état : à venir, à valider, passée (montant réel de l'opération), rapprochée ou sautée. Les échéances antérieures à la création de la récurrence, jamais générées, n'y figurent pas.
- **Rapport personnalisé** (G8) : lignes d'opérations filtrées par plage (dates saisies, ou périodes relatives calculées selon R1), comptes, catégories (une principale inclut ses sous-catégories), tiers, tags et types, regroupées par catégorie, sous-catégorie, tiers, tag (une ligne compte dans chacun de ses tags), période ou compte. Les virements sans catégorie sont exclus par défaut (R5). Export CSV ou Excel, tracé dans le journal d'audit. Les rapports enregistrés sont communs au foyer ; la fusion de catégories (C5) met à jour leurs filtres.
- **Flux** (G9) : réalisé de une à plusieurs périodes, des catégories de revenus vers « Revenus », puis vers les types (fixes, variables, épargne) et leurs catégories principales. Un excédent va en « Non dépensé », un déficit vient de « Pris sur les réserves ».
- **Répartition** (K4, `ExpenseSplit`, fonction pure) : seuls les comptes partagés comptent, pour que chaque membre voie le même résultat (R8).
  - Une dépense commune (fixe ou variable, hors catégories exclues) payée depuis un compte qui a un titulaire est apportée par ce titulaire.
  - Un virement vers un compte joint (sans titulaire) est un apport du membre à qui appartient le compte d'origine (son titulaire, ou le propriétaire s'il est privé). Seul ce lien est lu, jamais les opérations du compte privé ; un retrait du compte joint réduit l'apport.
  - Le total apporté est réparti entre les membres actifs : à parts égales, au prorata des revenus de la période sur les comptes partagés dont chacun est titulaire (parts égales sans revenu), ou selon des parts fixes. Solde = apporté − part ; les soldes s'annulent, et des remboursements conseillés les équilibrent.

### 6.10 API publique (I5)

- **Authentification** : un schéma de sélection (`HomeBudget`) envoie une requête portant `Authorization: Bearer` vers `ApiTokenAuthenticationHandler`, les autres vers le cookie de session. Un jeton invalide n'est jamais complété par le cookie.
- **Jetons** : `hb_` + 32 octets aléatoires (base64url), montrés une fois, stockés en SHA-256. Le principal du jeton porte l'identifiant de l'utilisateur (R8, auteur K2 et audit inchangés) et un rôle réduit : `Member` pour un jeton en écriture d'un utilisateur qui peut écrire, `ReadOnly` sinon. Jamais `Admin`.
- **Limites** (`ApiTokenRestrictionMiddleware`) : un jeton est refusé (403) sur `/api/auth`, `/api/account`, `/api/setup`, `/api/users`, `/api/invitations`, `/api/backups`, `/api/audit`, `/api/tokens`, `/api/webhooks`, `/api/notifications` et `/api/docs` ; un jeton en lecture n'envoie que des GET. Pas de contrôle anti-CSRF ni d'obligation de 2FA : pas de cookie, et un site tiers ne peut pas ajouter l'en-tête `Authorization`. Débit limité par jeton (limiteur global, partitionné par l'empreinte de l'en-tête).
- **Webhooks** : `WebhookEventInterceptor` met en file (`WebhookDelivery`) les opérations créées, modifiées ou supprimées et les imports terminés, dans la transaction de la modification, pour chaque webhook actif abonné, filtrés selon ce que voit son propriétaire (R8). `WebhookWorker` les envoie (`WebhookDispatcher`), signés `sha256=HMAC(secret, "{timestamp}.{corps}")`. Nouvelles tentatives à 1 min, 5 min, 30 min, 2 h et 12 h ; désactivation après 5 envois abandonnés à la suite.
- **Appels sortants** : client HTTP sans proxy ni redirection, délai de 10 s, et `ConnectCallback` qui vérifie chaque adresse **après la résolution DNS** (`WebhookAddressPolicy`) : boucle locale, link-local (métadonnées cloud), adresses non spécifiées et multicast toujours refusés ; réseaux privés selon `HB__Webhooks__AllowPrivateNetworks`.

### 6.11 Report du reste et mode enveloppes (D6, D7)

- **Calcul** : `BudgetBook` charge une fois catégories, budgets et provisions, puis calcule chaque période avec `BudgetCalculator`. Le report d'une catégorie dans la période P vaut la somme des restes « de base » (budget propre, provisions comprises − réalisé propre, sans report) des périodes depuis son activation jusqu'à P − 1 : reporter de proche en proche revient à cette somme, positive ou négative.
- **Périodes à venir** : elles ne reçoivent pas de report. La prévision (R3) suppose déjà que le reste de la période en cours sera dépensé ; le reporter sur la suivante le compterait deux fois.
- **Effets** : le report élargit (ou réduit) la jauge de la catégorie et le budget qui sortira des comptes (`CashBudgeted`, utilisé par la prévision), mais pas le récapitulatif D8, qui ne retient que ce que financent les revenus de la période.
- **Mode enveloppes** : option du foyer (`EnvelopeSince`). Toute dépense active reporte son reste depuis l'activation ; « à affecter » = Σ (revenus reçus − dépenses et épargne budgétées) de l'activation à la période affichée. Négatif, il signale qu'on a affecté plus que ce qu'on a reçu.
- **Déplacer** (`POST /api/budgets/{période}/moves`) : retire un montant du budget propre d'une catégorie (hors provisions et report) pour l'ajouter à une autre ; les deux deviennent des budgets propres à la période (D4), en montant.
- Une catégorie archivée ne reporte plus ; désactiver puis réactiver le report le fait repartir de la période en cours.

### 6.12 Suivis du foyer : aides, garde, santé (P1, P2, P4)

- **Rôles** : une catégorie porte un `FamilyRole` compatible avec son type (aides : revenus ; garde et santé : dépenses). Une sous-catégorie sans rôle prend celui de son parent. Les catégories par défaut « Allocation CAF », « CMG », « Nounou Maël » et « Santé » le reçoivent à la création, par la migration `FamilyTracking` sur une base existante, et à la restauration d'une sauvegarde antérieure au format 11.
- **Aides (P1)**, `GET /api/family/benefits/{année}` : chaque échéance des récurrences d'une catégorie d'aide, avec l'attendu figé (`ExpectedAmount`), le reçu (montant de l'opération) et l'écart = reçu − attendu (manque : sautée ou opération supprimée ; inconnu : à valider ou à venir). S'y ajoutent les versements hors échéancier (opérations d'une catégorie d'aide qui ne viennent d'aucune échéance). Le mode « à valider » (E5) est conseillé : la confirmation saisit le montant réellement versé.
- **Garde (P2)**, `GET /api/family/childcare/{année}` (et `/export` en CSV, audité) : frais payés (−Σ des lignes « garde ») moins aides à la garde reçues, par mois et par tiers ; montant déclarable = max(0, payé − aides). Aucune règle fiscale (plafond, taux du crédit d'impôt) n'est codée.
- **Santé (P4)**, `GET /api/family/health?from&to` : chaque opération dont les lignes « santé » sont une dépense, avec ses remboursements liés (B11) classés selon le `Reimburser` de leur tiers (CPAM, mutuelle, autre) et le reste à charge. Les remboursements classés en santé mais liés à aucune dépense sont totalisés à part. `PUT /api/payees/{id}/reimburser` désigne un organisme.
- **Années civiles** : celles de la déclaration d'impôts et de la CAF, indépendamment de la période budgétaire (R1).
- **Visibilité (R8)** : les filtres de requête du contexte s'appliquent ; un membre n'y voit jamais les comptes privés d'un autre.

## 7. Conventions d'API

- Une période est désignée par la clé `AAAA-MM` du mois dont elle porte le nom (en mode jour fixe N : le mois de début si N ≤ 15, le mois de fin sinon).
- Préfixe `/api`, noms en anglais, JSON en `camelCase`, dates au format ISO `YYYY-MM-DD`, montants en nombres décimaux à 2 décimales.
- Pagination par curseur pour les opérations (`?cursor=&limit=`).
- Erreurs au format `application/problem+json` (RFC 9457).
- Concurrence optimiste : l'en-tête `If-Match: <version>` est exigé pour un `PUT` ou un `DELETE`, et l'API répond `412` en cas de conflit.
- Authentification : cookie de session et en-tête `X-CSRF-TOKEN` depuis l'application ; `Authorization: Bearer hb_…` (jeton d'accès personnel, I5) depuis un script.
- Fichiers (pièces jointes, archive ZIP) : envoyés tels quels dans le corps de la requête, décrits en `format: binary` dans l'OpenAPI.
- Endpoints prévus pour le MVP : `/accounts`, `/categories`, `/payees`, `/transactions`, `/transfers`, `/recurrences`, `/recurrences/pending`, `/periods/current`, `/periods/{key}`, `/budgets/{period}`, `/budgets/categories/{id}`, `/dashboard/{period}`, `/forecast`, `/exports`, `/backups`, `/auth/*`, `/settings`.

## 8. Déploiement

### 8.1 Image

Le `Dockerfile` est en plusieurs étapes :
1. `node:22-alpine` compile la PWA ;
2. `dotnet/sdk:10.0` publie l'API, en y copiant la PWA dans `wwwroot` ;
3. `dotnet/aspnet:10.0` exécute l'appli sous l'utilisateur `app`, avec un `HEALTHCHECK` sur `/health/live`.

### 8.2 `docker-compose.yml` (stack Portainer, image tirée de GHCR)

```yaml
services:
  home-budget:
    image: ghcr.io/dockev03/home-budget:${HB_VERSION:-latest}
    container_name: home-budget
    restart: unless-stopped
    environment:
      TZ: Europe/Paris
      HB__PublicUrl: https://budget.example.fr
      HB__KnownProxyNetwork: 172.20.0.0/16
      HB__Backup__KeepDaily: 7
      HB__Push__VapidPublicKey: ""   # --generate-vapid-keys
      HB__Push__VapidPrivateKey: ""
      HB__Smtp__Host: ""
      HB__Smtp__From: ""
    volumes:
      - home-budget-data:/data
    networks: [npm]
    # pas de ports publiés : le NPM joint le conteneur par le réseau Docker

volumes:
  home-budget-data:

networks:
  npm:
    external: true
```

### 8.3 Variables d'environnement

| Variable | Rôle | Défaut |
|---|---|---|
| `HB__PublicUrl` | URL publique (liens dans les e-mails, notifications push) | — |
| `HB__KnownProxyNetwork` | Réseau du reverse proxy, à qui l'appli fait confiance | — |
| `HB__DataPath` | Dossier des données | `/data` |
| `HB__Backup__KeepDaily` / `__KeepWeekly` / `__KeepMonthly` | Rotation des sauvegardes | 7 / 4 / 12 |
| `HB__Smtp__Host` / `__Port` / `__Security` / `__User` / `__Password` / `__From` | Envoi d'e-mails (résumé) | désactivé |
| `HB__Push__VapidPublicKey` / `__VapidPrivateKey` / `__Subject` | Notifications push | désactivé |
| `HB__Notifications__Enabled` / `__IntervalMinutes` / `__Hour` | Vérification des alertes | `true` / 60 / 8 |
| `HB__Security__ApiRequestsPerMinute` | Débit autorisé par jeton d'accès personnel | 120 |
| `HB__Webhooks__Enabled` / `__AllowPrivateNetworks` / `__IntervalSeconds` | Envoi des webhooks, réseaux privés autorisés, passage de la file | `true` / `true` / 15 |
| `HB__Metrics__AllowedNetwork` | Réseau autorisé à interroger `/metrics` | réseau Docker |

## 9. CI/CD (GitHub Actions)

| Workflow | Déclencheur | Étapes |
|---|---|---|
| `ci.yml` | push, pull request | `dotnet build` / `dotnet test` (avec couverture), `npm ci` / lint / `vitest` / build, build de l'image Docker, Playwright contre le conteneur |
| `security.yml` | push, pull request, chaque semaine | CodeQL (C# et JS/TS), Trivy (fichiers et image, échec sur les failles HIGH/CRITICAL), `dependency-review` |
| `release.yml` | tag `vX.Y.Z` posé sur `main` | Contrôle que le commit est sur `main`, build de l'image (amd64), scan Trivy (échec sur HIGH/CRITICAL), publication sur GHCR : `X.Y.Z`, `X.Y`, `latest`, `sha-<court>` |
| `dependabot.yml` | chaque semaine | NuGet, npm, GitHub Actions, Docker |

Le serveur ne construit plus l'image : les fichiers compose tirent l'image versionnée `ghcr.io/dockev03/home-budget:${HB_VERSION}` publiée par `release.yml` (voir [DEPLOIEMENT.md](DEPLOIEMENT.md)).

## 10. Décisions écartées

| Option | Raison |
|---|---|
| Blazor WebAssembly | Lourd à charger sur mobile, hors ligne et graphiques plus pénibles à réaliser |
| PostgreSQL | Un service de plus à exploiter pour un usage domestique ; le passage reste possible plus tard (changement de pilote EF Core) |
| Node.js | Hors de la stack maîtrisée par le mainteneur |
| Clean Architecture multi-projets, MediatR | Complexité sans bénéfice à cette échelle |
| Hangfire ou Quartz | `BackgroundService` + `PeriodicTimer` suffisent |
| Synchronisation complète hors ligne (local-first à la Actual) | Coût très élevé ; la file d'attente de saisie couvre le besoin |
| Bibliothèque `WebPush` (.NET) | Dépendance de plus à surveiller (Trivy, Dependabot) ; le chiffrement et la signature tiennent en deux classes avec les primitives .NET |
| Background Sync API | Absente sur iOS : le rejeu se fait au retour du réseau, à l'ouverture et au retour sur l'onglet |
