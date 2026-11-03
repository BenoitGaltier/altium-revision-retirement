# Changelog

## 0.1.3 — 3 November 2026

- Identify Components as the explicit library root and read its complete recursive subtree before scanning items.
- Unfold every nested native SubFolder object and validate its parent chain. Add regressions covering four hierarchy levels and the five-entry SDK response.
- Include older Draft revisions in all three content kinds. Select them according to existing lifecycle transitions and permissions, without requiring a release timestamp.
- Read logical item inventory through the paginated native SDK API and combine flat and embedded revision collections when loading full histories.
- Match native item counts that include deleted tombstones, then exclude inactive items before reading histories or showing candidates. Retain count and page-coverage checks.
- Record the active workspace and complete folder/item/revision totals in local scan diagnostics.

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
