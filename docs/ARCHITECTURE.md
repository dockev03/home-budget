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
| Documentation de l'API | OpenAPI intégré + Scalar | Accessible en `/api/docs` (administrateurs uniquement) |
| Observabilité | OpenTelemetry + export Prometheus, HealthChecks | `/metrics` restreint au réseau interne |
| E-mail | MailKit | SMTP configuré par variables d'environnement |
| Push | Web Push (VAPID) | Bibliothèque `WebPush` |
| Front | Vue 3 + TypeScript + Vite | Composition API, Pinia, Vue Router |
| Interface | Vuetify 3 | Composants mobiles : bouton flottant, menu du bas, tiroirs |
| Graphiques | Apache ECharts (`vue-echarts`) | Courbes, anneau, barres, Sankey, calendrier |
| PWA | `vite-plugin-pwa` (Workbox) | Coquille de l'appli mise en cache, mises à jour |
| Stockage local | Dexie (IndexedDB) | Cache de lecture et file d'attente hors ligne |
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
| DM6 | **Audit automatique** par un intercepteur `SaveChanges` | Un seul mécanisme pour B13, K2 et N6 |

Tous les montants sont signés (négatif = sortie). Chaque entité porte `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, ainsi qu'un `Version` pour la concurrence optimiste.

### 5.2 Entités

```
User (Identity)            Id, UserName, Email, Role, IncomeShare?
HouseholdSettings          PeriodMode, PeriodStartDay, SafetyThreshold, ForecastIncludesIncome, AlertThresholds

Account                    Id, Name, Type{Checking,Savings,Investment,Cash,Credit}, InitialBalance, InitialDate,
                           OwnerId?, Visibility{Shared,Private}, Color, Icon, IncludeInForecast, IncludeInNetWorth,
                           OverdraftThreshold, IsArchived
AccountValuation           AccountId, Date, Value                       (A7)
Loan                       AccountId, Principal, AnnualRate, StartDate, Months, InsuranceMonthly   (A8)
Reconciliation             AccountId, Date, StatementBalance, Difference, ClosedAt                (A5)

CategoryGroup ─┐
Category       Id, ParentId?, Name, Kind{Income,Fixed,Variable,Savings}, Color, Icon, SortOrder, IsArchived
CategoryBudget             CategoryId, Mode{Amount,PercentOfIncome,Auto}, Value, Rollover   (défaut)
BudgetOverride             CategoryId, PeriodKey, Mode, Value                               (D4)
Provision                  CategoryId, AnnualAmount, DueMonth                               (D11)

Payee                      Id, Name, DefaultCategoryId?
LearnedRule                Id, Key (libellé normalisé), PayeeId?, CategoryId?, Hits, LastUsedAt   (I2–I4)
Tag                        Id, Name, Color

Transaction                Id, AccountId, Date, PayeeId?, Label, RawLabel?, Notes, Status{Posted,Pending},
                           IsCleared, ReconciliationId?, TransferId?, RecurrenceId?, OccurrenceDate?,
                           ImportBatchId?, ImportHash?
TransactionLine            Id, TransactionId, CategoryId?, Amount, Memo
TransactionLineTag         LineId, TagId
TransactionLink            SourceId, TargetId, Type{Refund,Advance}, ExpectedAmount?   (B11, B12, P4)
Attachment                 Id, TransactionId, FileName, ContentType, Size, Sha256

Recurrence                 Id, AccountId, Label, PayeeId?, Frequency{Day,Week,Month,Year}, Interval,
                           StartDate, EndDate?, MaxOccurrences?, Mode{Auto,Confirm}, Tolerance,
                           BusinessDay{None,Next,Previous}, IsSubscription, TransferAccountId?, IsActive
RecurrenceLine             RecurrenceId, CategoryId, Amount
RecurrenceOccurrence       RecurrenceId, Date, State{Generated,Skipped,Matched}, TransactionId?

SavingsGoal                Id, Name, TargetAmount, TargetDate, AccountId?, CategoryId?   (J1–J4)
Scenario / ScenarioEvent   Id, Name, IsActive / Date, Amount, Label, CategoryId?, AccountId   (F5)
SplitSetting               Mode{Equal,IncomeRatio,Fixed}, Shares, SharedCategoryIds   (K4)

ImportProfile              Id, Name, Format{Csv,Ofx,Qif}, Encoding, Delimiter, DateFormat, DecimalSep, Mapping(JSON)
ImportBatch                Id, AccountId, FileName, ImportedAt, Count, Duplicates

AuditLog                   Id, At, UserId, Action, Entity, EntityId, Changes(JSON), Ip
PushSubscription           UserId, Endpoint, P256dh, Auth, Preferences(JSON)
ApiToken                   Id, UserId, Name, Hash, Scopes, ExpiresAt, LastUsedAt   (I5)
Webhook                    Id, Url, Secret, Events, IsActive                       (I5)
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

- Les scénarios (F5) ne sont que des événements supplémentaires passés en entrée, et la prévision par moyenne (F7) n'est qu'une autre stratégie.
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

### 6.4 Hors ligne (L4)

- Le service worker met en cache la coquille de l'appli et les données de référence (comptes, catégories, tiers) ainsi que les 3 dernières périodes, stockées dans Dexie.
- Une saisie faite hors ligne est ajoutée à une file d'attente locale (`{id UUIDv7, type, payload}`) et s'affiche immédiatement avec un badge « en attente ».
- Au retour du réseau (ou à l'ouverture de l'appli : la Background Sync API n'existe pas sur iOS), la file est rejouée via `POST /api/sync`. Le serveur est idempotent grâce aux identifiants générés côté client.
- En cas de conflit de `Version`, c'est le serveur qui l'emporte et l'utilisateur est prévenu.
- Hors ligne, seule la création d'opérations est autorisée.

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
- **Pièces jointes** :
  - liste blanche de types de fichiers (JPEG, PNG, WebP, HEIC, PDF), vérifiée sur les premiers octets du fichier et pas seulement sur l'extension ;
  - 10 Mo maximum ;
  - servies en `Content-Disposition: attachment`, sauf les images ;
  - jamais exécutées.
- **API publique** : jetons personnels hachés en SHA-256, avec des droits (`read` ou `write`), une date d'expiration et une révocation possible. Les webhooks sont signés en HMAC-SHA256 (`X-HomeBudget-Signature`).
- **Secrets** (clés VAPID, SMTP) : uniquement en variables d'environnement. Les clés de protection des données ASP.NET sont stockées dans `/data/keys`.

### 6.6 Sauvegardes (O3, H6)

- **Tâche `BackupWorker`**, chaque nuit :
  1. un instantané cohérent de la base avec `VACUUM INTO '/data/backups/homebudget-AAAAMMJJ.db'` ;
  2. une rotation selon `BACKUP_KEEP_DAILY`, `_WEEKLY` et `_MONTHLY`.
- **Pièces jointes** : elles ne sont jamais modifiées (nommées par empreinte), donc le dossier `attachments/` peut être copié tel quel.
- **Recommandation** : synchroniser `/data/backups` et `/data/attachments` vers un NAS ou un stockage hors de la machine (en dehors de l'appli).
- **Export et restauration JSON** depuis l'interface (administrateurs), avec un audit et une confirmation.

### 6.7 Notifications (M1, M2)

- La tâche `AlertWorker` s'exécute toutes les heures. Elle vérifie les seuils de budget, le passage sous le seuil prévu, les échéances du lendemain et les opérations à valider.
- Une même alerte n'est pas envoyée deux fois : chaque alerte est enregistrée par (type, objet, période).
- Les envois se font en Web Push (VAPID) et/ou par e-mail (MailKit). Le résumé par e-mail part chaque semaine ou chaque mois, selon les préférences de chaque utilisateur.

## 7. Conventions d'API

- Préfixe `/api`, noms en anglais, JSON en `camelCase`, dates au format ISO `YYYY-MM-DD`, montants en nombres décimaux à 2 décimales.
- Pagination par curseur pour les opérations (`?cursor=&limit=`).
- Erreurs au format `application/problem+json` (RFC 9457).
- Concurrence optimiste : l'en-tête `If-Match: <version>` est exigé pour un `PUT` ou un `DELETE`, et l'API répond `412` en cas de conflit.
- Endpoints prévus pour le MVP : `/accounts`, `/categories`, `/payees`, `/transactions`, `/transfers`, `/recurrences`, `/recurrences/pending`, `/budgets/{period}`, `/dashboard/{period}`, `/forecast`, `/exports`, `/backups`, `/auth/*`, `/settings`.

## 8. Déploiement

### 8.1 Image

Le `Dockerfile` est en plusieurs étapes :
1. `node:22-alpine` compile la PWA ;
2. `dotnet/sdk:10.0` publie l'API, en y copiant la PWA dans `wwwroot` ;
3. `dotnet/aspnet:10.0` exécute l'appli sous l'utilisateur `app`, avec un `HEALTHCHECK` sur `/health/live`.

### 8.2 `docker-compose.yml` (stack Portainer construite depuis Git)

```yaml
services:
  home-budget:
    build: .
    container_name: home-budget
    restart: unless-stopped
    environment:
      TZ: Europe/Paris
      HB__PublicUrl: https://budget.example.fr
      HB__KnownProxyNetwork: 172.20.0.0/16
      HB__Backup__KeepDaily: 7
      HB__Smtp__Host: ""
      HB__Push__VapidPublicKey: ""
      HB__Push__VapidPrivateKey: ""
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
| `HB__Smtp__Host` / `__Port` / `__User` / `__Password` / `__From` | Envoi d'e-mails | désactivé |
| `HB__Push__VapidPublicKey` / `__VapidPrivateKey` / `__Subject` | Notifications push | désactivé |
| `HB__Metrics__AllowedNetwork` | Réseau autorisé à interroger `/metrics` | réseau Docker |

## 9. CI/CD (GitHub Actions)

| Workflow | Déclencheur | Étapes |
|---|---|---|
| `ci.yml` | push, pull request | `dotnet build` / `dotnet test` (avec couverture), `npm ci` / lint / `vitest` / build, build de l'image Docker, Playwright contre le conteneur |
| `security.yml` | push, pull request, chaque semaine | CodeQL (C# et JS/TS), Trivy (fichiers et image, échec sur les failles HIGH/CRITICAL), `dependency-review` |
| `dependabot.yml` | chaque semaine | NuGet, npm, GitHub Actions, Docker |

Pas de publication de l'image : Portainer reconstruit la stack depuis la branche `main`, manuellement ou via le webhook GitOps de Portainer.

## 10. Décisions écartées

| Option | Raison |
|---|---|
| Blazor WebAssembly | Lourd à charger sur mobile, hors ligne et graphiques plus pénibles à réaliser |
| PostgreSQL | Un service de plus à exploiter pour un usage domestique ; le passage reste possible plus tard (changement de pilote EF Core) |
| Node.js | Hors de la stack maîtrisée par le mainteneur |
| Clean Architecture multi-projets, MediatR | Complexité sans bénéfice à cette échelle |
| Hangfire ou Quartz | `BackgroundService` + `PeriodicTimer` suffisent |
| Synchronisation complète hors ligne (local-first à la Actual) | Coût très élevé ; la file d'attente de saisie couvre le besoin |
