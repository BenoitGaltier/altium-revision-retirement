# Altium Revision Retirement

Standalone C# extension loaded directly into Altium Designer. It finds superseded **component, schematic symbol and PCB footprint revisions** in the active workspace, lets you select them individually or together, and requests their existing lifecycle transition to **Obsolete**.

**Version 0.1.6 preview.** Adds a modeless window, direct Revision ID lookup, folder filters and retained results. The compiled Windows package includes the two Altium runtime DLLs and is distributed through the [private Fluidra release repository](https://github.com/BenoitGaltier/altium-revision-retirement-fluidra-releases/releases). Target PCs need Altium Designer and .NET Framework 4.8; Altium Developer, an SDK, compiler, IDE and EPD project are not required. The new workflow has isolated test coverage and a native SDK build; deployment and native acceptance of this preview are pending. The earlier read-only scanning workflow was verified with Designer 26.11.1.28 / C# SDK 4.0 / .NET Framework 4.8.

## Install the compiled release

Authorized Fluidra users can download **`altium-revision-retirement-0.1.6-windows-x64.zip`** from its private preview release assets, extract it, close Altium Designer, and run this command in the extracted folder. Version 0.1.5 remains the latest non-preview release:

```powershell
.\Install.ps1
```

The installer validates the extension, `Altium.SDK.dll` and `Altium.SDK.Interfaces.dll` bundled in the release, then copies them into this extension's own directory. No local SDK is consulted or downloaded. Vendor notices are included; SDK sources, tickets and license credentials are excluded. Source archives do not include SDK binaries.

It creates or updates only this extension's local native registration, backs up its managed files and registry, and preserves other extension entries. Restart Designer and use **Tools → Revision Retirement…** in a schematic/PCB editor. The fallback is **Run Process** with **`AltiumRevisionRetirement:Manage`**.

Use `Install.ps1 -CheckOnly` to verify prerequisites and bundled files without changing installed files. See the [English installation instructions](docs/INSTALLATION.md) for downloaded-script warnings, selecting another Altium instance and backups; the [user guide](docs/USER_GUIDE.md) covers operation and removal.

GitHub's automatic **Source code** downloads contain the development project, not the compiled installer. Developers can run `scripts/package.ps1` with a licensed local SDK to build the compiled release and source ZIP, or `scripts/package.ps1 -SourceOnly` to create a source archive without a native SDK build. `scripts/build.ps1` remains a developer command and supports `-AltiumSdkRoot` for a custom SDK path. Installation never invokes it.

![Offscreen UI preview with sample data, not a live workspace](docs/ui-preview.png)

Folder scanning starts at the workspace's **Components** root and loads its complete recursive folder tree before reading items in the selected branches. An exact Revision ID bypasses that inventory and reads the matching item's history directly. Older `Draft` revisions are included; selection depends on permissions and the existing direct transition to Obsolete, without requiring a release date.

## Use

1. Connect to the intended workspace in Designer and open the modeless extension window. You can continue using Designer while it remains open; running the command again returns to the same window.
2. Optionally choose **Folders…**, then enter an exact **Revision ID** and click **Search** to list all older non-Obsolete revisions of that item. Leave the field blank and choose **Scan** to inventory the selected folders instead. Folder selection includes subfolders and applies to both modes. Insufficient permissions and missing/ambiguous direct transitions remain visible with a blocking reason.
3. Select individual rows or **Select all** (all rows with a valid direct transition). The latest revision of every item is excluded even if hidden, planned or already Obsolete.
4. Choose **Set selected revisions to Obsolete…**. The extension rereads the selected items and dependencies. A referenced symbol/footprint cannot be retired while a parent revision remains non-Obsolete. Select an eligible old parent revision as well, or leave the model unchanged. A latest parent is never added automatically.
5. Review the ordered plan, enter a mandatory lifecycle change note, and confirm. Parents are processed before their selected models. There is no cascade or automatic multi-step lifecycle route.
6. Continue from the retained list. Verified Obsolete rows stay visible and cannot be selected again; unattempted rows keep their selection. **Refresh list** rereads only displayed items. An uncertain attempt remains blocked until you review the Altium timeline and refresh. **Stop** stops between operations; it does not undo successful transitions.

A flushed CSV journal is written to `%LOCALAPPDATA%\AltiumRevisionRetirement\Audit`. Each attempted change has an `intent` entry before the SDK write and a `verified` entry after a fresh read. If a response is lost, the batch stops without retrying. Check the revision's native Altium timeline before any manual retry.

## Development

```powershell
.\scripts\test.ps1      # safety engine, no Altium login or SDK needed
.\scripts\test-ui.ps1   # English interface, isolated sample data
.\tests\InstallerTests.ps1 # isolated compiled-installer and packaging tests
.\scripts\build.ps1     # full native extension, licensed local SDK required
.\scripts\package.ps1 -SkipBuild # compiled Windows ZIP + source ZIP + SHA-256 files
```

GitHub Actions runs the safety, folder-inventory, English-interface and installer tests on Windows. It does not compile the licensed native adapter or exercise a connected workspace. `src/AltiumGateway.cs` owns all SDK access; `src/RetirementEngine.cs` owns eligibility, dependency order and latest-revision guards. `src/RetirementForm.cs` owns selection and the confirmation dialog.
