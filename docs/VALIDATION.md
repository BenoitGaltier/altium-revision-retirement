# Acceptance procedure — isolated workspace only

## Verified during development

- Full Release build against local Designer Agile 26.11.1.28 / SDK 4.0 / .NET Framework 4.8, with warnings treated as errors.
- 33 isolated safety-engine tests: latest GUID handling, arbitrary revision labels, hidden/single revisions, lifecycle ambiguity, permissions, stale snapshots, workspace/user switches, dependency ordering and cycles, audit failures, interrupted/lost responses, postconditions and cancellation.
- No live workspace lifecycle transition was executed. Installation, real inventory completeness, ACL enforcement and native lifecycle rule validation remain pending.

## Required before production use

Use a disposable workspace or test folder with components and models created specifically for this validation. Record all item/revision GUIDs and lifecycle states in Explorer before starting. Test the same Altium version and workspace plan used in production.

1. Build, open the generated `.local.EPD`, install through Altium Developer and restart. Verify that `AltiumRevisionRetirement:Manage` opens and closes without creating a server/listener or modifying workspace data.
2. Create components, symbols and footprints with one revision and with at least three revisions. Give revision labels that do not sort lexically (for example 9 and 10, or structured schemes). Include hidden/inapplicable latest revisions and an unreleased/planned latest revision.
3. Compare every row to Explorer's full revision history. Verify latest means the true newest revision, including the planned/hidden cases. A single-revision item must not appear. An already Obsolete old revision must not appear.
4. Check older revisions with no Obsolete state, an ambiguous Obsolete name, no direct transition, an unreleased status and insufficient permissions. They must be listed with a blocking reason and must not be selectable.
5. Create a latest component that references an old symbol or footprint. Selecting that model must be blocked. Then create an eligible old parent referencing an old model; explicitly selecting both must produce a parent-before-model plan. An unselected non-Obsolete parent must still block it.
6. Switch the active workspace/user after scan or after preview. The operation must stop without applying the stale plan to either workspace.
7. With a role allowed to read but forbidden to perform the transition, verify the native server rejects the event. Verify a lifecycle rule that the native Explorer validation rejects is also rejected. Do not proceed if the SDK call bypasses a rule required by your organization.
8. On one disposable eligible revision, confirm a transition with a test note. Compare the native history note, the revision GUID, all other revision states, latest state and stored model/component content before and after. Only the chosen old revision's lifecycle event should change.
9. Verify a two-item batch, user cancellation between items, a server refusal, and network interruption. No uncertain request may be retried automatically. Check the CSV `intent` and `verified` events against native history.
10. Rescan after completion. Treated revisions should disappear. Check both old placed design instances and new-component placement for the expected lifecycle/applicability behavior.

Record the tested Designer build, workspace GUID, test role, lifecycle definition GUIDs, scan counts, before/after exports and conclusions in an internal report. Keep live audit reports, credentials and proprietary component data out of this repository. Stop deployment if any test disagrees with Explorer or the organization's lifecycle rules.
