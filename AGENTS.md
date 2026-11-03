# Development workflow

- Keep this extension standalone. Do not add a bridge dependency, local server, external account credentials or automatic Altium writes at startup.
- Do not execute lifecycle transitions on a real workspace as part of automated tests. Use a fake gateway; require a separately reviewed sandbox acceptance run for native integration.
- Keep the only mutation inside `AltiumGateway.Transition`, using a singleton native lifecycle event. Never add direct database writes, item deletion, revision payload updates, permission escalation or automatic retries.
- Protect the native latest revision by GUID and retain the identity, dependency, audit and postcondition checks.
- Run `scripts/test.ps1` and `scripts/build.ps1` for behavioral changes. The licensed local SDK is required for the native build. CI only covers isolated tests.
- Never commit SDK binaries/tickets, credentials, generated local EPD files or live workspace reports.
