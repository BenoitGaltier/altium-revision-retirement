# Investigate permission failures before changing the extension

This procedure uses the affected user's existing Altium session. The first stage collects evidence without changing any installed file. The optional native probe temporarily substitutes only this extension's DLL to capture missing API fields; its lifecycle writer is excluded at compilation. Neither stage changes workspace permissions or submits a lifecycle request. Automated engine tests and an SDK build are not evidence that the affected account can use a native API.

## Capture one reproducible case

1. In the affected user's Altium Designer session, confirm the workspace and signed-in Altium account. Use one exact **older Revision ID** that reproduces the problem. Record that ID and the workspace name; exclude the latest revision from the test.
2. Search for that Revision ID in the installed extension. Do not click **Set selected revisions to Obsolete**. Copy the complete Availability message. Use Search rather than only Refresh list so the existing `last-scan.txt` report is regenerated.
3. In native Altium Explorer, locate the **same revision** in its history. Record its current lifecycle state and whether the lifecycle menu offers a direct Obsolete transition, an approval request, or no transition. Open the menu only; do not choose an action that submits a state change. Menu availability is an observation, not proof that the server would accept a transition.
4. Leave Designer open and run the evidence collector from the affected Windows account:

   ```powershell
   .\Collect-AltiumDiagnostics.ps1
   ```

   The collector writes a new local folder and ZIP. It reads installation metadata, relevant process/module paths, registry backups and the existing extension scan report. It does not contact a workspace or execute native permission APIs. The scan report was produced by the previous Search inside Altium, not by the collector.
5. Review and share the generated ZIP with the person investigating the failure, together with the three observations below. Nothing is uploaded automatically.

| Observation | Value to provide |
| --- | --- |
| Workspace and exact older Revision ID | ... |
| Complete extension Availability message | ... |
| Native Explorer lifecycle menu | Direct Obsolete offered / Approval request offered / Neither / Could not inspect |

If more than one Designer installation exists, select the affected `Extensions` directory with `-AltiumExtensionsPath`. The report includes the running executable path; the investigator must confirm it corresponds to that installation. Do not assume the newest installed Designer is the one being used.

## What the evidence can establish

- In version 0.1.9, `Workspace permission denied: Library / Move revision between lifecycle states` is the extension's own ACL-evaluation result. Reaching that branch means its workspace identity check, native revision edit flag and Controlled-mode check passed, and the global ACL was marked read successfully. It does not prove that Altium's server rejected a lifecycle event: no event is sent for this blocked row. Confirm the installed version before relying on this inference.
- Registry and on-disk DLL versions/hashes establish which files are registered and present. Process/module paths and start/write timestamps help identify a different installation or a process started before files changed. The collector **does not inspect managed assembly versions in process memory**, so it does not claim a DLL's on-disk version is necessarily the version executing in the process.
- `last-scan.redacted.txt` retains the reported failure stage and available native error. A source log older than the installed DLL is flagged where possible. Missing logs and files that changed during collection are reported explicitly; they are not treated as a successful permission test.
- A native ACL read refusal, a missing result/status, an unresolved principal/group and a confirmed permission denial must remain separate conclusions. A failed diagnostic read does not establish lack of permission to perform a lifecycle transition.
- Visible menu options do not establish final server authorization. Compare the same workspace, account, revision and transition before drawing a conclusion.

For the global-denial case, a workspace administrator can inspect **Preferences > Data Management > Servers > active workspace Properties > Operations > Library > Move revision between lifecycle states**, without changing anything. Record which configured user/group/Owner/Collaborator grants apply to the affected account. Compare those native settings with the captured error. Do not add privileges merely to make the extension pass.

## Gate for the next code change

First identify the failing native call or interpretation from the captured evidence. If the existing report does not expose enough information, instrument only that call in a clearly identified diagnostic build. Keep the authorization logic unchanged while observing its inputs. The diagnostic build below captures request/result status, ACL entries including both `Allowed` and `AllowPermissions`, native scope/mode, the effective edit flags and group response formats/match counts. It exports the active canonical username and relevant native identifiers, but no group roster or session handle.

## Optional read-only native probe

The diagnostic package is **not a permission fix**. It is a measurement build, version `0.1.9.9000`, for an already installed official 0.1.9 extension. Its only native lifecycle writer throws immediately; the compiled DLL contains no reference to `AddLifeCycleStateChanges`. Both that guard and the original production build are checked before packaging.

1. Extract the full probe ZIP into a new Downloads folder. Do not extract it over an Altium installation. Run `Collect-AltiumDiagnostics.ps1` as above and confirm the report refers to the affected Designer installation.
2. Close Designer normally. Activate the probe using the resulting `diagnostics.json` path:

   ```powershell
   .\Use-PermissionProbe.ps1 -EvidenceReport '.\AltiumDiagnostics-...\diagnostics.json'
   ```

   The script checks the exact installed production DLL hash against the probe manifest, verifies this extension's registered identity/path, backs up that DLL and replaces **only** that DLL. It does not write `ExtensionsRegistry.xml`, an INS file, SDK libraries or another extension. Retain the printed **restore receipt path**. If multiple installations are present, confirm the report selects the one actually used by the affected account. If the package hash/installed version differs or Designer is running, it stops before replacement.
3. Start Designer, sign in as the affected user in the same workspace, and open the usual **Tools > Revision Retirement** command. The title must read **READ-ONLY permission probe 0.1.9.9000**. Enter the exact older Revision ID from the failing case and choose **Capture native responses**. This window has no lifecycle-change action. The report path is printed; failed reads are recorded too. Do not use an administrator account as a substitute for the affected account.
4. Run `Collect-AltiumDiagnostics.ps1` again from that user's Windows session. It includes the latest recognized probe report as `permission-probe.redacted.txt`. Review the generated ZIP and share it with the investigator; nothing is uploaded automatically.
5. Close Designer and restore the original production DLL using the saved receipt:

   ```powershell
   .\Use-PermissionProbe.ps1 -RestoreReceipt 'C:\...\restore-receipt.json'
   ```

   Exact backup/target hashes are checked. An intervening unknown DLL update blocks restoration instead of being overwritten. Restart Designer after restoring. No probe activation is performed automatically by downloading or building the package, and no workstation has been updated as part of preparing it.

Windows may warn about downloaded scripts. Verify the package source, unblock the downloaded ZIP in its Properties before extracting, and run only the included scripts from that extracted folder. If access to the installed DLL is denied, an administrator can perform the temporary swap/restoration; run the actual capture from the affected user's Altium session. No Altium Developer installation or local compilation is needed on the affected PC.

Use the observed, sanitized response as a regression fixture, then verify a diagnostic reread in the affected session. If final server authorization must be tested, use a separately reviewed sandbox with a disposable older revision and a non-administrator account. No production lifecycle write or permission change is part of this collection procedure.
