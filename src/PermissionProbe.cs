#if PERMISSION_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DXP;

namespace AltiumRevisionRetirement
{
    // Diagnostic build only. No permission rule is changed and the native writer is excluded at compilation.
    internal static class PermissionProbe
    {
        static Form window;
        static List<object> events;
        static string activeEmail;
        internal static void Record(string stage,object data)
        {if(events!=null && events.Count<10000) events.Add(new {Utc=DateTime.UtcNow.ToString("o"),Stage=stage,Data=data});}
        internal static void RecordAclEntry(string objectGuid,IULB_ACLEntry entry,WorkspaceGrant used)
        {
            object group=null,mask=null;
            try {group=entry.GetGroupName();} catch(Exception error) {group=PermissionDiagnostics.Summarize(error);}
            try {mask=entry.GetAllowPermissions();} catch(Exception error) {mask=PermissionDiagnostics.Summarize(error);}
            Record("acl-entry",new {ObjectGuid=objectGuid,Scope=used.Scope,Principal=used.PrincipalGuid,GroupName=group,Allowed=used.Allowed,AllowPermissions=mask});
        }
        internal static void RecordUserDetails(IEDMS_Vault vault)
        {
            try {
                var user=vault.GetUserDetails();
                if(user==null) {Record("diagnostic-user-details",new {Present=false});return;}
                activeEmail=user.GetEmail();
                Record("diagnostic-user-details",new {Guid=user.GetGUID(),Username=user.GetUserName(),Email=activeEmail});
            } catch(Exception error) {Record("diagnostic-user-details-failed",new {Error=PermissionDiagnostics.Summarize(error)});}
        }
        internal static void RecordGroup(string group,string user,string username,IStrings members)
        {
            try {RecordGroupCore(group,user,username,members);}
            catch(Exception error) {Record("group-trace-error",new {GroupGuid=group,Error=PermissionDiagnostics.Summarize(error)});}
        }
        static void RecordGroupCore(string group,string user,string username,IStrings members)
        {
            int guidMatches=0,nameMatches=0,emailMatches=0,embeddedGuidMatches=0;var formats=new Dictionary<string,int>();var relevant=new List<string>();
            for(int i=0;i<members.GetCount();i++)
            {
                string value=(members.GetItem(i)??"").Trim();Guid id;
                bool guid=Guid.TryParse(value,out id);
                bool exactGuid=guid && id.ToString("D")==Id.Of(user);
                bool exactName=!string.IsNullOrWhiteSpace(username) && string.Equals(value,username,StringComparison.OrdinalIgnoreCase);
                bool exactEmail=!string.IsNullOrWhiteSpace(activeEmail) && string.Equals(value,activeEmail,StringComparison.OrdinalIgnoreCase);
                bool embeddedGuid=value.IndexOf(Id.Of(user),StringComparison.OrdinalIgnoreCase)>=0;
                if(exactGuid) guidMatches++;if(exactName) nameMatches++;if(exactEmail) emailMatches++;if(embeddedGuid) embeddedGuidMatches++;
                if((exactGuid||exactName||exactEmail||embeddedGuid) && relevant.Count<3) relevant.Add(PermissionDiagnostics.Summarize(new Exception(value)));
                var format=guid ? "GUID" : value.Contains("=") ? "Name/value" : value.Contains("|") ? "Pipe-delimited" : value.Contains("@") ? "Email-like" : value.Any(char.IsWhiteSpace) ? "Text with whitespace" : "Plain text";
                int count;formats.TryGetValue(format,out count);formats[format]=count+1;
            }
            // Record the active user's comparison identity and entry shapes, never complete group rosters.
            Record("group-response",new {GroupGuid=group,ActiveCanonicalUsername=username,Count=members.GetCount(),ActiveGuidMatches=guidMatches,ActiveNameMatches=nameMatches,ActiveEmailMatches=emailMatches,EmbeddedActiveGuidMatches=embeddedGuidMatches,ActiveSubjectEntries=relevant,MemberFormats=formats});
        }
        internal static void Show()
        {
            if(window!=null && !window.IsDisposed) {window.Activate();return;}
            var form=new Form {Text="Fluidra | READ-ONLY permission probe 0.1.9.9000",Size=new Size(900,420),MinimumSize=new Size(780,360)};
            window=form;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=4};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(new Label {Text="Read-only native API capture. Enter the exact older Revision ID used in the failing search.\r\nLifecycle writes are disabled in this build. No workspace permissions are changed.\r\nThe local report contains workspace/user GUIDs, relevant ACL fields and group response formats; no group roster or session credential is exported.",Dock=DockStyle.Fill},0,0);
            var input=new TextBox {Dock=DockStyle.Fill,AccessibleName="Exact Revision ID"};layout.Controls.Add(input,0,1);
            var run=new Button {Text="Capture native responses",AutoSize=true};layout.Controls.Add(run,0,2);
            var output=new TextBox {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical};layout.Controls.Add(output,0,3);
            bool busy=false;
            form.FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
            run.Click+=(s,e)=>{
                if(string.IsNullOrWhiteSpace(input.Text)) {output.Text="Enter an exact older Revision ID.";return;}
                busy=true;run.Enabled=false;events=new List<object>();activeEmail=null;
                string path=null;
                try
                {
                    output.Text="Reading native workspace responses...";output.Refresh();
                    var gateway=new AltiumGateway();var workspace=gateway.ActiveWorkspace();
                    var item=gateway.FindItemByRevisionId(input.Text.Trim());
                    if(item==null) throw new InvalidOperationException("The exact Revision ID was not found.");
                    Record("item",new {ItemGuid=item.Guid,ItemHrid=item.Hrid,LatestGuid=item.LatestGuid,DefinitionGuid=item.DefinitionGuid,Revisions=item.Revisions.Select(r=>new {r.Guid,r.Hrid,r.StateGuid,r.StateName,r.CanUpdate}).ToArray(),Transitions=item.Transitions.Select(t=>new {t.Guid,t.BeforeGuid,t.AfterGuid,t.PermissionType}).ToArray()});
                    foreach(var revision in item.Revisions)
                    {
                        var candidate=RetirementEngine.Describe(workspace,item,revision);
                        if(candidate!=null) Record("unchanged-policy-result",new {candidate.RevisionGuid,candidate.RevisionHrid,candidate.Selectable,candidate.Reason});
                    }
                    if(gateway.ActiveWorkspace().Key!=workspace.Key) throw new InvalidOperationException("The active workspace or user changed during capture.");
                    output.Text="Native read completed. Saving local evidence...";
                }
                catch(Exception error) {Record("capture-error",new {Error=PermissionDiagnostics.Summarize(error)});output.Text="Capture stopped: "+PermissionDiagnostics.Summarize(error);}
                finally
                {
                    try
                    {
                        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AltiumRevisionRetirement","Diagnostics");Directory.CreateDirectory(root);
                        path=Path.Combine(root,"permission-probe-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".json");
                        var data=new {Schema=1,Build="0.1.9.9000 READ-ONLY",RevisionId=input.Text.Trim(),Policy="Unchanged from 0.1.9; native writer excluded",NativeWritesEnabled=false,Events=events};
                        var json=new JavaScriptSerializer {MaxJsonLength=4*1024*1024}.Serialize(data);
                        File.WriteAllText(path,json,new UTF8Encoding(false));
                        output.AppendText(Environment.NewLine+"Report: "+path+Environment.NewLine+"No lifecycle event was submitted. Review this report before sharing it; nothing is uploaded automatically.");
                    }
                    catch(Exception error) {output.AppendText(Environment.NewLine+"Could not save evidence: "+PermissionDiagnostics.Summarize(error));}
                    events=null;busy=false;run.Enabled=true;
                }
            };
            form.Controls.Add(layout);form.Show();
        }
    }
}
#endif
