using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AltiumRevisionRetirement
{
    public sealed class RetirementForm : Form
    {
        readonly IGateway gateway=new AltiumGateway();
        readonly DataGridView grid=new DataGridView();
        readonly Label workspace=new Label(), status=new Label(), counts=new Label();
        readonly Button scan=new Button(), all=new Button(), none=new Button(), apply=new Button(), cancel=new Button();
        bool busy, cancelled;
        public RetirementForm()
        {
            Text="Altium Revision Retirement"; Size=new Size(1250,720); MinimumSize=new Size(950,500);
            StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",9F); AutoScaleMode=AutoScaleMode.Dpi;
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=5,ColumnCount=1,Padding=new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            workspace.Dock=DockStyle.Fill; workspace.Text="Workspace actif : cliquer sur Analyser.";
            layout.Controls.Add(workspace,0,0);
            var toolbar=new FlowLayoutPanel {Dock=DockStyle.Fill};
            scan.Text="Analyser"; all.Text="Tout sélectionner"; none.Text="Tout désélectionner";
            foreach(var button in new[]{scan,all,none}) {button.AutoSize=true;toolbar.Controls.Add(button);}
            layout.Controls.Add(toolbar,0,1);
            grid.Dock=DockStyle.Fill; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false;
            grid.AutoGenerateColumns=false; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="pick",HeaderText="Choisir",Width=60});
            AddColumn("kind","Type",90); AddColumn("item","Item",130); AddColumn("revision","Ancienne révision",140);
            AddColumn("latest","Dernière protégée",140); AddColumn("name","Description",220); AddColumn("reason","Disponibilité",300);
            grid.Columns.Insert(4,new DataGridViewTextBoxColumn {Name="state",HeaderText="État actuel",Width=110,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.CellBeginEdit+=(s,e)=> {if(e.ColumnIndex==0 && !((Candidate)grid.Rows[e.RowIndex].Tag).Selectable) e.Cancel=true;};
            grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=(s,e)=>UpdateSelection();
            layout.Controls.Add(grid,0,2);
            counts.Dock=DockStyle.Fill; layout.Controls.Add(counts,0,3);
            var bottom=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3};
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,260));
            status.Dock=DockStyle.Fill;status.AutoEllipsis=true;
            cancel.Text="Arrêter";cancel.Enabled=false;cancel.Dock=DockStyle.Fill;
            apply.Text="Passer la sélection à Obsolete…";apply.Dock=DockStyle.Fill;
            bottom.Controls.Add(status,0,0);bottom.Controls.Add(cancel,1,0);bottom.Controls.Add(apply,2,0);layout.Controls.Add(bottom,0,4);
            Controls.Add(layout);
            scan.Click+=(s,e)=>RunScan(); all.Click+=(s,e)=>SelectRows(true); none.Click+=(s,e)=>SelectRows(false);
            cancel.Click+=(s,e)=>{cancelled=true;cancel.Enabled=false;status.Text="Arrêt demandé après l'opération en cours…";};
            apply.Click+=(s,e)=>RunApply();
            FormClosing+=(s,e)=>{if(busy) {e.Cancel=true;cancelled=true;}};
            UpdateSelection();
        }
        void AddColumn(string name,string title,int width)
        {grid.Columns.Add(new DataGridViewTextBoxColumn {Name=name,HeaderText=title,Width=width,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});}
        void SetBusy(bool value)
        {busy=value; scan.Enabled=!value;all.Enabled=!value;none.Enabled=!value;grid.Enabled=!value;cancel.Enabled=value;UpdateSelection();}
        void UpdateSelection()
        {
            if(counts==null) return;
            var selected=Selected(); counts.Text=grid.Rows.Count+" anciennes révisions non Obsolete • "+selected.Count+" sélectionnées. Les dernières révisions sont exclues.";
            apply.Enabled=!busy && selected.Count>0;
        }
        List<Candidate> Selected()
        {return grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Tag is Candidate && Convert.ToBoolean(r.Cells[0].Value??false)).Select(r=>(Candidate)r.Tag).ToList();}
        void SelectRows(bool value)
        {foreach(DataGridViewRow row in grid.Rows) row.Cells[0].Value=value && ((Candidate)row.Tag).Selectable;UpdateSelection();}
        void Progress(string message)
        {status.Text=message;status.Refresh();Application.DoEvents();if(cancelled) throw new OperationCanceledException("Opération interrompue. Les transitions déjà confirmées restent appliquées.");}
        void RunScan()
        {
            if(busy) return; cancelled=false;SetBusy(true);grid.Rows.Clear();
            try
            {
                var w=gateway.ActiveWorkspace(); workspace.Text="Workspace actif : "+w.Name+Environment.NewLine+w.Url;
                var result=new RetirementEngine(gateway).Scan(Progress);
                foreach(var c in result)
                {
                    int index=grid.Rows.Add(false,c.Kind,c.ItemHrid,c.RevisionHrid,c.BeforeName,c.LatestHrid,c.Name,c.Selectable ? "Transition directe disponible; dépendances vérifiées avant exécution" : c.Reason);
                    grid.Rows[index].Tag=c;
                    if(!c.Selectable) {grid.Rows[index].DefaultCellStyle.ForeColor=Color.DimGray;grid.Rows[index].Cells[0].ReadOnly=true;}
                }
                status.Text="Analyse terminée. Aucune donnée du workspace modifiée.";
            }
            catch(Exception ex) {ShowError(ex);}
            finally {SetBusy(false);}
        }
        void RunApply()
        {
            if(busy) return;grid.EndEdit();var selected=Selected(); cancelled=false;SetBusy(true);
            string journalPath=null;
            try
            {
                status.Text="Vérification des révisions et dépendances…";status.Refresh();
                var engine=new RetirementEngine(gateway); var plan=engine.Prepare(selected,Progress);
                using(var confirmation=new ConfirmationForm(plan))
                {
                    if(confirmation.ShowDialog(this)!=DialogResult.OK) return;
                    using(var journal=new AuditJournal())
                    {
                        journalPath=journal.Path;
                        int changed=engine.Execute(plan,confirmation.Note,journal,()=>cancelled,Progress);
                        MessageBox.Show(this,changed+" transition(s) vérifiée(s)."+Environment.NewLine+"Journal : "+journal.Path+Environment.NewLine+"Relancer l'analyse pour actualiser la liste.",Text,MessageBoxButtons.OK,MessageBoxIcon.Information);
                    }
                }
            }
            catch(Exception ex) {ShowError(journalPath==null ? ex : new InvalidOperationException(ex.Message+Environment.NewLine+"Journal : "+journalPath,ex));}
            finally {grid.Rows.Clear();status.Text="Relancer l'analyse avant toute nouvelle opération.";SetBusy(false);}
        }
        void ShowError(Exception ex)
        {status.Text=ex.Message;MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,ex is OperationCanceledException ? MessageBoxIcon.Information : MessageBoxIcon.Warning);}
    }
    sealed class ConfirmationForm : Form
    {
        readonly TextBox note=new TextBox();
        public string Note { get { return note.Text.Trim(); } }
        public ConfirmationForm(Plan plan)
        {
            Text="Confirmer les transitions vers Obsolete";Size=new Size(900,600);MinimumSize=new Size(750,480);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9F);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=5,ColumnCount=1,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
            layout.Controls.Add(new Label {Dock=DockStyle.Fill,Text="Workspace : "+plan.Candidates[0].Workspace.Name+Environment.NewLine+plan.Candidates.Count+" anciennes révisions, dans l'ordre ci-dessous. Arrêt au premier échec, sans annulation automatique des transitions précédentes."},0,0);
            var list=new ListBox {Dock=DockStyle.Fill,HorizontalScrollbar=true};
            foreach(var c in plan.Candidates) list.Items.Add(c.Kind+" | "+c.RevisionHrid+" → Obsolete | dernière protégée : "+c.LatestHrid);
            layout.Controls.Add(list,0,1);layout.Controls.Add(new Label {Text="Note de changement (obligatoire, enregistrée dans Altium) :",Dock=DockStyle.Fill},0,2);
            note.Multiline=true;note.MaxLength=500;note.Dock=DockStyle.Fill;layout.Controls.Add(note,0,3);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var ok=new Button {Text="Confirmer",DialogResult=DialogResult.OK,Enabled=false,AutoSize=true};
            var back=new Button {Text="Annuler",DialogResult=DialogResult.Cancel,AutoSize=true};
            note.TextChanged+=(s,e)=>ok.Enabled=!string.IsNullOrWhiteSpace(note.Text);
            buttons.Controls.Add(ok);buttons.Controls.Add(back);layout.Controls.Add(buttons,0,4);Controls.Add(layout);CancelButton=back;
        }
    }
}
