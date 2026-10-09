# Guide d'utilisation — import Excel (DataMart)

Ce guide explique comment importer un fichier Excel dans la base via
l'assistant d'import, puis comment suivre les imports dans le tableau de bord.

> Fichiers d'exemple : `docs/sample-data/` (voir son `README.md`).
> Parcours conseillé pour une première prise en main : `comptes_clients_2000.xlsx`.

## Prérequis

- Être connecté à l'application.
- Disposer d'un fichier **`.xlsx`** (seul format accepté, 100 Mo maximum).
- Connaître l'entité métier cible (ex. `ClientAccounts`).

## L'assistant en 5 étapes

L'assistant est un parcours en 5 étapes. Chaque étape se **confirme**
explicitement ; revenir sur une étape terminée puis la re-confirmer
réinitialise les étapes suivantes (jamais les précédentes).
Changer d'entité cible réinitialise le mapping, mais **pas** le fichier envoyé.

### 1. Entité cible
Choisir l'entité à alimenter dans la liste, vérifier l'aperçu du schéma
(champs, types), puis confirmer.

### 2. Envoi du fichier
Déposer le fichier `.xlsx`. Il est analysé dans le navigateur (aperçu des
50 premières lignes). Si le classeur contient **plusieurs feuilles**, choisir
la feuille à importer dans le sélecteur, puis confirmer.

### 3. Correspondance des colonnes (mapping)
- Les colonnes Excel sont **rapprochées automatiquement** par nom
  (`AccountNumber` ↔ `AccountNumber`). Ajuster les associations restantes
  dans le tableau ; chaque champ base ne peut être utilisé qu'une fois.
- **Champ obligatoire** : cocher « required » pour les colonnes qui doivent
  impérativement avoir une valeur sur chaque ligne (ex. n° de compte).
- **Formats** : selon le type du champ, préciser le format de date
  (`dd/MM/yyyy`, …), le séparateur décimal (point/virgule, symboles `$ €`
  à ignorer) et les libellés booléens (ex. vrai = `Oui`, faux = `Non`).
  La détection est automatique quand l'échantillon est sans ambiguïté.
- Confirmer, puis **enregistrer le mapping sous un nom** (ex.
  `Comptes – Banque X`). L'import exige un mapping enregistré :
  le bouton d'import reste inactif tant qu'aucun mapping n'est sélectionné.
  Un mapping enregistré peut être rechargé plus tard (les champs ayant
  disparu du schéma depuis sont signalés et restent non mappés).

### 4. Aperçu et validation
- L'aperçu montre les valeurs **transformées** (telles qu'elles seront
  importées avec les formats de l'étape 3).
- Lancer la **validation** : le serveur rejoue les mêmes règles que l'import
  sur tout le fichier, sans rien écrire en base. Le rapport indique les
  lignes acceptées et, pour chaque rejet, la ligne et le motif
  (champ obligatoire manquant, décimal/date/booléen illisible…).
- Essai conseillé : `lignes_rejetees_demo.xlsx` → 40 importées / 20 rejetées.

### 5. Vérification et import
- Relire le résumé (fichier, entité, mapping, effectifs de validation).
- Lancer l'**import** : le fichier est envoyé au serveur puis traité en
  tâche de fond (la progression est affichée). L'écriture est
  **tout-ou-rien par lot** : en cas d'échec, aucune ligne n'est conservée
  et le lot passe en `Failed` (conservé pour audit, jamais supprimé
  silencieusement).
- Le **rapport d'import** final donne les lignes importées / rejetées avec
  les motifs, et chaque ligne importée reste rattachée à son lot
  (traçabilité).

## Tableau de bord des imports

- **Filtres** : recherche (nom de fichier, utilisateur), statut, table cible,
  période (boutons 24 h / 7 j / 30 j), taille de page. Les filtres actifs
  s'affichent en pastilles, retirables une par une.
- **Journal d'un lot** : cliquer un lot pour voir ses lignes une par une
  (`Importées` / `Rejetées`), avec le motif de chaque rejet. L'affichage
  des données brutes (payloads) est masqué par défaut.
- La page se rafraîchit toutes les 2 secondes tant qu'un lot est en cours ;
  les vues filtrées ne sont jamais perturbées par ce rafraîchissement.

## En cas d'erreur

| Message / symptôme | Cause probable | Action |
|---|---|---|
| Fichier refusé à l'envoi | Pas un `.xlsx`, ou > 100 Mo | Convertir en `.xlsx`, réduire le fichier |
| `required field 'X'` | Colonne obligatoire vide sur la ligne | Corriger le fichier ou décocher « required » |
| `Cannot parse decimal/date/boolean` | Format non conforme (séparateur, ordre jour/mois, libellé) | Régler les options de format (étape 3) |
| Import impossible (bouton inactif) | Aucun mapping enregistré sélectionné | Enregistrer le mapping (étape 3) |
| Lot `Failed` | Erreur bloquante (ex. colonne mappée absente) | Lire le rapport, corriger, relancer |
| `Mapping … not found` (404) | Mapping supprimé entre-temps | Re-créer ou recharger le mapping |

## Règle d'or

**Rien n'est écrit en base sans validation humaine** : l'aperçu (étape 4)
puis l'écran de vérification (étape 5) sont les deux points de contrôle
obligatoires avant tout import. En cas de doute, relancer la validation
plutôt que l'import.
