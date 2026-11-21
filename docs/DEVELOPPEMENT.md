# Développement

## Prérequis

- SDK .NET 10 (`global.json`)
- Node.js 22

## Lancer en local

API, sur http://localhost:5080. Les données vont dans `src/HomeBudget.Api/data/`, et les cookies sont autorisés en HTTP :

```bash
dotnet run --project src/HomeBudget.Api --launch-profile http
```

PWA en mode développement, sur http://localhost:5173. Les requêtes `/api` sont relayées vers l'API :

```bash
cd src/HomeBudget.Web && npm install && npm run dev
```

Le jeton de premier lancement s'affiche dans la console de l'API.

## Vérifications (identiques à la CI)

Back : format, build, puis tests unitaires et d'intégration (y compris le contrat OpenAPI) :

```bash
dotnet format --verify-no-changes
```

```bash
dotnet build
```

```bash
dotnet test
```

Front : typage et build, lint, format, tests :

```bash
cd src/HomeBudget.Web && npm run build && npm run lint && npm run format:check && npm test
```

## Contrat d'API et client TypeScript

Le client du front est généré depuis `src/HomeBudget.Web/openapi.json`. Après une modification de l'API :

1. Régénérer `openapi.json` :

   ```bash
   UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests
   ```

2. Régénérer le client TypeScript :

   ```bash
   cd src/HomeBudget.Web && npm run gen:api
   ```

La CI échoue si l'un des deux fichiers n'est pas à jour.

## Migrations EF Core

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Nom> -p src/HomeBudget.Api -o Infrastructure/Data/Migrations
```

Les migrations sont appliquées automatiquement au démarrage.

## Tests de bout en bout (Playwright)

Contre une instance démarrée sur un dossier de données vide :

```bash
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080 HB__DataPath=/tmp/hb-e2e HB__SetupToken=e2e-setup-token HB__Security__AllowInsecureHttp=true HB__Security__AuthRequestsPerMinute=60 dotnet src/HomeBudget.Api/bin/Debug/net10.0/HomeBudget.Api.dll
```

```bash
cd tests/e2e && npm install && npx playwright test
```
