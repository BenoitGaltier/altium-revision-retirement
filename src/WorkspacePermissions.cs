using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AltiumRevisionRetirement
{
    public enum PermissionDecision { Unknown, Allowed, Denied }
    public sealed class WorkspaceGrant
    {
        // Native TEDM_ACLUserScopeMode: User, Organization, All, Group, Owner, Collaborator.
        public int Scope;
        public string PrincipalGuid;
        public bool Allowed;
    }
    public sealed class PermissionList
    {
        public bool Read;
        public string ReadFailure;
        public readonly List<WorkspaceGrant> Grants = new List<WorkspaceGrant>();
    }
    public sealed class WorkspacePermissions
    {
        public string WorkspaceKey, UserGuid, OrganizationGuid;
        public bool IdentityVerified, Administrator;
        public PermissionList GlobalOperation = new PermissionList();
        public readonly Dictionary<string,PermissionDecision> Groups = new Dictionary<string,PermissionDecision>();
    }
    public static class WorkspacePermissionPolicy
    {
        public const string OperationName = "Move revision between lifecycle states";
        // Altium.Sdk.DxpAppServer.Common.Constants.VaultConstants.AclOperations.LifecycleStateChangeOperationGuid.
        // Verified in the installed vendor DXPServerSDK metadata; never derived from a localized display name.
        public const string LifecycleOperationGuid = "01867B2F-6542-4141-B53C-BAA144DDD628";
        public static PermissionList ReadGlobalOperation(Func<string,PermissionList> readAcl)
        {
            try
            {
                var result=readAcl(LifecycleOperationGuid);
                return result ?? new PermissionList {ReadFailure="GetACLEntries: Altium returned no lifecycle operation permission list."};
            }
            catch(Exception error)
            {return new PermissionList {ReadFailure="GetACLEntries: "+PermissionDiagnostics.Summarize(error)};}
        }
        static PermissionDecision SamePrincipal(string a,string b)
        {
            Guid first, second;
            if(!Guid.TryParse(a,out first) || first==Guid.Empty || !Guid.TryParse(b,out second) || second==Guid.Empty)
                return PermissionDecision.Unknown;
            return first==second ? PermissionDecision.Allowed : PermissionDecision.Denied;
        }
        static PermissionDecision Matches(WorkspaceGrant grant,WorkspacePermissions user,string ownerGuid,bool canEdit)
        {
            switch(grant.Scope)
            {
                case 0:return SamePrincipal(grant.PrincipalGuid,user.UserGuid);
                case 1:return SamePrincipal(grant.PrincipalGuid,user.OrganizationGuid);
                case 2:return PermissionDecision.Allowed;
                case 3:
                    Guid group;
                    PermissionDecision member;
                    return Guid.TryParse(grant.PrincipalGuid,out group) && group!=Guid.Empty && user.Groups.TryGetValue(group.ToString("D"),out member) ? member : PermissionDecision.Unknown;
                case 4:return SamePrincipal(ownerGuid,user.UserGuid);
                case 5:return canEdit ? PermissionDecision.Allowed : PermissionDecision.Denied;
                default:return PermissionDecision.Unknown;
            }
        }
        public static PermissionDecision Evaluate(PermissionList acl,WorkspacePermissions user,string ownerGuid,bool canEdit)
        {
            if(acl==null || !acl.Read) return PermissionDecision.Unknown;
            bool allow=false,unknown=false,unknownDenial=false;
            foreach(var grant in acl.Grants)
            {
                var applies=Matches(grant,user,ownerGuid,canEdit);
                if(applies==PermissionDecision.Unknown) {unknown=true;if(!grant.Allowed) unknownDenial=true;continue;}
                if(applies!=PermissionDecision.Allowed) continue;
                if(!grant.Allowed) return PermissionDecision.Denied;
                allow=true;
            }
            // An unresolved positive grant cannot invalidate a known grant; an unresolved denial can.
            return unknownDenial ? PermissionDecision.Unknown : allow ? PermissionDecision.Allowed : unknown ? PermissionDecision.Unknown : PermissionDecision.Denied;
        }
        static string Failure(PermissionDecision decision,string denied,string unknown)
        { return decision==PermissionDecision.Allowed ? null : decision==PermissionDecision.Denied ? denied : unknown; }
        static string ReadFailure(PermissionList acl)
        { return acl!=null && !string.IsNullOrWhiteSpace(acl.ReadFailure) ? " "+acl.ReadFailure : ""; }
        public static string Reason(Workspace workspace,Item item,Revision revision,Transition transition)
        {
            var user=item.Permissions;
            if(user==null || !user.IdentityVerified || user.WorkspaceKey!=workspace.Key || SamePrincipal(user.UserGuid,workspace.UserGuid)!=PermissionDecision.Allowed)
                return "Permissions not verified: the active workspace user could not be confirmed. Refresh the list.";
            if(user.Administrator) return null; // Workspace administrator, never Windows administrator.
            if(transition.PermissionType==1) return "Approval required: request this transition in Altium Explorer.";
            if(transition.PermissionType!=0) return "Permissions not verified: unsupported state transition permission type.";
            if(!revision.CanUpdate) return "Workspace permission denied: no edit rights on this item revision.";
            var reason=Failure(Evaluate(user.GlobalOperation,user,item.OwnerGuid,revision.CanUpdate),
                "Workspace permission denied: Library / "+OperationName+".",
                user.GlobalOperation!=null && user.GlobalOperation.Read ?
                    "Permissions not verified: the global lifecycle ACL was read, but a principal or group membership could not be verified." :
                    "Permissions not verified: the workspace's lifecycle operation ACL could not be read."+ReadFailure(user.GlobalOperation));
            if(reason!=null) return reason;
            return Failure(Evaluate(transition.Permissions,user,item.OwnerGuid,revision.CanUpdate),
                "Workspace permission denied: this lifecycle transition does not allow the active user.",
                "Permissions not verified: the lifecycle transition ACL or group membership could not be read."+ReadFailure(transition.Permissions));
        }
    }
    public static class PermissionDiagnostics
    {
        public static string Summarize(Exception error)
        {
            var message=error.Message??"";
            message=Regex.Replace(message,@"https?://[^\s""'<>]+","[URL]",RegexOptions.IgnoreCase);
            message=Regex.Replace(message,@"\bBearer\s+[^\s""',;]+","Bearer [redacted]",RegexOptions.IgnoreCase);
            message=Regex.Replace(message,@"\b(?:password|pwd|token|[a-z_]*session[a-z_]*|ticket|authorization)\b[""']?\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s;,}]+)","[credential redacted]",RegexOptions.IgnoreCase);
            message=Regex.Replace(message,@"\s+"," ").Trim();
            if(message.Length>350) message=message.Substring(0,350)+"...";
            return error.GetType().Name+": "+message;
        }
    }
}
