using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AltiumRevisionRetirement
{
    internal sealed class FolderSelectionForm: Form
    {
        readonly TreeView tree=new TreeView {Dock=DockStyle.Fill,CheckBoxes=true,HideSelection=false};
        readonly IList<WorkspaceFolder> folders;
        readonly Button ok=new Button {Text="Use selected folders",DialogResult=DialogResult.OK,AutoSize=true};
        bool updating;
        public IList<string> SelectedFolders { get; private set; }
        public string SelectionSummary { get; private set; }
        IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes)
        {foreach(TreeNode node in nodes) {yield return node;foreach(var child in Nodes(node.Nodes)) yield return child;}}
        public FolderSelectionForm(IList<WorkspaceFolder> folders,IList<string> selected)
        {
            this.folders=folders;
            Text="Choose component folders";Size=new Size(700,600);MinimumSize=new Size(500,400);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9F);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            layout.Controls.Add(new Label {Text="Choose folders under Components. Checking a folder includes its subfolders.\nThis filter applies to both Revision ID searches and scans.",Dock=DockStyle.Fill},0,0);
            var nodes=folders.ToDictionary(f=>Id.Of(f.Guid),f=>new TreeNode(f.Name) {Tag=f,Checked=selected==null || selected.Any(id=>Id.Same(id,f.Guid))});
            foreach(var folder in folders.OrderBy(f=>f.Name,StringComparer.OrdinalIgnoreCase))
            {
                TreeNode parent;var node=nodes[Id.Of(folder.Guid)];
                if(!folder.IsComponentsRoot && !string.IsNullOrWhiteSpace(folder.ParentGuid) && nodes.TryGetValue(Id.Of(folder.ParentGuid),out parent)) parent.Nodes.Add(node);
                else tree.Nodes.Add(node);
            }
            tree.AfterCheck+=(s,e)=>
            {
                if(updating) return;updating=true;
                try
                {
                    foreach(var child in Nodes(e.Node.Nodes)) child.Checked=e.Node.Checked;
                    if(!e.Node.Checked) for(var parent=e.Node.Parent;parent!=null;parent=parent.Parent) parent.Checked=false;
                }
                finally {updating=false;}
                ok.Enabled=Nodes(tree.Nodes).Any(n=>n.Checked);
            };
            tree.ExpandAll();layout.Controls.Add(tree,0,1);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var cancel=new Button {Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};
            var all=new Button {Text="Select all",AutoSize=true};var none=new Button {Text="Clear selection",AutoSize=true};
            all.Click+=(s,e)=>SetAll(true);none.Click+=(s,e)=>SetAll(false);
            ok.Enabled=Nodes(tree.Nodes).Any(n=>n.Checked);
            ok.Click+=(s,e)=>
            {
                var checkedNodes=Nodes(tree.Nodes).Where(n=>n.Checked).ToList();
                SelectedFolders=checkedNodes.Count==folders.Count ? null : ComponentFolderTree.SelectSubtrees(this.folders,checkedNodes.Select(n=>((WorkspaceFolder)n.Tag).Guid));
                var top=checkedNodes.Where(n=>n.Parent==null || !n.Parent.Checked).ToList();
                SelectionSummary=SelectedFolders==null ? "all of Components" : string.Join(", ",top.Select(n=>n.FullPath))+" ("+SelectedFolders.Count+" folders including subfolders)";
            };
            buttons.Controls.Add(ok);buttons.Controls.Add(cancel);buttons.Controls.Add(none);buttons.Controls.Add(all);
            layout.Controls.Add(buttons,0,2);Controls.Add(layout);CancelButton=cancel;AcceptButton=ok;
        }
        void SetAll(bool value)
        {updating=true;foreach(var node in Nodes(tree.Nodes)) node.Checked=value;updating=false;ok.Enabled=value && folders.Count>0;}
    }
}
