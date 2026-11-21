# Déploiement — Portainer derrière Nginx Proxy Manager

## Prérequis

- Docker et Portainer.
- Nginx Proxy Manager (NPM) déjà en place, avec un réseau Docker partagé.
- Un nom de domaine pointant vers le NPM. Le HTTPS est **obligatoire** : cookies `Secure`, PWA, notifications push.

## 1. Créer la stack

Dans Portainer : **Stacks → Add stack → Repository**.

- Dépôt : ce dépôt Git, branche `main`, fichier `docker-compose.yml`.
- Variables d'environnement :

| Variable | Exemple | Rôle |
|---|---|---|
| `HB_PUBLIC_URL` | `https://budget.mondomaine.fr` | URL publique |
| `HB_KNOWN_PROXY_NETWORK` | `172.20.0.0/16` | Sous-réseau du réseau du NPM : `docker network inspect <réseau>` → `IPAM.Config.Subnet` |
| `NPM_NETWORK` | `npm_default` | Nom du réseau Docker du NPM |

Le conteneur ne publie **aucun port**. Il est joignable uniquement par le NPM, via le réseau Docker. Il tourne avec un système de fichiers en lecture seule, sans aucune capacité Linux, sous l'utilisateur non root `app` (UID 1654).

## 2. Configurer le NPM

**Proxy Hosts → Add** :

- Domaine : `budget.mondomaine.fr` ;
- schéma `http`, hôte `home-budget`, port `8080` ;
- SSL : certificat Let's Encrypt, **Force SSL**, **HSTS** activé, HTTP/2.

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
| `HB__SetupToken` | — | Impose le jeton de premier lancement (automatisation et tests uniquement) |
| `HB__Security__AllowInsecureHttp` | `false` | Autorise les cookies en HTTP non chiffré. **Développement et tests uniquement, jamais en production** |

## Supervision

- `GET /health/live` : le processus répond. Utilisé par le `HEALTHCHECK` Docker.
- `GET /health/ready` : la base de données est accessible.
- `GET /metrics` : métriques au format Prometheus. À interroger depuis le réseau Docker, sans passer par le NPM, en renseignant `HB__Metrics__AllowedNetwork`.

## Données et sauvegardes

Toutes les données sont dans le volume `home-budget-data`, monté sur `/data` :

- `homebudget.db` : la base, en mode WAL ;
- `keys/` : les clés de chiffrement des cookies. Si elles sont perdues, il faut se reconnecter, mais aucune donnée n'est perdue.

Les sauvegardes automatiques avec rotation arriveront à l'étape 1. En attendant, sauvegarder le volume au niveau de l'hôte.
