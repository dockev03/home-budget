# Cahier des charges — Home Budget

> Statut : **brouillon à valider** · Version 0.1 · Novembre 2026

## 1. Contexte et objectifs

Le budget du foyer est aujourd'hui suivi dans un classeur Excel mensuel :

- une feuille **GLOBAL** : budget comparé au réel pour les revenus, les dépenses fixes, les dépenses variables (dont certaines exprimées en % des salaires) et l'épargne, avec le calcul d'un « Restant » ;
- une feuille **Dépenses** : saisie manuelle des opérations (date, libellé, montant signé, catégorie, pointage).

**Objectif :** une application web auto-hébergée, utilisable sur PC et smartphone, qui remplace ce classeur. Elle doit en plus :

- réduire la saisie manuelle (import des relevés, catégories apprises) ;
- programmer les récurrences et les échéances ;
- **prévoir le solde de fin de mois** et au-delà ;
- offrir des graphiques et des rapports ;
- être partagée par les membres du foyer.

L'application combine l'esprit d'**Actual Budget** (budget simple, mobile, enveloppes en option) et celui de **Firefly III** (multi-comptes, virements, API, rigueur des données).

## 2. Périmètre

### 2.1 Utilisateurs

| Rôle | Droits |
|---|---|
| Administrateur | Tout, y compris la gestion des utilisateurs, des paramètres et des sauvegardes |
| Membre | Lecture et écriture sur les comptes partagés et sur ses comptes privés |
| Lecture seule | Consultation seulement |

Une instance correspond à **un foyer**. Il n'y a pas de gestion de plusieurs foyers.

### 2.2 Hors périmètre

| # | Élément | Décision |
|---|---|---|
| H4 | Import du classeur Excel existant | Non : on repart de zéro |
| H7 | Synchronisation bancaire automatique (DSP2) | Plus tard |
| I1 | Éditeur de règles de catégorisation | Non : remplacé par des **règles apprises** (voir I2 à I4) |
| M3 | Notifications ntfy, Gotify ou Telegram | Non |
| N3 | Connexion via un fournisseur d'identité OIDC | Non |
| N7 | Chiffrement de la base au repos | Non |
| O6 | Publication de l'image sur GHCR | Non : Portainer construit l'image depuis le dépôt Git |
| P5 | Lecture automatique des fiches de paie PDF | Non : le PDF est seulement **attaché** à l'opération de salaire (voir B7) |

## 3. Exigences fonctionnelles

Légende des lots (détail en §6) : **L1** = MVP cœur · **L2** = import et automatisation · **L3** = foyer · **L4** = prévision avancée · **L5** = confort · **L6** = bonus.

### A. Comptes

| # | Exigence | Lot |
|---|---|---|
| A1 | Plusieurs comptes, chacun avec un solde initial à une date | L1 |
| A2 | Types de comptes : courant, épargne, investissement, espèces, crédit | L1 |
| A3 | Solde par compte et solde global (patrimoine net) | L1 |
| A4 | Virement entre deux comptes : une seule saisie, deux écritures liées, **exclu des revenus et des dépenses** | L1 |
| A5 | Rapprochement bancaire : saisie du solde du relevé à une date, calcul de l'écart, verrouillage des opérations rapprochées | L2 |
| A6 | Archivage d'un compte (masqué, historique conservé) | L1 |
| A7 | Valorisation manuelle des comptes d'investissement (historique des valeurs saisies) | L4 |
| A8 | Comptes de crédit : capital, taux, durée, assurance → tableau d'amortissement et capital restant dû | L5 |

### B. Opérations

| # | Exigence | Lot |
|---|---|---|
| B1 | Saisie rapide : date, montant, tiers, catégorie, compte. Valeurs par défaut intelligentes (dernier compte utilisé, catégorie suggérée) | L1 |
| B2 | Pointage d'une opération, avec un filtre « non pointées » | L1 |
| B3 | Recherche et filtres : période, compte, catégorie, tag, tiers, montant min/max, texte, statut | L1 |
| B4 | Opérations à date future, prises en compte dans la prévision | L1 |
| B5 | Ventilation d'une opération sur plusieurs catégories | L1 |
| B6 | Note libre | L1 |
| B7 | Pièces jointes (photo, PDF), y compris les fiches de paie attachées à l'opération de salaire | L5 |
| B8 | Tiers avec autocomplétion | L1 |
| B9 | Dupliquer une opération | L1 |
| B10 | Modification en masse (catégorie, tags, compte, pointage) | L2 |
| B11 | Lier un remboursement à une dépense, pour afficher le coût net | L2 |
| B12 | Avances à recevoir (notes de frais, avance pro, CSE) : montant attendu, statut ouvert ou soldé | L2 |
| B13 | Historique des modifications d'une opération (qui, quand, avant/après) | L3 |

### C. Catégories et tags

| # | Exigence | Lot |
|---|---|---|
| C1 | Chaque catégorie a un type : revenu, dépense fixe, dépense variable ou épargne | L1 |
| C2 | Catégories parentes et sous-catégories (2 niveaux) | L1 |
| C3 | Couleur et icône | L1 |
| C4 | Tags libres, plusieurs par ligne d'opération | L2 |
| C5 | Fusionner deux catégories (les opérations, budgets et récurrences sont déplacés) | L2 |
| C6 | Jeu de catégories créé au premier lancement d'après le classeur actuel | L1 |

### D. Budget

| # | Exigence | Lot |
|---|---|---|
| D1 | Budget par catégorie et par période, comparé au réel, avec le reste | L1 |
| D2 | Trois modes de budget : **montant**, **% des revenus budgétés**, **automatique** (= somme des récurrences de la période) | L1 |
| D3 | Le mode automatique s'applique par défaut aux dépenses fixes | L1 |
| D4 | Budget différent pour une période donnée | L4 |
| D5 | Copier le budget de la période précédente ou d'une moyenne sur N mois | L4 |
| D6 | Report du reste sur la période suivante (par catégorie, activable) | L6 |
| D7 | Mode « enveloppes » complet en option (« à affecter » = revenus − montants affectés) | L6 |
| D8 | Récapitulatif : Restant = Revenus − Fixes − Variables − Épargne (budget et réel) | L1 |
| D9 | Jauges avec seuils d'alerte à 80 % et 100 % (seuils paramétrables) | L1 |
| D10 | Période budgétaire configurable (voir R1) | L1 |
| D11 | Provisions : une dépense annuelle ou ponctuelle lissée par mois | L4 |

### E. Récurrences et échéancier

| # | Exigence | Lot |
|---|---|---|
| E1 | Fréquences : tous les N jours, semaines, mois ou années (le trimestriel = tous les 3 mois) | L1 |
| E2 | Paiement en N fois, avec un libellé numéroté automatiquement (« Montre 1/4 ») | L1 |
| E3 | Fin : jamais, à une date, ou après N échéances | L1 |
| E4 | Mode **automatique** : l'opération est créée (non pointée) le jour de l'échéance | L1 |
| E5 | Mode **à valider** : l'échéance apparaît dans « À valider » ; on peut la confirmer (avec ajustement du montant), la sauter ou la reporter | L1 |
| E6 | Rapprochement : une opération importée correspondant à une échéance (même compte, montant dans la tolérance, date ± N jours) la remplace | L2 |
| E7 | Montant estimé avec une tolérance (en % ou en €) | L2 |
| E8 | Calendrier des échéances de la période | L5 |
| E9 | Report au jour ouvré (suivant ou précédent) si l'échéance tombe un week-end ou un jour férié français | L4 |
| E10 | Vue « Abonnements » : total mensuel et annuel des récurrences marquées comme abonnement | L2 |

### F. Prévision

| # | Exigence | Lot |
|---|---|---|
| F1 | Solde prévu en fin de période (formule en R3) | L1 |
| F2 | Courbe du solde jour par jour : réel en trait plein, prévu en pointillés | L1 |
| F3 | Alerte « passage sous le seuil prévu le JJ/MM » (seuil par compte, 0 € par défaut) | L1 |
| F4 | Prévision sur 3, 6 ou 12 mois | L4 |
| F5 | Scénarios « et si… » : événements fictifs, nommés et activables, jamais mélangés aux données réelles | L4 |
| F6 | Reste à vivre sur la période et par jour (formule en R4) | L1 |
| F7 | Stratégie de prévision au choix : par le budget, ou par la moyenne des N derniers mois | L4 |

### G. Tableau de bord et graphiques

| # | Exigence | Lot |
|---|---|---|
| G1 | Chiffres clés : revenus, dépenses, solde actuel, solde prévu, reste à vivre | L1 |
| G2 | Répartition des dépenses par catégorie (anneau) | L1 |
| G3 | Budget comparé au réel par catégorie | L1 |
| G4 | Revenus, dépenses et épargne sur 12 mois | L1 |
| G5 | Évolution du patrimoine net | L4 |
| G6 | Comparaison avec la période précédente et avec la même période un an avant | L4 |
| G7 | Top des tiers | L2 |
| G8 | Rapports personnalisés (filtres, regroupement par catégorie, tiers, tag ou mois, export) | L5 |
| G9 | Diagramme de flux (Sankey) : revenus → types → catégories | L5 |
| G10 | Thème clair, sombre ou automatique | L1 |

### H. Import, export et sauvegarde

| # | Exigence | Lot |
|---|---|---|
| H1 | Import CSV avec correspondance des colonnes, enregistrée en profil (ex. « La Banque Postale ») | L2 |
| H2 | Import OFX et QIF | L2 |
| H3 | Détection des doublons (empreinte date + montant + libellé brut, en tenant compte des opérations déjà saisies) avec écran de revue avant validation | L2 |
| H5 | Export CSV et Excel des opérations filtrées | L1 |
| H6 | Sauvegarde et restauration complète au format JSON (plus une archive des pièces jointes) | L1 |

### I. Automatisation (règles apprises)

| # | Exigence | Lot |
|---|---|---|
| I2 | Suggestion de catégorie et de tiers à partir de l'historique, apprise à chaque correction | L2 |
| I3 | Renommage automatique des libellés bancaires (« PRLV SEPA MACIF… » → tiers « Macif »), appris des corrections | L2 |
| I4 | Après une correction, proposer d'appliquer la même correction aux opérations passées similaires (avec aperçu et confirmation) | L2 |
| I5 | API REST documentée (OpenAPI), jetons d'accès personnels, webhooks signés | L5 |

### J. Épargne et objectifs

| # | Exigence | Lot |
|---|---|---|
| J1 | Objectifs d'épargne : montant cible, date cible, compte ou catégorie rattaché, jauge | L4 |
| J2 | Versement mensuel conseillé pour atteindre l'objectif | L4 |
| J3 | Indicateur d'épargne de précaution : nombre de mois de dépenses couverts | L4 |
| J4 | Suivi de l'épargne de Maël (objectif ou compte dédié) | L4 |

### K. Foyer et utilisateurs

| # | Exigence | Lot |
|---|---|---|
| K1 | Plusieurs utilisateurs dans le même foyer | L3 |
| K2 | Auteur et date de création et de modification de chaque opération | L1 (données) / L3 (affichage) |
| K3 | Comptes privés (visibles seulement par leur propriétaire) et comptes partagés | L3 |
| K4 | Répartition des dépenses communes (à parts égales, au prorata des revenus ou selon des parts fixes) avec le solde entre membres | L5 |
| K5 | Rôles : administrateur, membre, lecture seule | L3 |

### L. Mobile et expérience

| # | Exigence | Lot |
|---|---|---|
| L1 | Interface pensée d'abord pour le mobile, en responsive | L1 |
| L2 | Application installable sur le téléphone (PWA) | L1 |
| L3 | Bouton flottant « + » pour saisir une opération en moins de 3 secondes | L1 |
| L4 | Saisie hors ligne (file d'attente rejouée au retour du réseau) et consultation du cache en lecture seule | L5 |
| L5 | Raccourcis : refaire une opération récente ; les tiers fréquents sont proposés en premier | L2 |
| L6 | Navigation entre périodes par flèches et par glissement du doigt | L1 |
| L7 | Interface en français ; montants au format `1 234,56 €` ; dates au format JJ/MM/AAAA | L1 |

### M. Notifications

| # | Exigence | Lot |
|---|---|---|
| M1 | Notifications push (Web Push) : budget dépassé (80 / 100 %), passage sous le seuil prévu, échéance demain, opérations à valider. Choix par utilisateur et par type | L5 |
| M2 | Résumé par e-mail, hebdomadaire ou mensuel (SMTP générique : Gmail ou serveur tiers) | L5 |

### N. Sécurité

| # | Exigence | Lot |
|---|---|---|
| N1 | Connexion par identifiant et mot de passe (12 caractères minimum, mot de passe haché) | L1 |
| N2 | Double authentification TOTP, avec codes de secours | L1 |
| N4 | Verrouillage progressif après plusieurs échecs, et limitation du débit des requêtes de connexion | L1 |
| N5 | CSP stricte, cookies `HttpOnly`/`Secure`/`SameSite=Strict`, protection anti-CSRF, en-têtes de sécurité | L1 |
| N6 | Journal d'audit : connexions, échecs, changements de droits, exports, restaurations, suppressions | L3 |

### O. Exploitation

| # | Exigence | Lot |
|---|---|---|
| O1 | Image Docker et `docker-compose.yml` compatibles avec une stack Portainer construite depuis Git | L0 |
| O2 | Configuration par variables d'environnement | L0 |
| O3 | Sauvegarde automatique quotidienne de la base, avec rotation paramétrable (par défaut 7 quotidiennes, 4 hebdomadaires, 12 mensuelles) | L1 |
| O4 | CI GitHub Actions : build, tests, lint | L0 |
| O5 | Scans de sécurité : CodeQL, Trivy (fichiers et image), Dependabot, revue des dépendances | L0 |
| O7 | `/health` (vivacité et disponibilité) et `/metrics` (format Prometheus) | L0 |
| O8 | Tests unitaires (moteur de prévision ≥ 90 % de couverture), tests d'intégration de l'API, tests de bout en bout Playwright sur les parcours clés | L0+ |
| O9 | Migrations de base versionnées, appliquées au démarrage | L0 |

### P. Bonus

| # | Exigence | Lot |
|---|---|---|
| P1 | Suivi CAF et CMG : montant attendu (récurrence à valider) comparé au reçu, avec l'écart | L6 |
| P2 | Récapitulatif annuel des frais de garde, pour la déclaration d'impôts | L6 |
| P3 | Récapitulatif annuel par catégorie, comparé à l'année précédente | L4 |
| P4 | Santé : dépense → remboursement CPAM → remboursement mutuelle → reste à charge (s'appuie sur B11) | L6 |

## 4. Règles de gestion

**R1 — Période budgétaire.** C'est un paramètre du foyer :
- **calendaire** : du 1er au dernier jour du mois (par défaut, et **choix retenu par le foyer**) ;
- **jour fixe N** : du N du mois jusqu'à la veille du N suivant (par exemple du 25 au 24).

La période porte le nom du mois où elle se termine majoritairement. Tous les calculs (budget, prévision, rapports) passent par cette notion de période, jamais par le mois calendaire en dur.

**R2 — Montants.**
- Euro uniquement, **2 décimales**.
- Un montant est signé : négatif pour une sortie, positif pour une entrée.
- Le réalisé d'une catégorie de dépense correspond à −Σ(montants) : un remboursement positif classé dans la même catégorie diminue donc la dépense (comme dans l'Excel actuel).

**R3 — Solde prévu en fin de période.** Il est calculé sur les comptes inclus dans la prévision (par défaut, les comptes courants) :

```
Solde prévu = Solde réel au jour J
            + opérations déjà saisies datées après J
            + échéances de récurrence restantes (non encore générées)
            + Σ par catégorie de max(0, budget restant − échéances restantes de la catégorie)
              (avec un signe négatif pour les dépenses ; les revenus sont inclus par défaut
               et peuvent être exclus via une option)
```

Le reste de budget non couvert par une échéance est étalé uniformément sur les jours restants, pour tracer la courbe (F2).

**R4 — Reste à vivre.**

```
Reste à vivre = Solde réel au jour J
              + revenus attendus restants
              − dépenses fixes restantes
              − épargne prévue restante
              − seuil de sécurité (0 € par défaut)
```

Le reste à vivre par jour est ce montant divisé par le nombre de jours restants dans la période.

**R5 — Virements.** Un virement entre deux comptes du foyer est exclu des revenus et des dépenses. Un virement vers un compte d'épargne compte comme de l'épargne s'il porte une catégorie de type épargne.

**R6 — Récurrences.**
- La k-ième échéance est calculée depuis la date de départ, pour éviter la dérive des fins de mois : le 31 donne ensuite 28/29, puis 31.
- Une échéance générée, sautée ou supprimée n'est jamais régénérée.
- Le report au jour ouvré est appliqué après ce calcul.

**R7 — Opérations pointées et rapprochées.**
- Une opération pointée reste modifiable, avec une confirmation.
- Une opération rapprochée (A5) est verrouillée ; il faut annuler le rapprochement pour la modifier.

**R8 — Visibilité.** Un utilisateur ne voit jamais les opérations d'un compte privé qui ne lui appartient pas, y compris dans les totaux, les exports, l'API et les notifications.

## 5. Exigences non fonctionnelles

| Domaine | Exigence |
|---|---|
| Performance | Tableau de bord en moins de 500 ms côté serveur avec 10 ans d'historique (~30 000 opérations) ; premier affichage mobile en 4G en moins de 2 s |
| Disponibilité | Usage domestique, sans haute disponibilité. Redémarrage automatique du conteneur |
| Sécurité | Recommandations OWASP ASVS niveau 1 ; HTTPS assuré par le Nginx Proxy Manager existant ; aucun secret dans le dépôt |
| Données | Une seule base SQLite et un dossier de pièces jointes dans un volume `/data` |
| Compatibilité | Dernières versions de Chrome, Firefox, Safari et Edge ; iOS 16.4+ pour le push |
| Accessibilité | Contrastes conformes WCAG AA, navigation au clavier sur PC |
| Maintenabilité | Code en anglais ; interface et documentation en français ; tests obligatoires en CI |

## 6. Découpage en lots

| Lot | Contenu | Critère de fin |
|---|---|---|
| **L0 — Socle** | Squelette back et front, authentification + 2FA, Docker, CI et scans, health/metrics, migrations | Appli vide, sécurisée, déployée dans Portainer derrière le NPM |
| **L1 — Cœur (MVP)** | Comptes, opérations, ventilation, virements, catégories, budget (3 modes), récurrences, prévision de la période, reste à vivre, tableau de bord, PWA, export, sauvegardes | **Le classeur Excel peut être abandonné** |
| **L2 — Import et automatisation** | CSV/OFX/QIF, doublons, règles apprises, rapprochement des échéances, remboursements, avances, modification en masse, tags, abonnements, rapprochement bancaire | Un mois complet traité par import, sans saisie manuelle |
| **L3 — Foyer** | Plusieurs utilisateurs, rôles, comptes privés, historique et journal d'audit | Les deux membres du foyer utilisent l'appli |
| **L4 — Prévision avancée** | Prévision sur plusieurs mois, scénarios, moyennes, budgets par période, provisions, objectifs d'épargne, patrimoine, comparaisons, valorisations | |
| **L5 — Confort** | Hors ligne, push, e-mail, pièces jointes, crédits, Sankey, rapports personnalisés, calendrier, API publique et webhooks, répartition des dépenses | |
| **L6 — Bonus** | Mode enveloppes et report du reste, CAF/CMG, frais de garde, santé | |

## 7. Décisions prises

| # | Sujet | Décision |
|---|---|---|
| Q1 | Période budgétaire (R1) | **Calendaire** (du 1er au dernier jour du mois). Le mode « jour fixe » (D10) reste développé, mais n'est pas activé |
| Q2 | SMTP (M2) | Configuration SMTP générique (hôte, port, SSL/STARTTLS, identifiants), compatible avec **Gmail** (mot de passe d'application) ou un serveur tiers |
| Q3 | Format CSV de La Banque Postale (H1) | Profil « La Banque Postale (CSV) » créé à l'installation (`Date;Libellé;Montant(EUROS)`, en-tête du relevé ignoré). L'**OFX** est recommandé : chaque opération porte un identifiant de la banque (FITID), ce qui écarte les doublons à coup sûr |
| Q4 | Seuil de sécurité du reste à vivre (R4) | **0 €** par défaut, paramétrable |
| Q5 | Revenus dans la prévision (R3) | **Inclus** par défaut, avec une option pour les désactiver |
