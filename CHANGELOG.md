# Changelog

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
