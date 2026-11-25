# Déploiement — Portainer derrière Nginx Proxy Manager

## Prérequis

- Docker et Portainer.
- Nginx Proxy Manager (NPM) déjà en place, avec un réseau Docker partagé.
- Un nom de domaine pointant vers le NPM. Le HTTPS est **obligatoire** : cookies `Secure`, PWA, notifications push.
- Un accès au registre GHCR : l'image `ghcr.io/dockev03/home-budget` est publiée par le workflow `release.yml` à chaque tag de version, et le paquet est privé comme le dépôt. Créer un jeton GitHub (classic) avec le seul droit `read:packages`.

## 1. Créer la stack

L'image n'est plus construite sur le serveur : elle est tirée de GHCR, à la version choisie.

Dans Portainer :

1. **Registries → Add registry → Custom registry** : URL `ghcr.io`, utilisateur GitHub et le jeton `read:packages`.
2. **Stacks → Add stack → Repository** : ce dépôt Git, branche `main`, fichier `docker-compose.yml`. Le mode **Web editor** fonctionne aussi, en y collant le contenu du fichier.
3. Variables d'environnement :

| Variable | Exemple | Rôle |
|---|---|---|
| `HB_VERSION` | `0.6.0` | Version de l'image (défaut `latest`). Épingler une version précise : la mise à jour reste un choix explicite |
| `HB_PUBLIC_URL` | `https://budget.mondomaine.fr` | URL publique |
| `HB_KNOWN_PROXY_NETWORK` | `172.20.0.0/16` | Sous-réseau du réseau du NPM : `docker network inspect <réseau>` → `IPAM.Config.Subnet` |
| `NPM_NETWORK` | `npm_default` | Nom du réseau Docker du NPM |

Le conteneur ne publie **aucun port**. Il est joignable uniquement par le NPM, via le réseau Docker. Il tourne avec un système de fichiers en lecture seule, sans aucune capacité Linux, sous l'utilisateur non root `app` (UID 1654).

## 2. Configurer le NPM

**Proxy Hosts → Add** :

- Domaine : `budget.mondomaine.fr` ;
- schéma `http`, hôte `home-budget`, port `8080` ;
- SSL : certificat Let's Encrypt, **Force SSL**, **HSTS** activé, HTTP/2.

## Variante : NPM sur un autre serveur

Quand le NPM n'est pas sur la même machine, utiliser `docker-compose.proxy-distant.yml` (champ **Compose path** de la stack). Le conteneur publie alors un port, **uniquement sur une adresse privée**.

Prérequis : un réseau privé entre le serveur NPM et le serveur de la stack (WireGuard, Tailscale ou réseau privé de l'hébergeur). Le lien NPM → application est en HTTP : sur Internet, mots de passe et cookies de session circuleraient en clair.

| Variable | Exemple | Rôle |
|---|---|---|
| `HB_VERSION` | `0.6.0` | Version de l'image (défaut `latest`) |
| `HB_PUBLIC_URL` | `https://budget.mondomaine.fr` | URL publique |
| `HB_BIND_ADDRESS` | `10.0.0.2` | IP privée du serveur de la stack, sur laquelle le port est publié (obligatoire) |
| `HB_PORT` | `8080` | Port publié (défaut `8080`) |
| `HB_KNOWN_PROXY_NETWORK` | `10.0.0.1/32` | IP privée du serveur NPM (obligatoire) |

- Ne jamais mettre `0.0.0.0` dans `HB_BIND_ADDRESS` : Docker contourne les règles UFW, le port serait ouvert à tout Internet.
- Dans le NPM, le Proxy Host vise `http://<HB_BIND_ADDRESS>:<HB_PORT>`, avec les mêmes réglages SSL que ci-dessus.
- Si les logs montrent une adresse cliente en `172.x.x.x` au lieu de l'IP du NPM (proxy « userland » de Docker), ajouter aussi le sous-réseau du réseau de la stack à `HB_KNOWN_PROXY_NETWORK`, séparé par une virgule.

## Sans Portainer : Docker Compose

Sur le serveur, avec le fichier `docker-compose.yml` (ou `docker-compose.proxy-distant.yml`) et un fichier `.env` à côté :

```bash
echo "$JETON_READ_PACKAGES" | docker login ghcr.io -u <utilisateur-github> --password-stdin
```

```bash
cat > .env <<'ENV'
HB_VERSION=0.6.0
HB_PUBLIC_URL=https://budget.mondomaine.fr
HB_KNOWN_PROXY_NETWORK=172.20.0.0/16
NPM_NETWORK=npm_default
ENV
```

```bash
docker compose up -d
```

## Mettre à jour

1. Publier la nouvelle version (tag `vX.Y.Z` sur `main`, voir le [README](../README.md)).
2. Changer `HB_VERSION` :
   - Portainer : modifier la variable de la stack, puis **Update the stack** avec **Re-pull image** ;
   - Docker Compose : modifier `.env`, puis `docker compose pull && docker compose up -d`.

Les migrations de base sont appliquées au démarrage. Pour revenir en arrière, remettre l'ancienne version dans `HB_VERSION` ; restaurer d'abord une sauvegarde si la nouvelle version a migré la base.

## 3. Premier lancement

1. Dans Portainer, ouvrir le conteneur `home-budget`, puis **Logs**. Repérer la ligne :
   `Premier lancement : aucun utilisateur. Jeton de création de l'administrateur : XXXXXXXX`
2. Ouvrir `https://budget.mondomaine.fr` : l'écran « Premier lancement » demande ce jeton.
3. Créer le compte administrateur, puis activer la double authentification (page **Sécurité**).

Le jeton change à chaque redémarrage tant qu'aucun utilisateur n'existe. Il est désactivé définitivement dès que l'administrateur est créé.

## Variables d'environnement de l'application

| Variable | Défaut | Rôle |
|---|---|---|
| `HB__DataPath` | `/data` | Base SQLite (`homebudget.db`) et clés de chiffrement des cookies (`keys/`) |
| `HB__PublicUrl` | — | URL publique |
| `HB__KnownProxyNetwork` | — | Réseaux (CIDR, séparés par des virgules) autorisés à transmettre les en-têtes `X-Forwarded-For` et `X-Forwarded-Proto` |
| `HB__Metrics__AllowedNetwork` | — (boucle locale uniquement) | Réseaux autorisés à interroger `/metrics` (Prometheus). Une requête passée par le proxy est toujours refusée |
| `HB__Security__AuthRequestsPerMinute` | `10` | Requêtes d'authentification autorisées par minute et par adresse IP |
| `HB__Backup__Enabled` | `true` | Sauvegarde automatique quotidienne de la base |
| `HB__Backup__Hour` | `3` | Heure locale de la sauvegarde (fuseau `TZ` du conteneur) |
| `HB__Backup__KeepDaily` / `__KeepWeekly` / `__KeepMonthly` | `7` / `4` / `12` | Rotation : derniers jours, dernière sauvegarde de chaque semaine, de chaque mois |
| `HB__Recurrences__Enabled` | `true` | Traitement automatique des échéances de récurrence (au démarrage, puis chaque jour) |
| `HB__Recurrences__Hour` | `0` | Heure locale du traitement quotidien des échéances (fuseau `TZ` du conteneur) |
| `HB__SetupToken` | — | Impose le jeton de premier lancement (automatisation et tests uniquement) |
| `HB__Security__AllowInsecureHttp` | `false` | Autorise les cookies en HTTP non chiffré. **Développement et tests uniquement, jamais en production** |

## Supervision

- `GET /health/live` : le processus répond. Utilisé par le `HEALTHCHECK` Docker.
- `GET /health/ready` : la base de données est accessible.
- `GET /metrics` : métriques au format Prometheus. À interroger depuis le réseau Docker, sans passer par le NPM, en renseignant `HB__Metrics__AllowedNetwork`.

## Données et sauvegardes

Toutes les données sont dans le volume `home-budget-data`, monté sur `/data` :

- `homebudget.db` : la base, en mode WAL ;
- `keys/` : les clés de chiffrement des cookies. Si elles sont perdues, il faut se reconnecter, mais aucune donnée n'est perdue ;
- `backups/` : les sauvegardes automatiques `homebudget-AAAAMMJJ.db`, et les instantanés `avant-restauration-AAAAMMJJ-HHmmss.db` pris avant chaque restauration JSON.

### Sauvegardes automatiques

Chaque nuit (3 h par défaut), l'application écrit un instantané cohérent de la base (`VACUUM INTO`, sans interruption du service) dans `/data/backups`, puis applique la rotation : 7 quotidiennes, 4 hebdomadaires et 12 mensuelles par défaut. Au démarrage, la sauvegarde du jour est faite si elle manque.

Ces copies restent sur le même volume que la base : **elles ne protègent pas d'une panne du disque**. Synchroniser `/data/backups` vers un NAS ou un autre stockage, depuis l'hôte.

### Restaurer une sauvegarde

1. Arrêter la stack dans Portainer.
2. Dans le volume, remplacer `homebudget.db` par la sauvegarde choisie (renommée `homebudget.db`) et supprimer `homebudget.db-wal` et `homebudget.db-shm` s'ils existent.
3. Redémarrer la stack.

### Sauvegarde et restauration JSON (depuis l'application)

Dans **Paramètres**, un administrateur peut télécharger une sauvegarde JSON des données du foyer (paramètres, comptes, catégories, tiers, budgets, opérations, récurrences et échéances), et la restaurer plus tard, éventuellement sur une autre instance.

- Le fichier ne contient ni les comptes utilisateurs, ni les mots de passe, ni les secrets de double authentification. Il contient en revanche toutes les données financières : le conserver en lieu sûr.
- La restauration **remplace toutes les données du foyer**. Le fichier (64 Mio au plus) est validé en entier avant de toucher à la base, puis tout est restauré dans une seule transaction. Les utilisateurs existants sont conservés ; une référence à un utilisateur inconnu (auteur, propriétaire) est vidée.
- Juste avant, un instantané de la base est écrit dans `/data/backups/avant-restauration-AAAAMMJJ-HHmmss.db`. Ces instantanés échappent à la rotation : les supprimer à la main quand ils ne servent plus. Pour annuler une restauration, restaurer cet instantané comme ci-dessus.
- Exports et restaurations sont tracés dans les logs du conteneur ; le journal d'audit arrivera avec le lot L3.
