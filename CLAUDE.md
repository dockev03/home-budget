# Home Budget

Application web auto-hébergée de gestion du budget du foyer (PWA mobile et PC).

- Spécifications : [docs/CAHIER_DES_CHARGES.md](docs/CAHIER_DES_CHARGES.md)
- Architecture : [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Stack : ASP.NET Core 10 (Minimal APIs, EF Core + SQLite, Identity) · Vue 3 + TypeScript + Vuetify + ECharts (PWA) · Docker
- Conventions : code et noms en anglais ; interface, documentation et messages de commit en français.

## Méthode

* Étape N = phase N, après validation de la précédente. ≤ 6 fonctionnalités, regroupées par code touché.
* Avant de coder : plan court, attendre l'accord. Fin : build + tests + `dotnet format` verts, résumé fait / reste. Pas de refactor hors étape.
* Licence ou API non conforme à la spec : signaler, ne pas contourner. Aucun paquet sans accord.
* Changement à risque (droits, exposition réseau, suppression de données) : l'annoncer en une ligne en tête de réponse, attendre l'accord.

## Git

* Branche `feature/etape-N-sujet`, `feature/`, `fix/` ou `docs/sujet`, jamais réutilisée ; jamais de commit sur `main`/`master`/`staging`.
* Aucun commit ni push sans accord explicite ; présenter le message (Conventional Commits en français) avant.
* Aucun identifiant de modèle d'IA dans commits, PR ou fichiers (pas de `Co-Authored-By` d'IA).

## Versions (tags → image GHCR)

* SemVer, tag `vX.Y.Z` annoté, posé sur `main` après fusion et CI verte ; le push du tag publie l'image (`release.yml`).
* Étape N fusionnée → `v0.N.0`. Correctif (`fix/`) fusionné touchant l'appli → patch (`v0.N.1`). Docs ou CI seules → pas de tag.
* `v1.0.0` quand tous les lots (L0 à L6) sont terminés et validés ; jusque-là, étape N → `v0.N.0` (`v0.10.0` après `v0.9.0`). Ensuite étape = mineure, rupture (migration irréversible, variable supprimée ou renommée) = majeure.
* Pré-version pour essai sur le serveur : `vX.Y.Z-rc.N` (pas de `latest`).
* En fin d'étape ou de correctif fusionné, proposer le tag et la commande ; ne jamais créer ni pousser un tag sans accord explicite.
