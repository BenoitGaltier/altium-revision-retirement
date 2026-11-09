# Changelog

## 0.1.6 — 9 November 2026 (preview)

- Open one modeless management window. Running the command again activates the existing window and retains its results.
- Find an item by its exact Revision ID through the native SDK and read all its superseded, non-Obsolete revisions. A populated ID takes precedence over a scan; missing IDs never trigger a fallback scan.
- Add a checked Components folder tree. Folder selections include descendants and constrain both scanning and Revision ID lookup; names are displayed with their full hierarchy.
- Keep the result list and unattempted selections after confirmation cancellation, successful batches, interruptions and errors. Verified Obsolete rows remain visible, unchecked and disabled; uncertain attempts remain visible and blocked.
- Add Refresh list to reread only the displayed items, without rediscovering the entire workspace inventory. Every later batch still performs full eligibility and latest-revision checks.
- Extend isolated safety and UI coverage and update the English guide. Prepare a compiled preview with bundled runtime DLLs; do not deploy this version to the user's Designer installation before approval. Native acceptance of the new lookup and modeless workflow remains pending.

## 0.1.5 — 6 November 2026

- Include the unmodified Altium.SDK.dll and Altium.SDK.Interfaces.dll runtime assemblies in the compiled Windows package, with vendor notices and per-file hashes.
- Remove the target-PC Altium Developer/SDK dependency and custom SDK path option. Installation uses only the bundled payload and preserves other SDK or Altium system libraries.
- Verify first installation without an Altium Developer folder, corrupted or missing bundled DLLs, and independence from unrelated local SDK versions. Add an isolated real-assembly/UI load probe.
- Keep SDK sources, tickets, license credentials and SDK binaries out of source archives and Git history. Runtime redistribution remains governed by the applicable Altium agreement.
- Host the complete runtime bundle in a private repository for authorized internal Fluidra users; keep vendor binaries out of the public source repository and its releases.

## 0.1.4 — 6 November 2026

- Publish a compiled `windows-x64.zip` release with `Install.ps1`. Target PCs do not compile code, open EPD projects or need an IDE or SDK ticket.
- Create or update only this extension's local native registration, preserve other entries, verify installed hashes and retain backups. Restore managed files if deployment fails before registration replacement.
- Check the local proprietary Altium runtime libraries against the build-reference hashes; name missing or incompatible dependencies and explain how to obtain or select them. Libraries and tickets are excluded from releases.
- Add read-only `-CheckOnly`, explicit target/SDK paths, and clear guidance when a source archive is downloaded instead of the compiled installer.
- Fix XML path assignments and native build argument quoting under Windows PowerShell 5.1, including paths with spaces. Keep source-only packaging independent of a native SDK build.
- Add 19 isolated installer and packaging tests, including first installation without compilation, updates, .NET/runtime dependency checks, integrity checks, rollback, registration conflicts and preservation of other extensions. Update the English guides.

## 0.1.3 — 3 November 2026

- Identify Components as the explicit library root and read its complete recursive subtree before scanning items.
- Unfold every nested native SubFolder object and validate its parent chain. Add regressions covering four hierarchy levels and the five-entry SDK response.
- Include older Draft revisions in all three content kinds. Select them according to existing lifecycle transitions and permissions, without requiring a release timestamp.
- Read logical item inventory through the paginated native SDK API and combine flat and embedded revision collections when loading full histories.
- Match native item counts that include deleted tombstones, then exclude inactive items before reading histories or showing candidates. Retain count and page-coverage checks.
- Record the active workspace and complete folder/item/revision totals in local scan diagnostics.
- Keep the completed inventory totals visible in the window and verify a read-only native scan returning old Draft revisions for all three supported content types.
- Use the SDK's seconds-based wait parameter for a 60-second result timeout.

## 0.1.2 — 3 November 2026

- Fix the native scan error `No folders specified`: remove the synthetic empty root folder and query only actual folder GUIDs returned by Altium.
- Move inventory enumeration into a tested coordinator with explicit GUID validation, duplicate detection and complete-response checks. Cross-check zero counts against native item queries and use Altium-formatted uppercase GUIDs at the SDK boundary.
- Translate all controls, columns, status messages, eligibility reasons, errors and confirmation text to English.
- Add local scan diagnostics and regression tests for folder queries and the English interface.
## 0.1.1 — 3 November 2026

- Add the official Fluidra wordmark and an English explanation of workspace scope and latest-revision protection in the main window.
- Add an English user-guide link in the window and English descriptions, help and Fluidra thumbnails in Altium's local extension registration.
- Add a complete English user guide covering installation, control translations, selection, dependency handling, audit journals, troubleshooting, updates and removal.
- Generate EPD installation paths for the correct Altium Extensions folder and keep builds local even after Altium Developer rewrites the project's output path.
- Add a deployment script with runtime hash checks, local backups, a closed-Designer guard and preservation checks for other extension registrations.
- Verify native registration, deployment, loading and window opening with Designer 26.11.1.28. Keep real lifecycle writes subject to the sandbox acceptance procedure.

## 0.1.0 — 3 November 2026

- Initial standalone native extension with component, symbol and footprint revision selection.
- Protect latest revisions, respect existing direct transitions, check native parent references, and journal each attempted and verified transition.
- Add 33 isolated safety tests and Windows CI.
