# Altium Revision Retirement — User Guide

Review older workspace revisions and move selected ones to **Obsolete**, while keeping the latest revision of every item protected.

The extension runs directly inside Altium Designer. It uses the active Altium 365 workspace and the permissions of the signed-in user. It covers components, schematic symbols and PCB footprints. The Fluidra header explains this scope when the window opens; no analysis or lifecycle change starts automatically.

## Installation

Requirements: Windows x64, Altium Designer, the licensed **Altium Developer** extension with C# SDK 4.0, and .NET Framework 4.8.

1. Clone this repository and run the following commands in its root directory:

   ```powershell
   .\scripts\test.ps1
   .\scripts\build.ps1
   ```

2. In Altium Designer, open **`AltiumRevisionRetirement.local.EPD`**. The build generates this file with the correct local source, SDK, icon and installation paths. Do not open the tracked EPD template directly.
3. In Altium Developer, choose **Install Extension**. Opening the EPD may already create the local registration; in that case the command becomes **Update Local Installation**. This step registers the extension and installs its INS/RCS resources.
4. Close Altium Designer normally. Save or cancel your own document changes as appropriate. The installer never closes documents or terminates Altium.
5. Run:

   ```powershell
   .\scripts\build.ps1
   .\scripts\install.ps1
   ```

   The build keeps its output in `bin\Release`, even if Altium Developer changed the project output path. The install script copies the runtime files into the registered extension directory, verifies their SHA-256 hashes, and adds the English description, help link and Fluidra thumbnails to this extension's local registration. It saves a backup and verifies that the other extension registrations are unchanged. Access to Altium's ProgramData directory may require an elevated PowerShell session, depending on local permissions.

6. Start Altium Designer. In **Extensions and Updates → Installed**, find **Altium Revision Retirement** and check its description and Fluidra logo.
7. Open **Revision Retirement…** from the DXP menu, or the Tools menu in a schematic/PCB editor. If a customized menu hides the entry, run **`AltiumRevisionRetirement:Manage`** through **Run Process**.

The extension's runtime files are stored in the Altium installation's `Extensions\AltiumRevisionRetirement` directory. The SDK DLLs are copied only from your licensed local installation. SDK binaries and the SDK ticket are not stored in GitHub.

## Window and controls

The interface controls remain in French. The product description and this guide are in English.

| Control | Meaning |
| --- | --- |
| **Analyser** | Scan the active workspace. This is read-only. |
| **Choisir** | Select one old revision. |
| **Tout sélectionner** | Select all listed revisions with a valid direct transition. |
| **Tout désélectionner** | Clear the selection. |
| **Ancienne révision** | The superseded revision that may be changed. |
| **État actuel** | Its current lifecycle state. |
| **Dernière protégée** | The latest revision, which is excluded from the operation. |
| **Disponibilité** | Eligibility or the reason a row is blocked. |
| **Passer la sélection à Obsolete…** | Validate the selection and review the proposed transitions. |
| **Arrêter** | Stop between operations. Completed transitions remain applied. |
| **User guide (English)** | Open this guide. |

![Extension preview with sample data](ui-preview.png)

## Review and retire revisions

1. Connect to the intended workspace in Designer. Open the extension and check the workspace name and URL.
2. Click **Analyser**. The scan lists old revisions whose state is not named `Obsolete`. Single-revision items and the latest revision of every item are excluded. Hidden latest revisions are also protected.
3. Review the item, revision, current state and protected latest revision. A blocked row stays visible so you can see why it cannot be changed. Typical reasons include missing write permissions, an unpublished/planned revision, an absent or ambiguous Obsolete state, or no direct lifecycle transition.
4. Select the revisions to retire. **Tout sélectionner** selects eligible rows; it does not bypass the dependency checks.
5. Click **Passer la sélection à Obsolete…**. The extension rereads the items and their parent references. If a symbol or footprint is still referenced by a non-Obsolete parent, the operation stops unless that old parent is also explicitly selected and eligible. A latest parent is never added to the selection.
6. Review the ordered plan. Selected parents appear before their models. Enter a short note explaining the change, then click **Confirmer**. Closing or cancelling this dialog applies no transition.
7. Review the result and run another scan to refresh the list.

For example, if component revision `COMP-001-02` is the latest and still uses `SYM-001-01`, that symbol revision is protected by the parent reference. The extension does not retire the latest component or replace its symbol automatically. If an eligible old component revision is the only non-Obsolete parent of an old symbol, explicitly selecting both allows the component to be processed first.

## Journal and interrupted operations

The local CSV journal is stored at:

```text
%LOCALAPPDATA%\AltiumRevisionRetirement\Audit
```

An **intent** event is flushed to disk before each native transition. A **verified** event is written after a fresh read confirms the target state and the unchanged states of the other revisions. The note is also submitted to Altium with the lifecycle event.

The batch stops on the first rejected or uncertain operation. It does not retry automatically and does not undo successful changes. If an intent has no verified event, check the revision's native Altium timeline before deciding whether to retry. A lost response can occur after the server has already accepted a change.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Extension is installed but no menu appears | Restart Designer, check the installed extension, then use Run Process with `AltiumRevisionRetirement:Manage`. |
| Window opens but the scan cannot connect | Sign in to the intended workspace in Designer and rescan. The extension does not fall back to a different workspace. |
| Workspace or user changed after the scan | Run a new scan. The previous selection is deliberately rejected. |
| No direct transition to Obsolete | Ask the lifecycle owner to review the definition. The extension does not create transitions or follow an automatic multi-step route. |
| Model is used by a non-Obsolete parent | Keep the model, or explicitly select an eligible old parent too. A latest parent remains protected. |
| Inventory is incomplete or inconsistent | Stop and rescan. The extension refuses truncated or inconsistent results. |
| Installer says Altium is still running | Close Designer normally before updating; do not force-terminate it. |
| Source project changed after opening the EPD | Altium Developer may rewrite its output path. The build script overrides that path so runtime output still goes into `bin\Release`. |
| Description or logo is missing from Installed extensions | Close Designer, rerun the build and install scripts, and restart. The script fills only this extension's display metadata. |

## Updates and removal

For an update, close Designer, pull the new source, run the tests, build and install scripts, then restart. Local runtime backups and installation receipts are kept under `.artifacts\installation` in the source directory; these machine-specific files are excluded from Git.

Remove the extension through **Extensions and Updates → Installed**, then restart Designer. Removing the extension does not reverse lifecycle transitions already accepted by the workspace.

## Validation and limits

Installation and opening the window are separate from validating lifecycle writes. Use the [sandbox acceptance procedure](VALIDATION.md) before a production batch. The [safety design](SAFETY.md) explains identity checks, native API calls, dependency checks and audit behavior.

The SDK interface used here does not expose an atomic operation combining “still an old revision” with the transition. Do not delete/restore affected revisions, change their lifecycle definitions or edit their dependencies concurrently with a batch. Visibility is limited to what the signed-in user and the native API can access. Existing projects keep their old revision links, but can show applicability warnings after those revisions become Obsolete.
