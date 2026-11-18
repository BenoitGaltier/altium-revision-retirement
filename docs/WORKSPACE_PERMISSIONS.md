# Workspace permission checks

The extension uses the user signed into the active Altium workspace. It does not use Windows administrator status, the local installation account, an SDK license, a hardcoded person or a hardcoded group to authorize lifecycle changes.

For a standard user and a Controlled transition, Altium requires all three conditions:

1. Edit rights on the particular item revision.
2. The global **Library / Move revision between lifecycle states** operation permission.
3. Permission for that particular lifecycle transition.

This follows [Altium's lifecycle permission rules](https://www.altium.com/documentation/altium-designer/connected-workspace/defining-lifecycle-definitions) and [global operation permissions](https://www.altium.com/documentation/altium-designer/connected-workspace/setting-global-operation-permissions). A permission on a folder or on the latest revision alone does not prove access to a different revision.

## Native reads

- `GetUserGUID`, `GetUserOrganizationGUID` and `GetIsAdminUser` establish the native workspace subject. The subject is bound to the workspace GUID, service URL and user GUID.
- The selected revision's native `GetCanUpdate()` supplies its effective edit access. The vault-wide `GetCanUpdate()` is deliberately not used as a lifecycle authorization proxy.
- `GetACLEntries` reads the global lifecycle operation directly using its native GUID, `01867B2F-6542-4141-B53C-BAA144DDD628`. Version 0.1.9 removes the 0.1.8 dependency on an administrative operation-tree scan and English display labels. The operation's name and its parent label are not used for authorization.
- `GetACLEntries` reads the configured operation and transition ACLs, using their `Allowed` assignments. Content permission bitmasks are not substituted for operation assignments.
- User and organization grants match native GUIDs. Group grants use native `GetUsersInGroup`, matching a native user GUID or the exact canonical username returned by `GetUserNameByGUID`; display names are not identity evidence. Unrecognized membership formats remain unverified.
- Collaborator grants use effective revision edit access. The Owner entity for library content refers to the creator of the initial item, as described by Altium, rather than the author of the selected revision.

The SDK exposes the transition permission mode as an integer. This adapter handles mode `0` as Controlled and mode `1` as Using Approvals, and blocks unrecognized modes. Confirm the mode and ACL mapping against the native lifecycle editor during acceptance on the target Altium build; unit tests do not establish a vendor API contract.

Workspace administrators are identified by Altium's native administrator flag and have the documented administrative permission override. Latest-revision, lifecycle identity, dependency and audit safeguards still apply. For standard users, Using Approvals is blocked as a direct change and directs the user to request approval in Altium Explorer; the extension does not approve requests.

## Availability messages

| Message | Meaning |
| --- | --- |
| Workspace permissions verified | The configured rights for the active subject permit this direct transition; dependency checks remain. |
| Workspace permission denied: no edit rights | The native revision record reports no effective edit access. |
| Workspace permission denied: Library / ... | The global operation ACL does not authorize this subject. |
| Workspace permission denied: this lifecycle transition ... | The transition ACL does not authorize this subject. |
| Approval required | A standard user must use the native approval workflow. |
| Permissions not verified | The identity, native operation, ACL, membership or permission mode could not be verified. This is distinct from a confirmed refusal and blocks selection. |

If the operation ACL request fails, the message identifies `GetACLEntries` and retains a bounded, sanitized error from Altium. If the ACL was read but a configured principal or group cannot be resolved, a separate message identifies that stage. These blocking reasons are also written to `%LOCALAPPDATA%\AltiumRevisionRetirement\Diagnostics\last-scan.txt` after a completed search/scan. URLs and credential-like values are redacted before native read errors reach these diagnostics. No report is uploaded automatically.

Global configuration and group membership are shared only within one inventory read. A targeted refresh, confirmation preflight and each reread before a write create a fresh permission context. Native ACL calls use force-refresh flags. The final native writer rereads eligibility after the audit intent and the server independently authorizes the actual lifecycle event. A refusal or uncertain response stops the batch; no automatic retry or permission modification is attempted.

## Acceptance status

The global operation identifier was verified against the literal field `Altium.Sdk.DxpAppServer.Common.Constants+VaultConstants+AclOperations.LifecycleStateChangeOperationGuid` in Altium's installed `DXPServerSDK.dll` (assembly version 3.0.0.0, Designer 26.11.1.28). The developer-only `tests/NativePermissionContractTests.ps1 -NativeSdkAssembly <installed-DXPServerSDK.dll>` checks this contract through metadata without invoking vendor code. It requires PowerShell 7 on the development machine; it is never invoked by the installer, and no additional runtime DLL is added to the extension.

The native adapter compiles against the licensed local SDK. Isolated tests cover direct operation ACL lookup, native read failures, credential redaction, grants, refusals, unreadable configuration, approval modes, workspace/user changes and revoked rights between scan and execution. These checks do not establish what permissions a specific colleague currently has or prove the ACL/group and transition-mode integration on another Altium build. Complete the permission matrix in [VALIDATION.md](VALIDATION.md) with disposable revisions before production transitions. No live transition was used to develop this change.
