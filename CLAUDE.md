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
