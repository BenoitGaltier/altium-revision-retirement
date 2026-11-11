# Vault Explorer startup diagnostics

If Explorer worked before installing Revision Retirement and Altium now reports "Could not start Vault Explorer", collect the before/after evidence before changing the installation again.

Extract `AltiumRevisionRetirement-Diagnostics.zip` into a new folder. Run in PowerShell:

```powershell
.\Collect-AltiumDiagnostics.ps1
```

This collects the current `ExtensionsRegistry.xml`, up to three registry backups created by the installer, and a JSON report with registration status, file presence, assembly metadata and hashes. It writes a local diagnostics folder and ZIP in the current directory. It does not alter the Altium installation, load DLLs or contact a workspace. Send the generated diagnostics ZIP to the person investigating the issue; nothing is uploaded automatically.

For multiple Designer installations, select the affected existing directory explicitly:

```powershell
.\Collect-AltiumDiagnostics.ps1 -AltiumExtensionsPath 'C:\ProgramData\Altium\Altium Designer {GUID}\Extensions'
```

To isolate a regression, use Altium's **Extensions and Updates > Installed** page to uninstall only **Altium Revision Retirement**, then restart Designer and check Explorer again. Preserve the diagnostic ZIP and installer backups. Do not overwrite the shared registry with an arbitrary backup or reinstall unrelated extensions while the cause is unknown.

If Revision Retirement has already been removed and the warning persists, collect the diagnostics from that current state. The report records running X2 processes, relevant loaded module paths and the registry's last-write time, and retains raw XML evidence even if parsing fails. Keep the preinstallation backups for comparison.

Altium documents the same warning when its native Explorer extension is absent: [Altium knowledge base](https://www.altium.com/documentation/knowledge-base/altium-designer/a-warning-message-could-not-start-vault-explorer-upon-starting-altium-designer). If Explorer previously worked, compare registration and DLL evidence to distinguish a missing extension from a loading conflict. The screenshot alone does not establish which cause applies.
