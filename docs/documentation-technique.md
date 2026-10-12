---
title: "Documentation technique DataMart"
subtitle: "Import Excel contrôlé vers base de données : architecture, pipeline, sécurité, tests"
date: 2026-10-10
lang: fr
---

> Document technique du projet DataMart, outil d'import Excel vers base de
> données destiné à une société de conseil bancaire et financière.

---

## 1. Objet et contraintes métier

DataMart mappe un fichier Excel (`.xlsx`) vers une table de la base de données
au terme d'un parcours en 5 étapes, dont la règle fondatrice est simple :
**aucune écriture sans validation humaine**.

Le cadre bancaire et financier fixe les priorités, dans cet ordre :

1. **Traçabilité.** Chaque ligne importée porte son lot et sa ligne source
   (`ImportBatchId` + `SourceKey`, écrits dans la table cible elle-même),
   chaque lot conserve son statut, son checksum et ses totaux de contrôle,
   chaque opération sensible est journalisée (`AccessLog`). Deux niveaux
   coexistent : la trace portée par la table cible fait foi et est garantie ;
   le journal ligne à ligne, avec le snapshot des valeurs, est une réplication
   après coup en best effort, donc un lot `Success` peut garder un journal
   partiel (`JournalIncomplete`) et un lot `Failed` n'en a aucun puisqu'il
   n'a rien persisté (§6). Ce que le journal perd, les totaux du lot les
   gardent (`TotalRows`, `SkippedRows`, aperçu des lignes sautées) ; les
   lignes rejetées, elles, n'atteignent jamais la table cible et ne conservent
   leur motif détaillé que dans ce journal. Dans tous les cas, une correction
   remonte du chiffre en base jusqu'à la ligne du classeur.
2. **Pas d'écriture silencieuse.** Rien n'atteint la base tant qu'un humain n'a
   pas confirmé l'aperçu (étape 4) puis lancé l'import (étape 5). La validation
   pré-vol, elle, n'écrit jamais, c'est un dry-run.
3. **Cohérence validation/import.** Les deux chemins partagent les mêmes
   briques d'analyse (voir §7 et §9) : un fichier validé propre s'importe
   propre, et réciproquement. Les deux chemins sont écrits pour ne pas pouvoir
   diverger.

---

## 2. Stack et organisation du dépôt

| Élément | Choix |
|---|---|
| Backend | .NET 10, ASP.NET Core Minimal APIs |
| Architecture | Clean Architecture + CQRS (MediatR) |
| ORM | EF Core 10 (PostgreSQL via Npgsql), migrations Code First |
| Validation | FluentValidation (pipeline MediatR) |
| Mapping DTO | AutoMapper (profils découverts automatiquement) |
| Orchestration | .NET Aspire (AppHost) |
| Frontend | Angular 21 (NgModule), DevExtreme 26, SheetJS (worker), client API généré par NSwag |
| Tests | xUnit 2.9, Shouldly, Moq ; tests d'intégration sur PostgreSQL réel via Aspire |

```
DataMart.slnx
├── src/
│   ├── Domain/          Entités, enums, interfaces communes. Aucune dépendance externe.
│   ├── Application/     Cas d'usage CQRS (Command/Query + handler + validateur),
│   │                    DTOs, comportements de pipeline. Dépend de Domain uniquement.
│   ├── Infrastructure/  ApplicationDbContext, Identity, configurations EF,
│   │                    BulkImporter (COPY), services d'arrière-plan, migrations.
│   ├── Web/             Endpoints Minimal API, host SPA, antiforgery, composition root.
│   ├── AppHost/         Orchestrateur Aspire (PostgreSQL + API + frontend).
│   ├── ServiceDefaults/ Télémétrie/resilience partagés (OpenTelemetry).
│   └── Shared/          Constantes (noms de ressources/services).
└── tests/               Voir §13.
```

**Dépendances orientées vers l'intérieur** : `Domain ← Application ←
Infrastructure ← Web`. `Application` expose `IApplicationDbContext` ;
`Infrastructure` l'implémente. Pas de couche repository : les handlers
utilisent `IApplicationDbContext` directement.

**Conventions structurantes** :

- Un cas d'usage = un record `IRequest` + son handler **dans le même fichier**,
  sous `src/Application/<Feature>/Commands|Queries/<Action>/`.
- Chaque commande porte un validateur FluentValidation co-localisé, exécuté
  automatiquement par `ValidationBehaviour` (pipeline MediatR).
- Les endpoints implémentent `IEndpointGroup` dans `src/Web/Endpoints/` et sont
  **montés par réflexion** au démarrage sur `/api/{NomClasse}` (ou `RoutePrefix`
  quand la classe en définit un). Aucun enregistrement manuel.
- Les DTOs embarquent un profil AutoMapper imbriqué (`Mapping : Profile`),
  découvert automatiquement.

---

## 3. Orchestration Aspire

`dotnet run --project src/AppHost` démarre l'application complète. Le tableau
de bord Aspire (ouvert automatiquement) donne les URLs, les logs, les traces
et l'état de chaque ressource.

Modèle d'application (`src/AppHost/Program.cs`) :

| Ressource | Rôle |
|---|---|
| `dbserver` | PostgreSQL (Azure Postgres Flexible Server **exécuté en conteneur local**, `ContainerLifetime.Persistent` : les données survivent aux redémarrages). Base `DataMartDb`. |
| `webapi` | Projet `Web`. Référence `dbserver` (`WithReference` + `WaitFor` : l'API ne démarre qu'une fois la base saine). Endpoint HTTP externe ; l'URL « Scalar API Reference » pointe vers `/scalar`. |
| `webfrontend` | Application Angular (`AddJavaScriptApp`, `npm start`), référence `webapi` pour la découverte de service ; le proxy de dev (`proxy.conf.js`) route `/api` et `/openapi` vers l'API. |

`tests/TestAppHost` réutilise le même modèle pour les tests d'intégration
(`DistributedApplicationTestingBuilder`, dashboard désactivé).

La chaîne de télémétrie (logs, traces, métriques OpenTelemetry) est câblée par
`ServiceDefaults` et visible dans le dashboard Aspire.

---

## 4. Base de données

### 4.1 Entités principales

| Entité | Rôle |
|---|---|
| `ClientAccount`, `CustomerTransaction` | Entités métier cibles d'import (démo). Toute entité héritant de `BaseAuditableEntity` est **importable par défaut** ; `[Exclude]` (classe ou propriété) retire de la liste. |
| `Mapping` / `ColumnMapping` | Configuration de mapping réutilisable : colonnes Excel → champs DB, formats (date/heure/décimal/booléen), flag `IsRequired` par colonne. `[Exclude]` (entité système). |
| `ImportBatch` | Enregistrement de contrôle/audit par import : fichier, table cible, statut, compteurs, `SourceChecksum` (SHA-256), `ExecutedBy`, `ControlTotalsJson` (réconciliation), `LegalHold`, `RetentionExpiresAt`, champs de revert. **Immuable après statut terminal** (`Success`/`Failed`/`Reverted`) ; les setters lèvent `InvalidOperationException`. `[Exclude]`. |
| `ImportRecord` | Journal par ligne : `Payload` (snapshot JSON des valeurs mappées), `Status` (`Imported`/`Rejected`), `ErrorMessage`, `SourceKey`. Alimente le rapport d'import et le journal du dashboard. `[Exclude]`. |
| `EntityValidationRule` | Règle de validation par entité (voir §9). `[Exclude]`. |
| `AccessLog` | Journal d'accès append-only : `UserId`, `Action` (`ViewSchema`, `CreateMapping`, `UpdateMapping`, `ExecuteImport`, `PreviewData`), `MappingId`/`BatchId`, `Timestamp`, `CorrelationId`. `[Exclude]`. |

`SourceKey` et `ImportBatchId` existent sur `BaseAuditableEntity` (exclus) :
toute ligne importée est rattachée à son lot et à sa ligne source.

### 4.2 Migrations

Schéma versionné par migrations EF Core (Code First), appliquées au démarrage
par `ApplicationDbContextInitialiser.MigrateAsync()` puis seed (rôles +
`admin@localhost`) :

1. `InitialCreate`
2. `AddImportBatchJournalRowsWritten`
3. `AddImportBatchJournalIncomplete`
4. `AddEntityValidationRule`

Nouvelle migration : `dotnet ef migrations add <Nom> --project src/Infrastructure`.
Réinitialisation dev : `dotnet ef database drop` puis redémarrage (migrations + seed).

---

## 5. Surface API

Toutes les routes sont sous `/api`, authentification requise
(`RequireAuthorization` par groupe). Référence interactive : `/scalar` ;
spécification OpenAPI : `/openapi/v1.json` (base du client NSwag).

| Groupe | Routes | Rôle |
|---------------|--------------------------|----------|
| `Import` | `POST /api/Import` (upload + mappingId + emptyMode + sheetIndex) · `POST /api/Import/validate` · `GET /api/Import/validations/{id}` · `GET /api/Import/batches/{id}` · `…/preview` · `…/report` | Upload, validation async, statut, lots, rapport. |
| `MappingEndpoints` | `GET/POST /api/mappings`, `GET/PUT/DELETE /api/mappings/{id}` (filtres `targetEntity`, `search`) | CRUD des mappings sauvegardés. |
| `EntityRuleEndpoints` | `GET/POST /api/entity-rules`, `PUT /api/entity-rules/{id}` | Règles de validation. Pas de DELETE : désactivation via `IsActive` pour que les anciens rapports restent explicables. |
| `Dashboard` | `GET /api/Dashboard/statistics` · `…/batches` (pagination + filtres search/status/table/dates) · `…/batches/{id}/journal` (statut filtrable) | Historique et journal des imports. |
| `EntitySchema` | `GET …/schemas` | Schéma des entités importables (alimente l'étape 1 et l'auto-match). |
| `Antiforgery` | `GET /api/antiforgery` | Émet le token de requête (voir §10). |
| `Users` | login/register/logout (Identity) | Authentification. |

Erreurs : `ProblemDetailsExceptionHandler` mappe `AppValidationException` → 400,
`NotFoundException` → 404, `UnauthorizedAccessException` → 401,
`ForbiddenAccessException` → 403, et les échecs antiforgery → 400 avec un
marqueur de type stable consommé par le SPA (§10).

---

## 6. Pipeline d'import

```
POST /api/Import
  │ taille ≤ 100 Mo (endpoint + Kestrel + multipart) ; XlsxPackageInspector :
  │ signature ZIP 50 4B 03 04, ≤ 1 000 entrées, ≤ 600 Mo décompressé, rejet macros
  ▼
ImportFileCommand (MediatR)
  │ mapping trouvé (404 sinon) ; fichier copié vers un temp file nommé GUID
  │ (hors webroot) ; ImportBatch créé (Status=Processing, TotalRows précomptés)
  ▼
ImportJobQueue (channel en mémoire) ──► ImportProcessingService (BackgroundService)
  ▼
ImportFileExecutor.ExecuteAsync (scope DI dédié)
  │ ExcelDataReader en lecture forward-only (reader.Read(), jamais AsDataSet)
  │ plafond 1 100 000 lignes ; avance jusqu'à la feuille choisie (NextResult)
  │ par ligne : ImportCellParser.Parse (types/formats) → champs requis
  │   (EmptyFieldMode : Throw / SkipRow / Default) → RuleEvaluator (§9)
  │ journal : ImportRecord par ligne (payload JSON + verdict)
  ▼
BulkImporter : COPY "<table>" (...) FROM STDIN BINARY (Npgsql)
  │ transaction unique : tout ou rien
  ▼
ImportBatch → Success (+ ControlTotalsJson) ou Failed (conservé pour audit)
  ▼
journal ImportRecord écrit par chunks de 5 000 après le COPY
  │ (best effort ; échec → JournalIncomplete, l'import reste Success)
```

Les lignes parsées et les entrées de journal sont matérialisées en mémoire
avant le COPY. Ce qui reste forward-only, c'est la lecture des octets du
classeur : ExcelDataReader les parcourt ligne à ligne (`reader.Read()`, jamais
via `AsDataSet()`), sans porter le fichier entier. Le traitement lui-même est
borné par le plafond de 1 100 000 lignes, qui est le véritable garde-fou
mémoire.

Une première version régénérait les lignes directement depuis le reader ;
après un échec transitoire du COPY, la tentative suivante repartait d'un
reader déjà en fin de fichier, produisait zéro ligne, et le lot finissait
`Success` avec une table vide. Matérialiser une seule fois donne au mécanisme
de retry une source ré-itérable et supprime la cause du bug.

Le COPY binaire (`COPY ... FROM STDIN BINARY`, Npgsql) a été retenu d'emblée
pour le débit : l'objectif était de tenir des fichiers volumineux sans temps
d'import prohibitif. Le chemin EF (insertions par lots via `SaveChanges`) n'a
pas été comparé par benchmark ; le choix repose sur l'écart de performances
connu entre COPY et insertions par lot, pas sur une mesure locale.

Le COPY s'exécute dans une transaction unique : un échec ne laisse rien à
nettoyer ni à replacer. Un import par lots exigerait de retenir ce qui est
déjà parti et d'ajouter un mécanisme de rattrapage, soit une seconde couche
transactionnelle par-dessus la première.

Le journal, lui, part après le COPY, par chunks de 5 000 lignes. Un import
échoué ne laisse donc aucune entrée de journal, et un échec d'écriture de
journal ne fait pas retomber un import réussi : le lot porte alors
`JournalIncomplete`, car les lignes manquantes ne sont plus dérivables (le
fichier source est supprimé dès la fin du traitement). C'est une conséquence
directe du choix transactionnel, et le dashboard l'expose comme telle (pastille
« journal en cours d'upload »).

Côté SPA, le POST retourne `ImportStarted(batchId)` immédiatement ; le wizard
interroge `GET batches/{id}` toutes les 500 ms (`startPoller`) jusqu'à un statut
terminal, puis affiche le rapport (`GET batches/{id}/report`).

Ordres de grandeur mesurés en local (PostgreSQL en conteneur Aspire, machine de
développement, appel direct du cas d'usage sans la couche HTTP/antiforgery) :
un import nominal de 2 000 lignes atteint le statut `Success` en ~0,85 s de
bout en bout, dont ~0,2 s de phase synchrone du POST (temp file, checksum,
pré-compte) ; ~0,95 s au tout premier lancement, caches à froid. Les chiffres
couvrent l'atteinte du statut, le rattrapage du journal se terminant juste
après. À ce volume, la transformation est probablement minoritaire dans le
temps total : les écritures de contrôle fixes (insertions du lot, statuts,
journal) sont l'explication la plus plausible, mais le profil détaillé n'a pas
été relevé. Côté utilisateur, la granularité du polling (500 ms) s'ajoute à ces
chiffres, pour une attente perçue de l'ordre de 1,3 s dans le pire cas. Les pistes
d'optimisation, non priorisées faute de profil, figurent en fin de §14.

---

## 7. Validation pré-vol (dry-run)

Même chaîne d'analyse que l'import, **sans aucune écriture** :

```
POST /api/Import/validate
  ▼ StartValidationCommand : précompte des lignes (ImportRowCounter), échec rapide
    sur mapping inconnu / colonnes manquantes / fichier surdimensionné
  ▼ ValidationJobQueue (channel séparé : un dry-run long ne bloque jamais un import)
  ▼ ValidationProcessingService : ImportFileValidator.ValidateAsync avec
    IProgress<int> throttlé (~1 % des lignes) ; temp file supprimé en finally
  ▼ IValidationProgressStore (ConcurrentDictionary en mémoire)
  ▼ GET /api/Import/validations/{id} → ValidationStatusDto (Running/Done/Failed
    + rapport ImportReportDto : total, importées, rejetées, erreurs par ligne)
```

`RunValidationCommand` (synchrone) reste disponible pour les tests. La
validation charge les règles actives une fois par exécution : une règle modifiée
en cours de route ne peut pas changer le verdict à mi-fichier.

---

## 8. Mapping et formats

Le wizard rapproche d'abord les colonnes automatiquement (noms normalisés :
casse et séparateurs ignorés), puis l'utilisateur complète à la main ; un champ
base ne peut recevoir qu'une seule colonne. Ce rapprochement guide la suite :
c'est lui qui détermine quelles validations s'appliqueront.

Le champ obligatoire a deux sources qui se cumulent (le flag `IsRequired`
posé colonne par colonne dans le mapping, et les colonnes non-nullables du
schéma), et l'import contrôle l'union des deux.

Les formats source se règlent par champ : dates (format déclaré, repli sur
l'année à 2 chiffres, puis culture invariante, puis culture française), heures,
décimales (séparateur, suppression des symboles monétaires), booléens
(libellés vrai/faux personnalisés : `Oui`/`Non`, `1`/`0`…). Quand l'échantillon
de données est sans ambiguïté (un jour > 12 tranche jour/mois), le format est
détecté automatiquement.

Le mode `EmptyFieldMode` se choisit **au niveau de la validation** et
s'applique ensuite à l'import : `Throw` rejette le lot, `SkipRow` ignore la
ligne, `Default` écrit la valeur par défaut du type. Un seul réglage, donc les
deux chemins jugent le fichier de la même façon.

Enfin, un mapping doit être **enregistré** pour autoriser validation et import ;
c'est une contrainte de traçabilité (le lot référence le mapping utilisé). Le
wizard invalide le mapping sauvegardé dès qu'une édition locale diverge :
validation et import ne peuvent pas s'exécuter sur une version périmée du
mapping affiché.

---

## 9. Règles de validation par entité

`EntityValidationRule` : `TargetEntity`, `Field`, `Operator` (égalité,
comparaisons, `In`/`NotIn`, `Contains`), membre de droite soit constante
(`Value` / `Values`), soit fonction horaire (`Today`, `TodayMinusYears(N)`,
`TodayMinusDays(N)`, évaluée en UTC au moment de l'import via `TimeProvider`),
`Message` personnalisable, `IsActive`.

La sémantique d'évaluation tient en quatre règles, implémentées dans
`RuleEvaluator` (code pur, sans I/O ; l'horloge arrive en paramètre, ce qui
permet aux tests d'épingler des dates exactes).

- Les règles **actives** de l'entité sont combinées en ET : une ligne n'est
  importée que si elle les satisfait toutes.
- Une cellule vide (nulle ou placeholder type `N/A`) saute la règle ; la
  nullité stricte relève du schéma et des champs requis (§8).
- Une règle qui référence un champ disparu du schéma (dérive de schéma) lève
  une exception et interrompt l'exécution ; il faut corriger ou désactiver la
  règle avant de relancer.
- Validation et import appellent le même évaluateur avec les mêmes entrées, et
  chaque échec conserve la valeur résolue du membre de droite (`ResolvedValue`),
  ce qui garde les rapports explicables même si la règle change ensuite.

---

## 10. Sécurité

**CSRF (antiforgery)** : modèle en deux moitiés.

- Cookie `XSRF-TOKEN` (`SameSite=Strict`, `HttpOnly` : le JS ne le lit pas).
- `GET /api/antiforgery` retourne le *request token* correspondant dans le
  corps ; `XsrfInterceptor` le met en cache et l'envoie dans l'en-tête
  `RequestVerificationToken` sur les requêtes modifiantes.
- Auto-réparation : cache absent → priming puis retry ; token périmé (redémarrage
  serveur) → le handler répond 400 avec le type `https://datamart/antiforgery`,
  l'intercepteur jette le cache, re-prime et retente **une seule fois**.

**Upload** : la validation client n'est que de l'UX ; le serveur vérifie
indépendamment : taille (100 Mo, alignée sur Kestrel et multipart), signature
ZIP, structure du paquet OOXML, nombre d'entrées (≤ 1 000), taille décompressée
(≤ 600 Mo, cap temporairement relevé pour les tests de charge 1,1 M lignes),
rejet des macros, nom de fichier régénéré (GUID), stockage hors webroot,
traitement borné par un plafond de 1 100 000 lignes (§6).

**Authentification / autorisation** : ASP.NET Core Identity (cookie), groupes
d'endpoints derrière `RequireAuthorization`, garde `AuthGuard` côté SPA.

**Maker-checker (quatre yeux)** : la règle « l'auteur du mapping ne peut pas
exécuter l'import » est implémentée mais **commentée** dans `UploadFile`
(environnement de dev mono-utilisateur). À réactiver (et rendre togglable par
environnement) avant production.

**Journalisation d'accès** : `AccessLogService` écrit une entrée à chaque
opération sensible (consultation du schéma, création/édition de mapping,
exécution d'import, lecture de données) avec utilisateur, horodatage et
corrélation.

---

## 11. Configuration des garde-fous

La plupart des plafonds évoqués plus haut ne sont pas dans un fichier de
configuration mais **dans le code, sous forme de constantes**. C'est délibéré.
Plusieurs d'entre eux fonctionnent par paires : le contrôle applicatif de
taille rejette le fichier *avant* d'en lire le contenu, et ne sert à rien si
Kestrel admet davantage. Rendre ces plafonds configurables est envisageable ;
la seule vraie contrainte est de déplacer les valeurs appariées ensemble, pour
qu'aucune couche ne laisse passer ce qu'une autre rejette.

La charge attendue est faite de fichiers petits et fréquents (imports
évènementiels de quelques centaines à quelques milliers de lignes). Les
plafonds du tableau sont dimensionnés bien au-delà de ce volume : le test à
1,1 M lignes a servi à démontrer la marge, pas à décrire la charge. Le cap
décompressé en tire une conséquence à moyen terme : il sert aussi de défense
zip-bomb, et un plafond très supérieur aux fichiers réels protège moins bien
qu'un plafond calé sur l'ordre de grandeur réel. À terme : régler les défauts
sur la charge réelle, et ne les relever que pour les campagnes de tests de
charge.

| Réglage | Valeur | Emplacement |
|---------------|---------------------|---------------------|
| Taille maximale d'upload | 100 Mo | `src/Web/Program.cs` (Kestrel `MaxRequestBodySize` **et** `MultipartBodyLengthLimit`) + contrôle dans `UploadFile` / `ValidateFile` (`src/Web/Endpoints/Import.cs`) ; les trois doivent rester alignés |
| Entrées du paquet OOXML | 1 000 max | `DefaultMaxEntries`, `src/Web/Common/XlsxPackageInspector.cs` |
| Taille décompressée du paquet | 600 Mo max | `DefaultMaxDecompressedBytes`, même fichier, **temporairement relevé** pour les tests de charge 1,1 M lignes ; à rabaisser ensuite |
| Plafond de lignes à l'import | 1 100 000 | `ImportFileCommand.MaxRows`, `src/Application/ImportFile/Commands/ImportFile/ImportFile.cs` |
| Aperçu Excel (frontend) | 50 lignes | option `sheetRows`, `src/Web/ClientApp/src/app/Excelmap/excel.worker.ts` |
| Polling wizard | 500 ms | options `startPoller`, `excelmap.component.ts` |
| Rafraîchissement dashboard | 2 s | `dashboard.component.ts` |
| Cookie / en-tête antiforgery | `XSRF-TOKEN` / `RequestVerificationToken` | `AddAntiforgery`, `src/Web/Program.cs` |
| Chaîne de connexion | PostgreSQL local | fournie par Aspire (`appsettings.json` en dev autonome) |

---

## 12. Frontend (Angular)

- **Wizard 5 étapes** (`Excelmap`) : état dans des signals, état dérivé en
  `computed`, `confirmStep` avec réinitialisation en cascade des étapes
  aval ; `dx-stepper` linéaire en indicateur. Changer d'entité réinitialise le
  mapping mais pas le fichier ; changer de feuille réinitialise le mapping.
- **Worker** (`excel.worker.ts`) : parsing SheetJS hors thread principal,
  `ArrayBuffer` transféré zero-copy, aperçu limité à 50 lignes ; worker
  recréé à chaque parse (résultats périmés impossibles).
- **Polling** : utilitaire partagé `startPoller` (import et validation),
  intervalle 500 ms, handle stoppable (`ngOnDestroy`).
- **Composants purs testables** sans Angular : `format-detection.ts`,
  `cell-transform.ts`, `poll.ts`.
- **Client API** : généré par NSwag depuis `/openapi/v1.json`
  (`npm run generate-api`), jamais édité à la main.
- **Dashboard** : filtres serveur (recherche insensible à la casse, statut,
  table, dates) + pastilles de filtres, rafraîchissement auto 2 s uniquement
  hors filtres, journal par lot avec filtres statut/message.

---

## 13. Tests

Tous les projets de test vivent sous `tests/`.

| Projet | Contenu | Exécution |
|-------------------|-----------------------|---------|
| `Domain.UnitTests` | Logique du domaine (immuabilité `ImportBatch`) ; 24 tests | `dotnet test` |
| `Application.UnitTests` | Parseurs (ImportCellParser), évaluateur de règles, services mockés, profils AutoMapper ; 71 tests | `dotnet test` |
| `Application.FunctionalTests` | **Intégration sur PostgreSQL réel** via Aspire (`TestAppHost`) + Respawn : **les 5 scénarios requis** (import réussi, colonne obligatoire manquante, type invalide, erreurs partielles, mapping inexistant), filtres du dashboard, règles d'entité ; 20 tests | Docker requis |
| `FixturesGenerator` | Génère les jeux de tests `.xlsx` (fixtures d'intégration + livrables `docs/sample-data`) de façon déterministe | `dotnet run` |
| Frontend | 78 specs Jasmine/Karma (parsing, poller, formatage des règles, intercepteur XSRF) | `npm test` (`CHROME_BIN` → Edge si Chrome absent) |

Les datasets de démonstration (`docs/sample-data/`, valeurs françaises, `.xlsx`
uniquement) sont documentés avec leur résultat attendu dans le README du dossier.

---

## 14. Limitations connues et évolutions

La plupart des limites ci-dessous tiennent au volume attendu du système, fait
de fichiers petits et fréquents.

- **INSERT only** : pas de mode UPSERT/mise à jour, chaque import crée de
  nouvelles lignes. Choix du MVP ; un mode réconciliation appartiendrait à une
  deuxième version.
- **Maker-checker désactivé** en dev (§10).
- **Files d'attente et progression en mémoire** (`ImportJobQueue`,
  `ValidationJobQueue`, `IValidationProgressStore`) : les travaux en vol sont
  perdus au redémarrage du processus. Avec des imports courts et peu fréquents,
  la fenêtre de perte est minuscule. Une file durable (Hangfire, files
  PostgreSQL) devient nécessaire si le volume ou l'exigence de continuité
  montent.
- **Lignes et journal en mémoire** : voir §6 ; borné par le plafond de lignes,
  et cohérent avec le volume attendu.
- **Traitement mono-threadé** : au-delà du COPY, l'optimisation n'a pas été
  poussée ; le parse et l'évaluation des règles sont séquentiels. La
  parallélisation reste possible, parce que le travail par ligne est une
  fonction pure et que l'ordre se rétablit par indexation (chaque ligne
  connaît son numéro de source).
- **Deux freins à cette parallélisation** : le décodage Excel est séquentiel
  (flux ZIP/XML unique), ce qui plafonne la phase de transformation ; et la
  garantie de retry du COPY repose sur la matérialisation des lignes (cf. le
  bug corrigé du lot vide), qu'un pipeline parallèle devrait ré-ancrer, par
  exemple en rejouant le calcul sur échec puisque la transformation est pure.
- **Cap décompressé à 600 Mo temporaire** : voir §11.
- **Revert non exposé** : `ImportBatch.Revert()` existe dans le domaine, aucun
  endpoint ne l'appelle encore.
- **Un seul format d'entrée** : `.xlsx`, par choix produit. Le parseur
  streaming accepterait le CSV à peu de frais, si un jour un usage le justifie.

Si la performance devenait un jour un enjeu, l'ordre des travaux devrait venir
d'un profil réel du pipeline, qui n'a pas été relevé à ce jour ; sans cette
mesure, toute hiérarchie de gains serait une estimation. Les pistes évoquées ici (file durable pour la continuité,
parallélisation de la transformation et des règles, qui se heurterait au
décodage séquentiel du lecteur Excel) diffèrent trop en coût d'ingénierie
pour être priorisées à l'aveugle.

---

*Documents liés : `docs/guide-utilisation.md` (guide utilisateur),
`docs/sample-data/README.md` (jeux de tests).*
