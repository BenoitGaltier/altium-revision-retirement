using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using AltiumRevisionRetirement;

namespace AltiumRevisionRetirement
{
    // The offscreen UI test replaces the native adapter; any workspace call fails the test.
    public sealed class AltiumGateway : IGateway
    {
        public Workspace ActiveWorkspace() { throw new NotSupportedException(); }
        public IList<Item> ReadAll(Action<string> p) { throw new NotSupportedException(); }
        public Item ReadItem(string id) { throw new NotSupportedException(); }
        public IList<Parent> ReadParents(string id) { throw new NotSupportedException(); }
        public void Transition(Candidate c,string n) { throw new NotSupportedException(); }
    }
}
static class UiSmokeTests
{
    static object Field(object form,string name) {return form.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);}
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
                var c=new Candidate {Kind=kinds[i],Workspace=new Workspace {Name="Demonstration workspace"},ItemHrid="ITEM-00"+(i+1),RevisionHrid="ITEM-00"+(i+1)+"-01",LatestHrid="ITEM-00"+(i+1)+"-02",BeforeName="Released",Name="Example "+kinds[i],Reason=i==2?"No direct transition to Obsolete":null};
                int row=grid.Rows.Add(false,c.Kind,c.ItemHrid,c.RevisionHrid,c.BeforeName,c.LatestHrid,c.Name,c.Reason??"Direct transition available; dependencies checked before applying");
                grid.Rows[row].Tag=c;if(c.Selectable) plan.Ordered.Add(c);
            }
            var select=form.GetType().GetMethod("SelectRows",BindingFlags.NonPublic|BindingFlags.Instance);
            select.Invoke(form,new object[]{true});
            Assert(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)&&Convert.ToBoolean(grid.Rows[1].Cells[0].Value)&&!Convert.ToBoolean(grid.Rows[2].Cells[0].Value),"Select all included a blocked revision");
            Assert(((Button)Field(form,"apply")).Enabled,"Selection did not enable Apply");
            English(form);
            using(var confirm=new ConfirmationForm(plan)) English(confirm);
            form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();form.PerformLayout();
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[0],System.Drawing.Imaging.ImageFormat.Png);}
            select.Invoke(form,new object[]{false});Assert(!((Button)Field(form,"apply")).Enabled,"Empty selection enabled Apply");
        }
        Console.WriteLine("English UI, confirmation dialog, protected selection and Fluidra layout tests passed; no native workspace used.");return 0;
    }
}
