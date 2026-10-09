# Jeux de tests — import Excel

Fichiers `.xlsx` de démonstration pour l'assistant d'import DataMart.
Données **fictives** générées de façon déterministe (`tests/FixturesGenerator`,
`dotnet run --project tests/FixturesGenerator`) : relancer le générateur
reproduit ces fichiers à l'identique. Les IBAN sont fictifs mais plausibles
dans leur format (`FR.. .... .... ......... ..`).

Les en-têtes de colonnes sont en anglais car l'assistant rapproche
automatiquement les colonnes Excel des champs base de données
(`AccountNumber`, `ClientName`, …). Seules les **valeurs** sont françaises.

## Fichiers

| Fichier | Contenu | Résultat attendu |
|---|---|---|
| `comptes_clients_2000.xlsx` | 2 000 comptes clients, formats uniformes (dates ISO, décimales à point, `Oui`/`Non`) | 2 000 importés, 0 rejeté. Réglage format : booléen vrai = `Oui`, faux = `Non`. |
| `transactions_1000.xlsx` | 1 000 transactions (`H`/`F`, villes françaises, montants ±) | 1 000 importées, 0 rejetée. |
| `comptes_multiformats_150.xlsx` | 150 lignes mélangeant dates FR/US/ISO, décimales `1 234,56` / `1,234.56` / `$…`, booléens `Oui`/`Non`/`1`/`0` | Sert à configurer puis valider les options de format (étape 3). |
| `comptes_multi_feuilles.xlsx` | 2 feuilles : `Comptes actifs` (75) et `Comptes clôturés` (75) | Sert à tester le sélecteur de feuille (étape 2). |
| `lignes_rejetees_demo.xlsx` | 60 lignes : 40 saines + 5 sans n° de compte + 5 montant invalide + 5 date invalide + 5 booléen invalide | **40 importées, 20 rejetées** — vitrine du rapport d'import (étapes 4–5). |
| `transactions_avec_erreurs_200.xlsx` | 200 transactions avec dates EU/US mélangées et quelques montants `n/a` | Quelques lignes rejetées — le rapport affiche le chiffre exact à l'exécution. |
| `regles_age_60.xlsx` | 60 transactions : 50 majeures + 10 mineures (naissances 2012–2021) | Avec la règle démo R1 : **50 importées, 10 rejetées** (`CustomerDOB must be <= …`). |
| `regles_genre_60.xlsx` | 60 transactions : 50 `H`/`F` (dont un `f` minuscule → accepté) + 10 hors liste (`X`, `Male`, `?`, `M`, `Femme`, `Homme`) | Avec la règle démo R2 : **50 importées, 10 rejetées** (comparaison insensible à la casse). |
| `regles_montants_60.xlsx` | 60 transactions : 50 montants ≥ 0 (dont un `0.00` limite) + 10 négatifs | Avec la règle démo R3 : **50 importées, 10 rejetées**. |
| `regles_mixtes_120.xlsx` | 120 transactions : 90 saines + 10 mineures + 10 genres invalides + 10 montants négatifs | Avec R1+R2+R3 : **90 importées, 30 rejetées** — vitrine du cumul AND des règles. |

## Règles démo (page `Validation rules`, entité `CustomerTransactions`)

Créer ces 3 règles avant d'importer les fichiers `regles_*` :

| # | Champ | Opérateur | Valeur | Message suggéré |
|---|---|---|---|---|
| R1 | `CustomerDOB` | `LessThanOrEqual` | Fonction `TodayMinusYears`, argument `18` (ou bouton « Age 18+ preset ») | `Must be 18 or older` |
| R2 | `CustGender` | `In` | Valeurs `H, F` | — |
| R3 | `TransactionAmount` | `GreaterThanOrEqual` | Valeur `0` | — |

## Scénarios de démonstration suggérés

1. **Import nominal** : `comptes_clients_2000.xlsx` → entité `ClientAccounts` →
   mapping automatique → validation → import. Attendu : lot `Success`, 2 000 lignes.
2. **Rapport d'erreurs** : `lignes_rejetees_demo.xlsx` → validation.
   Attendu : 40 importées / 20 rejetées, chaque rejet motivé
   (champ obligatoire, décimal, date, booléen).
3. **Multi-feuilles** : `comptes_multi_feuilles.xlsx` → changer de feuille à
   l'étape 2 et constater que les colonnes détectées suivent la feuille.
4. **Règles de validation** : créer R1+R2+R3 (ci-dessus, entité
   `CustomerTransactions`), puis importer `regles_mixtes_120.xlsx` → mapper
   (rapprochement automatique) → étape 4 : les 3 règles s'affichent en
   lecture seule ; validation attendue **90 importées / 30 rejetées**, chaque
   rejet motivé par sa règle (`CustomerDOB must be <= …`,
   `CustGender must be one of [H, F]`, `TransactionAmount must be >= 0`).
