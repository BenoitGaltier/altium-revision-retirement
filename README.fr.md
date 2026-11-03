# Altium Revision Retirement

Extension C# **autonome**, chargée directement dans Altium Designer, pour passer les anciennes révisions de **composants, symboles et footprints** à l'état **Obsolete** dans le workspace actif. Elle ne dépend d'aucun bridge, serveur local ou application compagnon.

**Version 0.1.0 de prévalidation** : compilation native réussie avec Altium Designer Agile 26.11.1.28 / SDK C# 4.0 / .NET Framework 4.8. Les tests automatisés utilisent une base simulée. L'installation et les transitions sur un vrai workspace restent à vérifier avec la [procédure de validation](docs/VALIDATION.md). Aucun lifecycle de production n'a été modifié pendant le développement.

## Installation

Prérequis : Windows x64, Altium Designer, extension **Altium Developer** et SDK 4.0 sous licence, .NET Framework 4.8.

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
```

Ouvrir ensuite **`AltiumRevisionRetirement.local.EPD`** dans Altium Designer, puis utiliser **Install Extension / Update Local Installation** dans Altium Developer. Redémarrer Designer pour charger l'extension. Si Developer modifie la configuration de sortie du projet, le recompiler.

Ouvrir **DXP → Revision Retirement…**, ou **Tools → Revision Retirement…** dans un éditeur schématique/PCB. Le process **`AltiumRevisionRetirement:Manage`** via **Run Process** reste disponible. Les DLL du SDK et le ticket proviennent uniquement de l'installation locale et ne sont pas stockés dans GitHub. Le fichier EPD suivi par Git est un modèle : le build produit le fichier `.local.EPD` avec les chemins résolus.

## Utilisation

1. Se connecter au workspace voulu dans Altium Designer.
2. Cliquer sur **Analyser** : lecture seule des anciennes révisions dont le lifecycle n'est pas `Obsolete`. Les dernières révisions sont exclues, y compris celles masquées ou planifiées.
3. Cocher les lignes une par une, ou utiliser **Tout sélectionner** pour toutes celles possédant une transition directe valide. Les révisions sans droits suffisants, non publiées ou sans transition directe restent visibles avec le motif de blocage.
4. Cliquer sur **Passer la sélection à Obsolete…**. L'extension relit les données et vérifie les dépendances. Un modèle encore référencé par une révision non Obsolete est protégé : sélectionner aussi cette ancienne révision parente si elle est éligible, ou conserver le modèle. Une dernière révision parente n'est jamais ajoutée automatiquement.
5. Vérifier le plan, saisir la note de changement obligatoire et confirmer. Les parents sélectionnés passent avant leurs modèles. L'extension ne crée aucune transition et ne contourne pas les droits.
6. Relancer l'analyse après l'opération. **Arrêter** interrompt le lot entre deux opérations ; les transitions réussies restent appliquées.

## Protections

La dernière révision est déterminée par l'API native et son GUID, sans tri alphabétique des numéros. Le workspace, l'utilisateur, l'historique des révisions, les droits, l'état courant et la transition sont relus avant chaque changement. L'appel natif relit à nouveau la révision après l'écriture du journal.

Une seule transition native est soumise à la fois. Aucun accès direct à la base, suppression, modification de contenu, publication ou changement de définition de lifecycle n'est implémenté. Les états de toutes les autres révisions du même item sont comparés après chaque transition, dont celui de la dernière révision.

Le journal CSV se trouve dans `%LOCALAPPDATA%\AltiumRevisionRetirement\Audit`. Il est enregistré sur disque avant chaque tentative, puis après vérification. Toute erreur ou réponse incertaine arrête le lot, sans nouvel essai automatique. Contrôler la timeline native Altium avant une reprise manuelle.

L'API utilisée ne fournit pas de transaction atomique associant le test « ancienne révision » au changement d'état. Les relectures ne constituent donc pas une garantie absolue contre une suppression/restauration de révision, modification de dépendance ou modification de lifecycle simultanée par un autre client. Coordonner ces opérations pendant le traitement. Les dépendances contrôlées sont celles exposées par l'API native et les droits de l'utilisateur. Les anciens projets conservent leurs liens mais peuvent signaler l'inapplicabilité d'une révision devenue Obsolete.

La [documentation anglaise](README.md) contient les détails de build et d'architecture. La [conception de sécurité](docs/SAFETY.md) précise les limites. Les standards de lifecycle et d'installation suivis sont ceux de la [documentation Altium](https://www.altium.com/documentation/altium-designer/connected-workspace/items/managing-revision-lifecycle) et du [DXP Developer](https://www.altium.com/documentation/altium-dxp-developer) ; cette extension indépendante n'est pas certifiée par Altium.
