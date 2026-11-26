# Home Budget

Application web auto-hébergée de gestion du budget du foyer : opérations, budgets par catégorie, récurrences, prévision du solde de fin de mois et graphiques. Utilisable sur PC et smartphone (PWA).

## Documentation

- [Cahier des charges](docs/CAHIER_DES_CHARGES.md) : fonctionnalités, règles de gestion, découpage en lots
- [Architecture](docs/ARCHITECTURE.md) : stack, modèle de données, sécurité, déploiement

## Publier une version

Une fois l'étape fusionnée sur `main` et la CI verte, poser un tag annoté et le pousser. Numérotation : étape N → `v0.N.0`, correctif → `v0.N.1`, docs ou CI seules → pas de tag ; `v1.0.0` quand tous les lots sont terminés et validés (règles complètes dans le [CLAUDE.md](CLAUDE.md)).

```bash
git switch main && git pull
```

```bash
git tag -a v0.6.0 -m "Version 0.6.0 : règles apprises, tags et modification en masse (étape 6)"
```

```bash
git push origin v0.6.0
```

Le workflow `release.yml` vérifie que le tag est sur `main`, construit l'image, la scanne avec Trivy, puis la publie sur `ghcr.io/dockev03/home-budget` avec les tags `0.6.0`, `0.6`, `latest` et `sha-<court>`. 
## Déployer avec Docker

L'image est privée : se connecter une fois à GHCR avec un jeton GitHub (classic) limité à `read:packages`.

```bash
echo "$JETON_READ_PACKAGES" | docker login ghcr.io -u <utilisateur-github> --password-stdin
```

**Avec Docker Compose** (recommandé) : récupérer `docker-compose.yml`, créer un fichier `.env` à côté (`HB_VERSION`, `HB_PUBLIC_URL`, `HB_KNOWN_PROXY_NETWORK`, `NPM_NETWORK`), puis :

```bash
docker compose up -d
```

Le NPM sur un autre serveur ? Utiliser `docker-compose.proxy-distant.yml`. Avec Portainer, déclarer le registre `ghcr.io` puis créer la stack depuis ce dépôt.

**Avec `docker run`** (essai local sur `http://localhost:8080`, sans reverse proxy) :

```bash
docker run -d --name home-budget -p 127.0.0.1:8080:8080 \
  -v home-budget-data:/data \
  -e HB__Security__AllowInsecureHttp=true \
  --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true \
  ghcr.io/dockev03/home-budget:0.6.0
```

`HB__Security__AllowInsecureHttp=true` autorise la connexion en HTTP : **essai local uniquement**. En production, l'application exige le HTTPS (cookies `Secure`) et se place derrière un reverse proxy. Détails, variables, premier lancement et mises à jour : [DEPLOIEMENT.md](docs/DEPLOIEMENT.md).

## Stack prévue

ASP.NET Core 10 · EF Core + SQLite · Vue 3 + Vuetify + ECharts (PWA) · Docker (stack Portainer derrière Nginx Proxy Manager) · GitHub Actions (CodeQL, Trivy)
