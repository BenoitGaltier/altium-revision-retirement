using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AltiumRevisionRetirement
{
    public sealed class RetirementForm: Form
    {
        readonly IGateway gateway=new AltiumGateway();
        readonly DataGridView grid=new DataGridView();
        readonly Label workspace=new Label(), status=new Label(), counts=new Label();
        readonly Button scan=new Button(), all=new Button(), none=new Button(), apply=new Button(), cancel=new Button();
        bool busy, cancelled;
        ScanDiagnostics scanDiagnostics;
        public RetirementForm()
        {
            Text="Fluidra | Altium Revision Retirement"; Size=new Size(1250,820); MinimumSize=new Size(1000,600);
            StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",9F); AutoScaleMode=AutoScaleMode.Dpi;
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,112));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            workspace.Dock=DockStyle.Fill; workspace.Text="Active workspace: click Scan.";
            layout.Controls.Add(Branding(),0,0);
            layout.Controls.Add(workspace,0,1);
            var toolbar=new FlowLayoutPanel {Dock=DockStyle.Fill};
            scan.Text="Scan"; all.Text="Select all"; none.Text="Clear selection";
            foreach(var button in new[]{scan,all,none}) {button.AutoSize=true;toolbar.Controls.Add(button);}
            layout.Controls.Add(toolbar,0,2);
            grid.Dock=DockStyle.Fill; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false;
            grid.AutoGenerateColumns=false; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="pick",HeaderText="Select",Width=60});
            AddColumn("kind","Type",90); AddColumn("item","Item",130); AddColumn("revision","Old revision",140);
            AddColumn("latest","Protected latest",140); AddColumn("name","Description",220); AddColumn("reason","Availability",300);
            grid.Columns.Insert(4,new DataGridViewTextBoxColumn {Name="state",HeaderText="Current state",Width=110,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.CellBeginEdit+=(s,e)=> {if(e.ColumnIndex==0 && !((Candidate)grid.Rows[e.RowIndex].Tag).Selectable) e.Cancel=true;};
            grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=(s,e)=>UpdateSelection();
            layout.Controls.Add(grid,0,3);
            counts.Dock=DockStyle.Fill; layout.Controls.Add(counts,0,4);
            var bottom=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3};
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,260));
            status.Dock=DockStyle.Fill;status.AutoEllipsis=true;
            cancel.Text="Stop";cancel.Enabled=false;cancel.Dock=DockStyle.Fill;
            apply.Text="Set selected revisions to Obsolete…";apply.Dock=DockStyle.Fill;
            bottom.Controls.Add(status,0,0);bottom.Controls.Add(cancel,1,0);bottom.Controls.Add(apply,2,0);layout.Controls.Add(bottom,0,5);
            Controls.Add(layout);
            scan.Click+=(s,e)=>RunScan(); all.Click+=(s,e)=>SelectRows(true); none.Click+=(s,e)=>SelectRows(false);
            cancel.Click+=(s,e)=>{cancelled=true;cancel.Enabled=false;status.Text="Stopping after the current operation…";};
            apply.Click+=(s,e)=>RunApply();
            FormClosing+=(s,e)=>{if(busy) {e.Cancel=true;cancelled=true;}};
            UpdateSelection();
        }
        static Control Branding()
        {
            var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.FromArgb(0,58,112),Padding=new Padding(16,10,16,10)};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var logo=new PictureBox {Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,16,22,16),AccessibleName="Fluidra logo"};
            using(var stream=typeof(RetirementForm).Assembly.GetManifestResourceStream("AltiumRevisionRetirement.FluidraLogo.png"))
            {
                if(stream==null) throw new InvalidOperationException("Embedded Fluidra logo is missing.");
                using(var source=Image.FromStream(stream)) logo.Image=new Bitmap(source);
            }
            logo.Disposed+=(s,e)=>{if(logo.Image!=null) logo.Image.Dispose();};
            header.Controls.Add(logo,0,0);
            var text=new Panel {Dock=DockStyle.Fill,Margin=new Padding(0)};
            var title=new Label {Text="Workspace Revision Lifecycle Management",Dock=DockStyle.Top,Height=25,ForeColor=Color.White,Font=new Font("Segoe UI",12F,FontStyle.Bold),Margin=new Padding(0)};
            var description=new Label {Text="Find superseded symbols, footprints and components in the active Altium 365 workspace.\nReview selected revisions and move eligible ones to Obsolete. The latest revision of each item is protected.",Dock=DockStyle.Top,Height=36,ForeColor=Color.White,Margin=new Padding(0)};
            var guide=new LinkLabel {Text="User guide (English)",Dock=DockStyle.Top,Height=19,LinkColor=Color.White,ActiveLinkColor=Color.White,VisitedLinkColor=Color.White,Margin=new Padding(0)};
            guide.LinkClicked+=(s,e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {FileName="https://github.com/BenoitGaltier/altium-revision-retirement/blob/main/docs/USER_GUIDE.md",UseShellExecute=true});
            text.Controls.Add(guide);text.Controls.Add(description);text.Controls.Add(title);header.Controls.Add(text,1,0);return header;
        }
        void AddColumn(string name,string title,int width)
        {grid.Columns.Add(new DataGridViewTextBoxColumn {Name=name,HeaderText=title,Width=width,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});}
        void SetBusy(bool value)
        {busy=value; scan.Enabled=!value;all.Enabled=!value;none.Enabled=!value;grid.Enabled=!value;cancel.Enabled=value;UpdateSelection();}
        void UpdateSelection()
        {
            if(counts==null) return;
            var selected=Selected(); counts.Text=grid.Rows.Count+" old non-Obsolete revisions • "+selected.Count+" selected. Latest revisions are excluded.";
            apply.Enabled=!busy && selected.Count>0;
        }
        List<Candidate> Selected()
        {return grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Tag is Candidate && Convert.ToBoolean(r.Cells[0].Value??false)).Select(r=>(Candidate)r.Tag).ToList();}
        void SelectRows(bool value)
        {foreach(DataGridViewRow row in grid.Rows) row.Cells[0].Value=value && ((Candidate)row.Tag).Selectable;UpdateSelection();}
        void Progress(string message)
        {status.Text=message;if(scanDiagnostics!=null) scanDiagnostics.Progress(message);status.Refresh();Application.DoEvents();if(cancelled) throw new OperationCanceledException("Operation cancelled. Previously verified transitions remain applied.");}
        void RunScan()
        {
            if(busy) return; cancelled=false;SetBusy(true);grid.Rows.Clear();
            scanDiagnostics=new ScanDiagnostics();
            try
            {
                var w=gateway.ActiveWorkspace(); workspace.Text="Active workspace: "+w.Name+Environment.NewLine+w.Url;
                scanDiagnostics.Workspace(w);
                string inventorySummary=null;
                var result=new RetirementEngine(gateway).Scan(message=>{inventorySummary=message;Progress(message);});
                foreach(var c in result)
                {
                    int index=grid.Rows.Add(false,c.Kind,c.ItemHrid,c.RevisionHrid,c.BeforeName,c.LatestHrid,c.Name,c.Selectable ? "Direct transition available; dependencies will be checked before applying": c.Reason);
                    grid.Rows[index].Tag=c;
                    if(!c.Selectable) {grid.Rows[index].DefaultCellStyle.ForeColor=Color.DimGray;grid.Rows[index].Cells[0].ReadOnly=true;}
                }
                status.Text=inventorySummary??"Scan completed. No workspace data was changed.";
                scanDiagnostics.Complete(result);
            }
            catch(Exception ex) {scanDiagnostics.Failed(ex);ShowError(ex);}
            finally {scanDiagnostics=null;SetBusy(false);}
        }
        void RunApply()
        {
            if(busy) return;grid.EndEdit();var selected=Selected(); cancelled=false;SetBusy(true);
            string journalPath=null;
            try
            {
                status.Text="Checking revisions and dependencies…";status.Refresh();
                var engine=new RetirementEngine(gateway); var plan=engine.Prepare(selected,Progress);
                using(var confirmation=new ConfirmationForm(plan))
                {
                    if(confirmation.ShowDialog(this)!=DialogResult.OK) return;
                    using(var journal=new AuditJournal())
                    {
                        journalPath=journal.Path;
                        int changed=engine.Execute(plan,confirmation.Note,journal,()=>cancelled,Progress);
                        MessageBox.Show(this,changed+" transition(s) verified."+Environment.NewLine+"Journal: "+journal.Path+Environment.NewLine+"Scan again to refresh the list.",Text,MessageBoxButtons.OK,MessageBoxIcon.Information);
                    }
                }
            }
            catch(Exception ex) {ShowError(journalPath==null ? ex: new InvalidOperationException(ex.Message+Environment.NewLine+"Journal: "+journalPath,ex));}
            finally {grid.Rows.Clear();status.Text="Scan again before starting another operation.";SetBusy(false);}
        }
        void ShowError(Exception ex)
        {status.Text=ex.Message;MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,ex is OperationCanceledException ? MessageBoxIcon.Information: MessageBoxIcon.Warning);}
    }
    sealed class ConfirmationForm: Form
    {
        readonly TextBox note=new TextBox();
        public string Note { get { return note.Text.Trim(); } }
        public ConfirmationForm(Plan plan)
        {
            Text="Confirm transitions to Obsolete";Size=new Size(900,600);MinimumSize=new Size(750,480);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9F);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=5,ColumnCount=1,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
            layout.Controls.Add(new Label {Dock=DockStyle.Fill,Text="Workspace: "+plan.Candidates[0].Workspace.Name+Environment.NewLine+plan.Candidates.Count+" old revisions in the order below. The batch stops on the first failure; previous transitions are not rolled back automatically."},0,0);
            var list=new ListBox {Dock=DockStyle.Fill,HorizontalScrollbar=true};
            foreach(var c in plan.Candidates) list.Items.Add(c.Kind+" | "+c.RevisionHrid+" → Obsolete | protected latest: "+c.LatestHrid);
            layout.Controls.Add(list,0,1);layout.Controls.Add(new Label {Text="Change note (required; recorded in Altium):",Dock=DockStyle.Fill},0,2);
            note.Multiline=true;note.MaxLength=500;note.Dock=DockStyle.Fill;layout.Controls.Add(note,0,3);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var ok=new Button {Text="Confirm",DialogResult=DialogResult.OK,Enabled=false,AutoSize=true};
            var back=new Button {Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};
            note.TextChanged+=(s,e)=>ok.Enabled=!string.IsNullOrWhiteSpace(note.Text);
            buttons.Controls.Add(ok);buttons.Controls.Add(back);layout.Controls.Add(buttons,0,4);Controls.Add(layout);CancelButton=back;
        }
    }
}
