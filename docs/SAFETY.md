# Safety design

## Native boundary

The only writer is `AltiumGateway.Transition`. It constructs one native `IULB_LifeCycleStateChange` and submits a singleton list through `IEDMS_Vault.AddLifeCycleStateChanges`. The request contains the existing transition GUID, its existing destination state GUID, the selected revision GUID and the user's note. No setter on the item's stored lifecycle field, database query, content mutation, definition update or arbitrary DXP process is exposed.

Read operations use `edmsForceRefresh`; the revision read requests `eGetOption_GetAllItemRevisions`, `eGetOption_IncludeHiddenRevisions`, `eGetOption_ExcludeDeleted` and `eGetOption_ExcludeParameters`. Item inventory is read per folder and supported content type with before/after item-count comparisons. A truncated or inconsistent folder response aborts the scan instead of enabling a partial result. Only accessible, non-deleted content can be enumerated; this is not an administrator-wide database dump or a transactional workspace snapshot.

The active workspace comes from `EDP.Utils.GetDXPServerManager().GetEnterpriseVault()`, with a connected-server check. No first-installed-vault fallback or external credential storage exists. Workspace GUID, service URL and user GUID form the captured identity.

## Preconditions

1. All GUIDs are well formed, revisions are unique, belong to their parent item, and the native latest GUID belongs to the returned full history.
2. The content type is exactly Component, Symbol or PcbComponent (footprint).
3. The candidate is not the native latest revision, is published, is writable, and is not already named Obsolete.
4. Exactly one state named Obsolete exists in the item's lifecycle definition; exactly one direct transition links the current state to it in that same definition. Multi-step routes and ambiguous states are blocked.
5. All non-Obsolete referencing parent revisions are explicitly selected and eligible, or the operation is blocked. A topological sort processes them first; cycles are blocked.
6. Before each individual write, the snapshot and dependency checks are repeated. Dependencies must now actually be Obsolete. An intent record is flushed to disk. The native writer rereads eligibility and identity once more before submission.

The server still decides whether the current principal may perform the native transition. No permissions, definitions or administrative settings are changed by the extension. Actual server-side lifecycle validation behavior must be checked in a sandbox installation; compilation alone is not evidence of full equivalence to every Explorer validation workflow.

## Postconditions and interruption

An accepted response must identify exactly one requested revision and target state. A force-refreshed read must show the selected target state. Every other revision present in the before-snapshot, including the then-latest revision, must still exist with its original state. The selected revision must still be non-latest and the workspace/user must be unchanged. A verified journal record is then flushed.

A refusal, timeout, missing acknowledgment, changed identity, failed audit write or failed postcondition stops the batch. There is no automatic retry and no rollback transition. A response may have been lost after a successful server commit; an `intent` without `verified` requires reviewing the native revision timeline. A manually initiated new scan/confirmation is a new user action, not automatic recovery.

The cancel button is processed between native operations, never by aborting an in-flight server mutation. All SDK calls remain on Altium's UI thread. The UI disables scan, selection, apply and closing during work to prevent reentrancy; it pumps messages at progress boundaries, after which identities and snapshots are checked again.

## Limits requiring operational validation

- No server-side atomic compare-and-swap or lock covering revision deletion/restoration and transition is exposed by the SDK interface used. Another client can race after the final read. Prevent concurrent lifecycle-definition edits, revision deletion/restoration and dependency edits for the affected items during execution.
- `GetLastItemRevision` and the all/hidden-revision response must be compared with Explorer's complete history in the target Designer/workspace version, including planned and inapplicable latest revisions.
- Revision links can be filtered by access rights. Parent links outside what the server returns cannot be discovered by this client. Cross-workspace or unreadable parents that are returned are blocked.
- Historical project usage, external documents and non-native model references may not be represented by the revision-link API. Obsolescence changes applicability warnings in designs that still use those old revisions; it does not update designs to a newer revision.
- The native SDK is proprietary and version-dependent. SDK symbols were compiled locally, but the full adapter and EPD have not yet been acceptance-tested in a running Designer session.
- Batches contain separate lifecycle events. Successfully verified events remain if a later one fails. Server audit records and the local journal preserve the per-revision trail.

Sources: [Altium lifecycle management](https://www.altium.com/documentation/altium-designer/connected-workspace/items/managing-revision-lifecycle), [Altium items and revision notes](https://www.altium.com/documentation/altium-designer/connected-workspace/items), [Altium Developer installation](https://www.altium.com/documentation/altium-dxp-developer). The implementation binds to the licensed, locally installed SDK interfaces; SDK files are not redistributed here.
