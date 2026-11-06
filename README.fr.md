# Altium Revision Retirement

Extension C# **autonome**, chargée directement dans Altium Designer, pour passer les anciennes révisions de **composants, symboles et footprints** à l'état **Obsolete** dans le workspace actif. Elle ne dépend d'aucun bridge, serveur local ou application compagnon.

**Version 0.1.5 de prévalidation** : l'archive Windows contient l'extension compilée et ses deux DLL Altium. Aucun Altium Developer, SDK, compilateur, IDE ou projet EPD n'est requis sur le poste utilisateur. Le scan natif a été vérifié avec Altium Designer Agile 26.11.1.28 / SDK C# 4.0 / .NET Framework 4.8. Les transitions natives restent à vérifier avec la [procédure de validation](docs/VALIDATION.md). Aucune transition réelle n'a été exécutée par ces contrôles.

## Installation

Télécharger **`altium-revision-retirement-0.1.5-windows-x64.zip`** dans les assets de la release, extraire l'archive, fermer Altium puis lancer dans son dossier :

```powershell
.\Install.ps1
```

Prérequis : Windows x64, .NET Framework 4.8 et Altium Designer sous licence. `Altium.SDK.dll` et `Altium.SDK.Interfaces.dll` sont incluses dans l'archive et vérifiées par l'installateur. Altium Developer n'est pas requis. Les sources du SDK, tickets et fichiers d'activation sont exclus.

Redémarrer Altium puis ouvrir **Tools → Revision Retirement…** dans un éditeur schématique/PCB. `Install.ps1 -CheckOnly` vérifie les dépendances sans modifier l'installation. Les [instructions d'installation en anglais](docs/INSTALLATION.md) détaillent les avertissements Windows, les chemins personnalisés et les sauvegardes. Les téléchargements GitHub « Source code » sont destinés au développement ; choisir l'asset `windows-x64.zip` pour installer.

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
