using AltiumRevisionRetirement;

static class PermissionFixtures
{
    public static PermissionList Acl(params WorkspaceGrant[] grants)
    {var acl=new PermissionList {Read=true};acl.Grants.AddRange(grants);return acl;}
    public static WorkspaceGrant Grant(int scope,string principal=null,bool allow=true)
    {return new WorkspaceGrant {Scope=scope,PrincipalGuid=principal,Allowed=allow};}
    public static void Bind(Item item,Workspace workspace)
    {
        if(item.Permissions!=null) return;
        item.Permissions=new WorkspacePermissions {WorkspaceKey=workspace.Key,UserGuid=workspace.UserGuid,IdentityVerified=true,
            GlobalOperation=Acl(Grant(5))};
        foreach(var transition in item.Transitions)
        {transition.PermissionType=0;transition.Permissions=Acl(Grant(2));}
    }
}
