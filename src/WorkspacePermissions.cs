using System;
using System.Collections.Generic;

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
                "Permissions not verified: cannot resolve Library / "+OperationName+" for the active user.");
            if(reason!=null) return reason;
            return Failure(Evaluate(transition.Permissions,user,item.OwnerGuid,revision.CanUpdate),
                "Workspace permission denied: this lifecycle transition does not allow the active user.",
                "Permissions not verified: the lifecycle transition ACL or group membership could not be read.");
        }
    }
}
