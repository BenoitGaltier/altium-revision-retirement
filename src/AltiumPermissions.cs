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
            if(output==null || !output.GetSuccess() || records==null || !records.GetSuccess())
                throw new InvalidOperationException("Altium could not read the workspace ACL.");
        }
        static bool RootOperation(IULB_Operation operation)
        {
            var parent=operation.GetParentOperationGUID();Guid id;
            return string.IsNullOrWhiteSpace(parent) || Guid.TryParse(parent,out id) && id==Guid.Empty;
        }
        static bool Named(IULB_Operation operation,string title)
        {
            return new[]{operation.GetHRID(),operation.GetSharedName(),operation.GetDescription()}
                .Any(value=>string.Equals((value??"").Trim(),title,StringComparison.OrdinalIgnoreCase));
        }
        static void AddOperations(IEnumerable<IULB_Operation> source,Dictionary<string,IULB_Operation> all)
        {
            foreach(var operation in source)
            {
                var id=Id.Of(operation.GetGUID());
                if(all.ContainsKey(id)) continue;
                all.Add(id,operation);
                var children=new List<IULB_Operation>();
                for(int i=0;i<operation.GetSubOperationCount();i++) children.Add(operation.GetSubOperation(i));
                AddOperations(children,all);
            }
        }
        static bool UnderLibrary(IULB_Operation operation,Dictionary<string,IULB_Operation> all)
        {
            var seen=new HashSet<string>();
            while(!RootOperation(operation))
            {
                string parent=Id.Of(operation.GetParentOperationGUID());
                if(!seen.Add(parent) || !all.TryGetValue(parent,out operation)) return false;
                if(Named(operation,"Library")) return true;
            }
            return false;
        }
        PermissionRead ReadPermissions(IEDMS_Vault vault)
        {
            var workspace=ActiveWorkspace();
            var read=new PermissionRead {Vault=vault,User=new WorkspacePermissions {WorkspaceKey=workspace.Key,UserGuid=workspace.UserGuid}};
            try
            {
                if(!Id.Same(vault.GetVaultGUID(),workspace.Guid) || !Id.Same(vault.GetUserGUID(),workspace.UserGuid)) return read;
                read.User.Administrator=vault.GetIsAdminUser();
                read.User.OrganizationGuid=vault.GetUserOrganizationGUID();
                read.User.IdentityVerified=ActiveWorkspace().Key==workspace.Key;
                if(!read.User.IdentityVerified || read.User.Administrator) return read;
                var result=vault.GetAllOperations(ReadFlags(),null,null);Check(result);
                var all=new Dictionary<string,IULB_Operation>();
                AddOperations(List<IULB_Operation>(result.GetOperations()),all);
                // Some SDK builds return root operations only, as they do for folders.
                foreach(var root in all.Values.Where(RootOperation).ToList())
                {
                    var nested=vault.GetOperations(Id.Native(root.GetGUID()),"",true,ReadFlags(),null,null);Check(nested);
                    AddOperations(List<IULB_Operation>(nested.GetOperations()),all);
                }
                var matches=all.Values.Where(o=>Named(o,WorkspacePermissionPolicy.OperationName) && UnderLibrary(o,all)).ToList();
                // No translated-name guessing, administrator-group-name guessing, or permissive fallback.
                if(matches.Count==1) read.User.GlobalOperation=ReadPermissionAcl(read,matches[0].GetGUID());
            }
            catch(Exception) { read.User.GlobalOperation=new PermissionList(); }
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
            catch(Exception) { acl=new PermissionList(); }
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
