# Altium Revision Retirement

Extension C# **autonome**, chargée directement dans Altium Designer, pour passer les anciennes révisions de **composants, symboles et footprints** à l'état **Obsolete** dans le workspace actif. Elle ne dépend d'aucun bridge, serveur local ou application compagnon.

**Version 0.1.3 de prévalidation** : compilation, installation et scan natif en lecture seule vérifiés avec Altium Designer Agile 26.11.1.28 / SDK C# 4.0 / .NET Framework 4.8. Le scan parcourt l'arborescence Components et remonte des anciennes révisions Draft de composants, symboles et footprints. Le logo Fluidra, l'interface anglaise et le lien vers le guide sont visibles dans Altium Designer. Les tests automatisés utilisent une base simulée. Les transitions natives restent à vérifier avec la [procédure de validation](docs/VALIDATION.md). Aucune transition de lifecycle réelle n'a été exécutée par l'installation ou ces contrôles.

## Installation

Prérequis : Windows x64, Altium Designer, extension **Altium Developer** et SDK 4.0 sous licence, .NET Framework 4.8.

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
```

Ouvrir ensuite **`AltiumRevisionRetirement.local.EPD`** dans Altium Designer, puis utiliser **Install Extension / Update Local Installation** dans Altium Developer. Fermer ensuite Designer normalement, relancer `scripts/build.ps1`, puis exécuter **`scripts/install.ps1`** pour copier les DLL et configurer le logo et la description. Redémarrer Designer pour charger l'extension. Le build conserve une sortie locale même si Developer réécrit le chemin de sortie du projet.

Ouvrir **DXP → Revision Retirement…**, ou **Tools → Revision Retirement…** dans un éditeur schématique/PCB. Le process **`AltiumRevisionRetirement:Manage`** via **Run Process** reste disponible. Les DLL du SDK et le ticket proviennent uniquement de l'installation locale et ne sont pas stockés dans GitHub. Le fichier EPD suivi par Git est un modèle : le build produit le fichier `.local.EPD` avec les chemins résolus.

Le [guide utilisateur complet en anglais](docs/USER_GUIDE.md) couvre l'installation, les contrôles, le workflow, les erreurs et la désinstallation. Il est aussi accessible depuis le bandeau de l'extension.

L'ensemble des contrôles, états d'avancement, erreurs et confirmations de l'extension est désormais en anglais.

## Utilisation

1. Se connecter au workspace voulu dans Altium Designer.
2. Cliquer sur **Scan** : lecture seule des anciennes révisions dont le lifecycle n'est pas `Obsolete`. Les dernières révisions sont exclues, y compris celles masquées ou planifiées.
3. Cocher les lignes une par une, ou utiliser **Select all** pour toutes celles possédant une transition directe valide. Les révisions sans droits suffisants ou sans transition directe restent visibles avec le motif de blocage.
4. Cliquer sur **Set selected revisions to Obsolete…**. L'extension relit les données et vérifie les dépendances. Un modèle encore référencé par une révision non Obsolete est protégé : sélectionner aussi cette ancienne révision parente si elle est éligible, ou conserver le modèle. Une dernière révision parente n'est jamais ajoutée automatiquement.
5. Vérifier le plan, saisir la note de changement obligatoire et confirmer. Les parents sélectionnés passent avant leurs modèles. L'extension ne crée aucune transition et ne contourne pas les droits.
6. Relancer l'analyse après l'opération. **Stop** interrompt le lot entre deux opérations ; les transitions réussies restent appliquées.

## Protections

La dernière révision est déterminée par l'API native et son GUID, sans tri alphabétique des numéros. Le workspace, l'utilisateur, l'historique des révisions, les droits, l'état courant et la transition sont relus avant chaque changement. L'appel natif relit à nouveau la révision après l'écriture du journal.

Une seule transition native est soumise à la fois. Aucun accès direct à la base, suppression, modification de contenu, publication ou changement de définition de lifecycle n'est implémenté. Les états de toutes les autres révisions du même item sont comparés après chaque transition, dont celui de la dernière révision.

Le journal CSV se trouve dans `%LOCALAPPDATA%\AltiumRevisionRetirement\Audit`. Il est enregistré sur disque avant chaque tentative, puis après vérification. Toute erreur ou réponse incertaine arrête le lot, sans nouvel essai automatique. Contrôler la timeline native Altium avant une reprise manuelle.

L'API utilisée ne fournit pas de transaction atomique associant le test « ancienne révision » au changement d'état. Les relectures ne constituent donc pas une garantie absolue contre une suppression/restauration de révision, modification de dépendance ou modification de lifecycle simultanée par un autre client. Coordonner ces opérations pendant le traitement. Les dépendances contrôlées sont celles exposées par l'API native et les droits de l'utilisateur. Les anciens projets conservent leurs liens mais peuvent signaler l'inapplicabilité d'une révision devenue Obsolete.

La [documentation anglaise](README.md) contient les détails de build et d'architecture. La [conception de sécurité](docs/SAFETY.md) précise les limites. Les standards de lifecycle et d'installation suivis sont ceux de la [documentation Altium](https://www.altium.com/documentation/altium-designer/connected-workspace/items/managing-revision-lifecycle) et du [DXP Developer](https://www.altium.com/documentation/altium-dxp-developer) ; cette extension indépendante n'est pas certifiée par Altium.
