using System;
using AltiumRevisionRetirement;

static class WorkspacePermissionTests
{
    public static void Run(Action<string,Action> test)
    {
        var workspace=new Workspace {Guid=Guid.NewGuid().ToString(),UserGuid=Guid.NewGuid().ToString(),Url="https://example.invalid"};
        Func<Item> fixture=()=>{
            var item=new Item {OwnerGuid=workspace.UserGuid};item.Transitions.Add(new Transition());
            PermissionFixtures.Bind(item,workspace);return item;
        };
        var revision=new Revision {CanUpdate=true};
        Func<Item,string> reason=item=>WorkspacePermissionPolicy.Reason(workspace,item,revision,item.Transitions[0]);
        test("global ACL lookup uses the vendor lifecycle operation GUID directly",()=>{
            var i=fixture();int calls=0;
            i.Permissions.GlobalOperation=WorkspacePermissionPolicy.ReadGlobalOperation(id=>{
                calls++;Assert(Guid.Parse(id)==new Guid("01867B2F-6542-4141-B53C-BAA144DDD628"));
                return PermissionFixtures.Acl(PermissionFixtures.Grant(5));
            });
            Assert(calls==1&&reason(i)==null);
        });
        test("a missing native operation ACL response cannot become an allowance",()=>{
            var i=fixture();i.Permissions.GlobalOperation=WorkspacePermissionPolicy.ReadGlobalOperation(id=>null);
            Assert(reason(i).StartsWith("Permissions not verified")&&reason(i).Contains("GetACLEntries"));
        });
        test("native operation ACL errors retain their cause and remain blocked",()=>{
            var i=fixture();i.Permissions.GlobalOperation=WorkspacePermissionPolicy.ReadGlobalOperation(id=>{throw new InvalidOperationException("ACL request rejected: read access denied");});
            Assert(reason(i).StartsWith("Permissions not verified")&&reason(i).Contains("read access denied"));
        });
        test("read failure is distinct from unresolved membership after a successful ACL read",()=>{
            var i=fixture();i.Permissions.GlobalOperation=PermissionFixtures.Acl(PermissionFixtures.Grant(3,Guid.NewGuid().ToString()));
            Assert(reason(i).Contains("ACL was read")&&reason(i).Contains("membership"));
        });
        test("direct native operation lookup preserves a configured refusal",()=>{
            var i=fixture();i.Permissions.GlobalOperation=WorkspacePermissionPolicy.ReadGlobalOperation(id=>PermissionFixtures.Acl());
            Assert(reason(i).StartsWith("Workspace permission denied"));
        });
        test("operation read diagnostics redact credentials and URL query values",()=>{
            var detail=PermissionDiagnostics.Summarize(new InvalidOperationException("ACL denied\r\nPortalSessionHandle=topsecret password='hidden password' token=private Authorization: Bearer abc123 https://workspace.invalid/service?ticket=secret"));
            foreach(var secret in new[]{"topsecret","hidden password","private","abc123","workspace.invalid","ticket=secret"}) Assert(!detail.Contains(secret));
            Assert(detail.Contains("ACL denied")&&!detail.Contains("\r")&&!detail.Contains("\n"));
        });
        test("native error diagnostics are bounded",()=>Assert(PermissionDiagnostics.Summarize(new Exception(new string('x',2000))).Length<400));
        test("missing organization identity does not invalidate a collaborator grant",()=>{var i=fixture();i.Permissions.OrganizationGuid=null;Assert(reason(i)==null);});
        test("workspace collaborator and Anyone allow a standard user",()=>Assert(reason(fixture())==null));
        test("missing native revision edit right blocks the transition",()=>{revision.CanUpdate=false;try{Assert(reason(fixture()).Contains("no edit rights"));}finally{revision.CanUpdate=true;}});
        test("empty global operation ACL denies",()=>{var i=fixture();i.Permissions.GlobalOperation=PermissionFixtures.Acl();Assert(reason(i).Contains("Library /"));});
        test("unreadable global operation is unverified, not denied",()=>{var i=fixture();i.Permissions.GlobalOperation=new PermissionList();Assert(reason(i).StartsWith("Permissions not verified"));});
        test("unknown operation is never replaced by generic vault write capability",()=>{var i=fixture();i.Permissions.GlobalOperation=null;Assert(reason(i).StartsWith("Permissions not verified"));});
        test("specific transition user grant matches active workspace user",()=>{var i=fixture();i.Transitions[0].Permissions=PermissionFixtures.Acl(PermissionFixtures.Grant(0,workspace.UserGuid));Assert(reason(i)==null);});
        test("another user's local transition grant denies",()=>{var i=fixture();i.Transitions[0].Permissions=PermissionFixtures.Acl(PermissionFixtures.Grant(0,Guid.NewGuid().ToString()));Assert(reason(i).Contains("lifecycle transition"));});
        test("verified group membership permits the configured operation",()=>{var i=fixture();var g=Guid.NewGuid().ToString("D");i.Permissions.GlobalOperation=PermissionFixtures.Acl(PermissionFixtures.Grant(3,g));i.Permissions.Groups[g]=PermissionDecision.Allowed;Assert(reason(i)==null);});
        test("verified non-member has no operation grant",()=>{var i=fixture();var g=Guid.NewGuid().ToString("D");i.Permissions.GlobalOperation=PermissionFixtures.Acl(PermissionFixtures.Grant(3,g));i.Permissions.Groups[g]=PermissionDecision.Denied;Assert(reason(i).StartsWith("Workspace permission denied"));});
        test("unreadable group membership is unknown",()=>{var i=fixture();i.Transitions[0].Permissions=PermissionFixtures.Acl(PermissionFixtures.Grant(3,Guid.NewGuid().ToString()));Assert(reason(i).StartsWith("Permissions not verified"));});
        test("unresolved additional positive grant does not cancel collaborator grant",()=>{var i=fixture();i.Permissions.GlobalOperation.Grants.Add(PermissionFixtures.Grant(3,Guid.NewGuid().ToString()));Assert(reason(i)==null);});
        test("owner is the creator of the initial item, not the selected revision",()=>{var i=fixture();i.Permissions.GlobalOperation=PermissionFixtures.Acl(PermissionFixtures.Grant(4));Assert(reason(i)==null);i.OwnerGuid=Guid.NewGuid().ToString();Assert(reason(i).StartsWith("Workspace permission denied"));});
        test("missing owner evidence stays unverified",()=>{var i=fixture();i.Permissions.GlobalOperation=PermissionFixtures.Acl(PermissionFixtures.Grant(4));i.OwnerGuid=null;Assert(reason(i).StartsWith("Permissions not verified"));});
        test("organization principal uses native workspace organization GUID",()=>{var i=fixture();var g=Guid.NewGuid().ToString();i.Permissions.OrganizationGuid=g;i.Transitions[0].Permissions=PermissionFixtures.Acl(PermissionFixtures.Grant(1,g));Assert(reason(i)==null);});
        test("native Workspace admin may transition without local grants",()=>{var i=fixture();i.Permissions.Administrator=true;i.Transitions[0].PermissionType=1;i.Transitions[0].Permissions=new PermissionList();i.Permissions.GlobalOperation=new PermissionList();Assert(reason(i)==null);});
        test("approval workflow never becomes a direct standard-user transition",()=>{var i=fixture();i.Transitions[0].PermissionType=1;Assert(reason(i).StartsWith("Approval required"));});
        test("unknown native transition permission mode blocks",()=>{var i=fixture();i.Transitions[0].PermissionType=99;Assert(reason(i).StartsWith("Permissions not verified"));});
        test("empty local ACL is denied rather than defaulted to Anyone",()=>{var i=fixture();i.Transitions[0].Permissions=PermissionFixtures.Acl();Assert(reason(i).StartsWith("Workspace permission denied"));});
        test("unreadable local ACL is not a permission denial",()=>{var i=fixture();i.Transitions[0].Permissions=new PermissionList();Assert(reason(i).StartsWith("Permissions not verified"));});
        test("explicit matching denial blocks another matching grant",()=>{var i=fixture();i.Transitions[0].Permissions.Grants.Add(PermissionFixtures.Grant(0,workspace.UserGuid,false));Assert(reason(i).StartsWith("Workspace permission denied"));});
        test("unknown scope is never silently allowed",()=>{var i=fixture();i.Transitions[0].Permissions=PermissionFixtures.Acl(PermissionFixtures.Grant(99));Assert(reason(i).StartsWith("Permissions not verified"));});
        test("permissions read for a different workspace are blocked",()=>{var i=fixture();i.Permissions.WorkspaceKey="different";Assert(reason(i).StartsWith("Permissions not verified"));});
        test("permissions read for a different user are blocked",()=>{var i=fixture();i.Permissions.UserGuid=Guid.NewGuid().ToString();Assert(reason(i).StartsWith("Permissions not verified"));});
        test("unverified administrator identity never bypasses ACLs",()=>{var i=fixture();i.Permissions.Administrator=true;i.Permissions.IdentityVerified=false;Assert(reason(i).StartsWith("Permissions not verified"));});
        test("invalid user evidence stays unverified",()=>{var i=fixture();i.Permissions.UserGuid="";Assert(reason(i).StartsWith("Permissions not verified"));});
    }
    static void Assert(bool value) {if(!value) throw new Exception("Workspace permission policy failed.");}
}
