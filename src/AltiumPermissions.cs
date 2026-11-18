using System;
using System.Collections.Generic;
using System.Linq;
using DXP;

namespace AltiumRevisionRetirement
{
    public sealed partial class AltiumGateway
    {
        sealed class PermissionRead
        {
            public IEDMS_Vault Vault;
            public WorkspacePermissions User;
            public readonly Dictionary<string,PermissionList> Acls = new Dictionary<string,PermissionList>();
        }
        static void Check(IEDM_AsyncSimpleResult result)
        {
            if(result==null || !result.WaitUntilDone(60)) throw new InvalidOperationException("Altium did not return the workspace ACL.");
            var output=result.GetOutput();var records=result.GetResult();
            if(output==null) throw new InvalidOperationException("Altium returned no ACL request status.");
            if(!output.GetSuccess()) throw new InvalidOperationException("Altium rejected the ACL request: "+output.GetMessage());
            if(records==null) throw new InvalidOperationException("Altium returned no ACL result status.");
            if(!records.GetSuccess()) throw new InvalidOperationException("Altium rejected the ACL result: "+records.GetMessage());
        }
        PermissionRead ReadPermissions(IEDMS_Vault vault)
        {
            var workspace=ActiveWorkspace();
            var read=new PermissionRead {Vault=vault,User=new WorkspacePermissions {WorkspaceKey=workspace.Key,UserGuid=workspace.UserGuid}};
            try
            {
                if(!Id.Same(vault.GetVaultGUID(),workspace.Guid) || !Id.Same(vault.GetUserGUID(),workspace.UserGuid)) return read;
                read.User.Administrator=vault.GetIsAdminUser();
                // An unavailable organization is relevant only to an organization grant, not every user.
                try {read.User.OrganizationGuid=vault.GetUserOrganizationGUID();} catch(Exception) {read.User.OrganizationGuid=null;}
                read.User.IdentityVerified=ActiveWorkspace().Key==workspace.Key;
                if(!read.User.IdentityVerified || read.User.Administrator) return read;
                // Read just the native operation's ACL. No enumeration of the administrative operation tree
                // and no dependency on Library or transition display labels in any language.
                read.User.GlobalOperation=WorkspacePermissionPolicy.ReadGlobalOperation(id=>ReadPermissionAcl(read,id));
            }
            catch(Exception error) { read.User.GlobalOperation=new PermissionList {ReadFailure="Workspace permission context: "+PermissionDiagnostics.Summarize(error)}; }
            if(ActiveWorkspace().Key!=workspace.Key) read.User.IdentityVerified=false;
            return read;
        }
        PermissionList ReadPermissionAcl(PermissionRead read,string objectGuid)
        {
            objectGuid=Id.Of(objectGuid);
            PermissionList acl;
            if(read.Acls.TryGetValue(objectGuid,out acl)) return acl;
            acl=new PermissionList();
            try
            {
                var result=read.Vault.GetACLEntries(Id.Native(objectGuid),ReadFlags(),null,null);Check(result);
                var entries=List<IULB_ACLEntry>(result.GetACLEntries());
                foreach(var entry in entries)
                {
                    if(!Id.Same(entry.GetObjectGUID(),objectGuid)) throw new InvalidOperationException("The permission ACL belongs to a different object.");
                    // Operation and transition ACLs use Allowed; content edit access uses the native revision's CanUpdate.
                    var grant=new WorkspaceGrant {Scope=(int)entry.GetUserScopeMode(),PrincipalGuid=entry.GetUserScope(),Allowed=entry.GetAllowed()};
                    acl.Grants.Add(grant);
                    if(grant.Scope==3) ReadGroup(read,grant.PrincipalGuid);
                }
                acl.Read=true;
            }
            catch(Exception error) { acl=new PermissionList {ReadFailure="GetACLEntries: "+PermissionDiagnostics.Summarize(error)}; }
            read.Acls[objectGuid]=acl;
            return acl;
        }
        void ReadGroup(PermissionRead read,string groupGuid)
        {
            groupGuid=Id.Of(groupGuid);
            if(read.User.Groups.ContainsKey(groupGuid)) return;
            var decision=PermissionDecision.Unknown;
            try
            {
                var manager=EDP.Utils.GetDXPServerManager();
                var members=manager.GetUsersInGroup(Id.Native(groupGuid));
                if(members!=null)
                {
                    var username=manager.GetUserNameByGUID(Id.Native(read.User.UserGuid));
                    bool unknown=false;decision=PermissionDecision.Denied;
                    for(int i=0;i<members.GetCount();i++)
                    {
                        var member=(members.GetItem(i)??"").Trim();Guid guid;
                        bool isGuid=Guid.TryParse(member,out guid) && guid!=Guid.Empty;
                        if(isGuid && guid.ToString("D")==Id.Of(read.User.UserGuid) ||
                            !string.IsNullOrWhiteSpace(username) && string.Equals(member,username,StringComparison.OrdinalIgnoreCase))
                        {decision=PermissionDecision.Allowed;break;}
                        // Accept only native GUIDs or canonical usernames. An unknown compound/display format cannot prove non-membership.
                        if(member.Length==0 || !isGuid && (string.IsNullOrWhiteSpace(username) || member.Any(char.IsWhiteSpace) || member.IndexOfAny(new[]{'|',';',',','='})>=0)) unknown=true;
                    }
                    if(decision!=PermissionDecision.Allowed && unknown) decision=PermissionDecision.Unknown;
                }
            }
            catch(Exception) { decision=PermissionDecision.Unknown; }
            read.User.Groups[groupGuid]=decision;
        }
    }
}
