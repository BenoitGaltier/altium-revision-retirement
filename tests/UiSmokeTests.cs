using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AltiumRevisionRetirement;

namespace AltiumRevisionRetirement
{
    // The offscreen UI test replaces the native adapter; any workspace call fails the test.
    public sealed class AltiumGateway : IGateway
    {
        public Workspace ActiveWorkspace() { throw new NotSupportedException(); }
        public IList<WorkspaceFolder> ReadFolders(Action<string> p) { throw new NotSupportedException(); }
        public IList<Item> ReadAll(IList<string> folders,Action<string> p) { throw new NotSupportedException(); }
        public Item FindItemByRevisionId(string id) { throw new NotSupportedException(); }
        public Item ReadItem(string id) { throw new NotSupportedException(); }
        public IList<Parent> ReadParents(string id) { throw new NotSupportedException(); }
        public void Transition(Candidate c,string n) { throw new NotSupportedException(); }
    }
}
static class UiSmokeTests
{
    sealed class FixtureGateway: IGateway
    {
        public readonly Workspace Workspace=new Workspace {Guid=G(),UserGuid=G(),Name="Isolated fixture",Url="https://example.invalid"};
        public readonly Item Item;
        public int Scans,Lookups,Reads,Writes;
        public bool LoseResponse;
        public FixtureGateway()
        {
            Item=new Item {Guid=G(),FolderGuid=G(),Hrid="CMP-001",Name="Example component",Kind="Component",DefinitionGuid=G()};
            Item.States.Add(new State {Guid=G(),DefinitionGuid=Item.DefinitionGuid,Name="Draft"});
            Item.States.Add(new State {Guid=G(),DefinitionGuid=Item.DefinitionGuid,Name="Obsolete"});
            Item.Transitions.Add(new Transition {Guid=G(),DefinitionGuid=Item.DefinitionGuid,BeforeGuid=Item.States[0].Guid,AfterGuid=Item.States[1].Guid});
            for(int i=1;i<=3;i++) Item.Revisions.Add(new Revision {Guid=G(),ItemGuid=Item.Guid,Hrid="CMP-001-0"+i,StateGuid=Item.States[0].Guid,StateName="Draft",CanUpdate=true});
            Item.LatestGuid=Item.Revisions[2].Guid;
        }
        public Workspace ActiveWorkspace() {return Workspace;}
        public IList<WorkspaceFolder> ReadFolders(Action<string> p) {throw new Exception("Unexpected folder discovery");}
        public IList<Item> ReadAll(IList<string> folders,Action<string> p) {Scans++;return folders==null || folders.Contains(Item.FolderGuid) ? new[]{Item} : new Item[0];}
        public Item FindItemByRevisionId(string id) {Lookups++;return Item.Revisions.Any(r=>r.Hrid==id) ? Item : null;}
        public Item ReadItem(string id)
        {
            Reads++;Assert(id==Item.Guid,"Read a different item");
            var copy=new Item {Guid=Item.Guid,FolderGuid=Item.FolderGuid,Hrid=Item.Hrid,Name=Item.Name,Kind=Item.Kind,DefinitionGuid=Item.DefinitionGuid,LatestGuid=Item.LatestGuid};
            copy.States.AddRange(Item.States);copy.Transitions.AddRange(Item.Transitions);
            copy.Revisions.AddRange(Item.Revisions.Select(r=>new Revision {Guid=r.Guid,ItemGuid=r.ItemGuid,Hrid=r.Hrid,StateGuid=r.StateGuid,StateName=r.StateName,CanUpdate=r.CanUpdate}));return copy;
        }
        public IList<Parent> ReadParents(string id) {return new Parent[0];}
        public void Transition(Candidate c,string note)
        {
            Writes++;var r=Item.Revisions.Single(v=>v.Guid==c.RevisionGuid);r.StateGuid=c.TargetGuid;r.StateName="Obsolete";
            if(LoseResponse) throw new InvalidOperationException("Simulated lost response after server acceptance");
        }
    }
    static string G() {return Guid.NewGuid().ToString();}
    static object Field(object form,string name) {return form.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);}
    static void Invoke(object form,string method,params object[] args) {form.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,args);}
    static RetirementForm Hidden(RetirementForm form) {form.ShowInTaskbar=false;form.Opacity=0;return form;}
    static void Assert(bool value,string message) {if(!value) throw new Exception(message);}
    static void English(Control control)
    {
        string text=control.Text??"";
        foreach(string french in new[]{"Analyser","Confirmer","Annuler","sélection","révision","Relancer","Vérification","État","Arrêter","Workspace actif"})
            Assert(text.IndexOf(french,StringComparison.OrdinalIgnoreCase)<0,"French UI text remains: "+text);
        foreach(Control child in control.Controls) English(child);
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        string note=null;var fixture=new FixtureGateway();var notifications=new List<string>();
        using(var f=Hidden(new RetirementForm(fixture,p=>note,(message,icon)=>notifications.Add(message),System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])),"ui-fixture"))))
        {
            f.Show();Application.DoEvents();
            var input=(TextBox)Field(f,"revisionId");input.Text="CMP-001-03";
            Assert(((Button)Field(f,"scan")).Text=="Search","Revision ID did not take precedence in the UI");
            Invoke(f,"RunScan");var grid=(DataGridView)Field(f,"grid");
            Assert(grid.Rows.Count==2&&fixture.Scans==0&&fixture.Lookups==1,"UI search scanned or exposed latest");
            Invoke(f,"SelectRows",true);Invoke(f,"RunApply");
            Assert(grid.Rows.Count==2&&fixture.Writes==0&&((RevisionRow)grid.Rows[0].Tag).Selected,"Cancel cleared list or selection");
            note="Fixture transition";grid.Rows[1].Cells[0].Value=false;Invoke(f,"RunApply");
            Assert(grid.Rows.Count==2&&fixture.Writes==1&&(string)grid.Rows[0].Cells["state"].Value=="Obsolete"&&grid.Rows[0].Cells[0].ReadOnly,"Successful batch cleared list or left completed row eligible");
            Invoke(f,"SelectRows",true);fixture.LoseResponse=true;Invoke(f,"RunApply");
            Assert(grid.Rows.Count==2&&fixture.Writes==2&&grid.Rows[1].Cells[0].ReadOnly&&((RevisionRow)grid.Rows[1].Tag).Availability.Contains("Unverified"),"Uncertain write cleared list or allowed retry");
            int scans=fixture.Scans;Invoke(f,"RefreshList");
            Assert(grid.Rows.Count==2&&fixture.Scans==scans&&grid.Rows.Cast<DataGridViewRow>().All(r=>(string)r.Cells["state"].Value=="Obsolete"),"Targeted refresh discarded rows or scanned");
            input.Clear();Assert(((Button)Field(f,"scan")).Text=="Scan","Blank ID did not restore scan");
            f.GetType().GetField("selectedFolders",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,new[]{G()});Invoke(f,"RunScan");
            Assert(grid.Rows.Count==0&&fixture.Scans==scans+1,"UI ignored folder scope");
            Assert(notifications.Count==2,"Unexpected fixture errors");English(f);
        }
        var root=new WorkspaceFolder {Guid=G(),Name="Components"};var branch=new WorkspaceFolder {Guid=G(),Name="Components family",ParentGuid=root.Guid};
        var child=new WorkspaceFolder {Guid=G(),Name="Nested",ParentGuid=branch.Guid};var sibling=new WorkspaceFolder {Guid=G(),Name="Symbols",ParentGuid=root.Guid};
        using(var dialog=new FolderSelectionForm(new[]{root,branch,child,sibling},null))
        {
            dialog.ShowInTaskbar=false;dialog.Opacity=0;dialog.Show();Application.DoEvents();
            var tree=(TreeView)Field(dialog,"tree");var nodes=tree.Nodes[0].Nodes;
            nodes.Cast<TreeNode>().Single(n=>((WorkspaceFolder)n.Tag).Guid==sibling.Guid).Checked=false;
            using(var bitmap=new Bitmap(dialog.Width,dialog.Height)) {dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"folder-preview.png"),System.Drawing.Imaging.ImageFormat.Png);}
            ((Button)Field(dialog,"ok")).PerformClick();
            Assert(dialog.SelectedFolders.Count==2&&dialog.SelectedFolders.Contains(branch.Guid)&&dialog.SelectedFolders.Contains(child.Guid),"Folder deselection retained root and broadened scope");English(dialog);
        }
        int created=0;
        Func<RetirementForm> create=()=>{created++;return Hidden(new RetirementForm());};
        RetirementWindow.Show(create);RetirementWindow.Show(create);Application.DoEvents();
        var window=(RetirementForm)typeof(RetirementWindow).GetField("current",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Assert(created==1&&!window.Modal,"Main window is modal or duplicated");window.Close();
        RetirementWindow.Show(create);Assert(created==2,"Closed modeless window did not reopen");
        ((RetirementForm)typeof(RetirementWindow).GetField("current",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Close();
        using(var form=new RetirementForm())
        {
            Assert(((Button)Field(form,"scan")).Text=="Scan","Scan button is not English");
            Assert(((Button)Field(form,"all")).Text=="Select all","Select all button is not English");
            Assert(((Button)Field(form,"none")).Text=="Clear selection","Clear selection button is not English");
            var grid=(DataGridView)Field(form,"grid");
            foreach(DataGridViewColumn column in grid.Columns) Assert(column.HeaderText.IndexOf("révision",StringComparison.OrdinalIgnoreCase)<0,"French column header");
            ((Label)Field(form,"workspace")).Text="Active workspace: Demonstration workspace\nhttps://example.invalid";
            var plan=new Plan();
            string[] kinds={"Component","Symbol","Footprint"};
            for(int i=0;i<3;i++)
            {
                var c=new Candidate {RevisionGuid=Guid.NewGuid().ToString(),Kind=kinds[i],Workspace=new Workspace {Name="Demonstration workspace"},ItemHrid="ITEM-00"+(i+1),RevisionHrid="ITEM-00"+(i+1)+"-01",LatestHrid="ITEM-00"+(i+1)+"-02",BeforeName="Released",Name="Example "+kinds[i],Reason=i==2?"No direct transition to Obsolete":null};
                int row=grid.Rows.Add(false,c.Kind,c.ItemHrid,c.RevisionHrid,c.BeforeName,c.LatestHrid,c.Name,c.Reason??"Direct transition available; dependencies checked before applying");
                grid.Rows[row].Tag=new RevisionRow(c);if(c.Selectable) plan.Ordered.Add(c);
            }
            var select=form.GetType().GetMethod("SelectRows",BindingFlags.NonPublic|BindingFlags.Instance);
            select.Invoke(form,new object[]{true});
            Assert(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)&&Convert.ToBoolean(grid.Rows[1].Cells[0].Value)&&!Convert.ToBoolean(grid.Rows[2].Cells[0].Value),"Select all included a blocked revision");
            Assert(((Button)Field(form,"apply")).Enabled,"Selection did not enable Apply");
            Invoke(form,"ObserveAudit","verified",plan.Candidates[0]);
            select.Invoke(form,new object[]{true});
            Assert(!Convert.ToBoolean(grid.Rows[0].Cells[0].Value)&&grid.Rows[0].Cells[0].ReadOnly&&grid.Rows.Count==3,"Retained Obsolete row was selected again");
            ((Label)Field(form,"resultScope")).Text="Displayed results: folder scan | Folders: all of Components (including subfolders)";
            English(form);
            using(var confirm=new ConfirmationForm(plan)) English(confirm);
            form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();form.PerformLayout();
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[0],System.Drawing.Imaging.ImageFormat.Png);}
            select.Invoke(form,new object[]{false});Assert(!((Button)Field(form,"apply")).Enabled,"Empty selection enabled Apply");
        }
        Console.WriteLine("PASS English UI, modeless single window, Revision ID priority, folder selection, retained results after cancel/success/uncertain writes, targeted refresh and Fluidra layout; no native workspace used.");return 0;
    }
}
