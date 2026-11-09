using System;
using System.Collections.Generic;
using System.Linq;
using AltiumRevisionRetirement;

static class SafetyTests
{
    static int passed;
    static string G() { return Guid.NewGuid().ToString("D"); }
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    static void Reject(Action action) {try {action();} catch(InvalidOperationException) {return;} throw new Exception("Expected rejection");}
    static void Test(string name,Action action) {action();passed++;Console.WriteLine("PASS "+name);}
    static Item Make(string kind)
    {
        var item=new Item {Guid=G(),Hrid=kind+"-001",Name="Fixture",Kind=kind,DefinitionGuid=G(),FolderGuid=G()};
        var released=new State {Guid=G(),Name="Released",DefinitionGuid=item.DefinitionGuid};
        var obsolete=new State {Guid=G(),Name="Obsolete",DefinitionGuid=item.DefinitionGuid};
        item.States.Add(released);item.States.Add(obsolete);
        item.Transitions.Add(new Transition {Guid=G(),DefinitionGuid=item.DefinitionGuid,BeforeGuid=released.Guid,AfterGuid=obsolete.Guid});
        item.Revisions.Add(new Revision {Guid=G(),ItemGuid=item.Guid,Hrid="REV-9",StateGuid=released.Guid,StateName="Released",Released=true,CanUpdate=true});
        item.Revisions.Add(new Revision {Guid=G(),ItemGuid=item.Guid,Hrid="REV-10",StateGuid=released.Guid,StateName="Released",Released=true,CanUpdate=true});
        item.LatestGuid=item.Revisions[1].Guid;
        return item;
    }
    sealed class Fake : IGateway
    {
        public Workspace Workspace=new Workspace {Guid=G(),UserGuid=G(),Name="Test",Url="https://example.invalid"};
        public readonly List<Item> Items=new List<Item>();
        public readonly Dictionary<string,List<Parent>> Parents=new Dictionary<string,List<Parent>>();
        public readonly List<string> Writes=new List<string>();
        public Action OnRead=null, OnWrite=null, OnFind=null;
        public int Scans, Lookups, ItemReads;
        public Item LookupOverride;
        public bool ThrowOnWrite, IgnoreWrite;
        public Workspace ActiveWorkspace() {return new Workspace {Guid=Workspace.Guid,UserGuid=Workspace.UserGuid,Name=Workspace.Name,Url=Workspace.Url};}
        public IList<WorkspaceFolder> ReadFolders(Action<string> progress) {throw new NotSupportedException();}
        public IList<Item> ReadAll(IList<string> folders,Action<string> progress) {Scans++;return folders==null ? Items : Items.Where(i=>folders.Any(f=>Id.Same(f,i.FolderGuid))).ToList();}
        public Item FindItemByRevisionId(string id) {Lookups++;if(OnFind!=null) OnFind();return LookupOverride??Items.SingleOrDefault(i=>i.Revisions.Any(r=>string.Equals(r.Hrid,id,StringComparison.OrdinalIgnoreCase)));}
        public Item ReadItem(string id) {ItemReads++;if(OnRead!=null) OnRead();return Copy(Items.Single(i=>i.Guid==id));}
        static Item Copy(Item source)
        {
            var i=new Item {Guid=source.Guid,Hrid=source.Hrid,Name=source.Name,Kind=source.Kind,DefinitionGuid=source.DefinitionGuid,LatestGuid=source.LatestGuid,FolderGuid=source.FolderGuid};
            i.States.AddRange(source.States);i.Transitions.AddRange(source.Transitions);
            i.Revisions.AddRange(source.Revisions.Select(r=>new Revision {Guid=r.Guid,ItemGuid=r.ItemGuid,Hrid=r.Hrid,StateGuid=r.StateGuid,StateName=r.StateName,Released=r.Released,CanUpdate=r.CanUpdate}));
            return i;
        }
        public IList<Parent> ReadParents(string id)
        {
            if(!Parents.ContainsKey(id)) return new List<Parent>();
            return Parents[id].Select(p=>new Parent {WorkspaceGuid=p.WorkspaceGuid,RevisionGuid=p.RevisionGuid,Hrid=p.Hrid,
                StateName=Items.SelectMany(i=>i.Revisions).Where(r=>r.Guid==p.RevisionGuid).Select(r=>r.StateName).FirstOrDefault()??p.StateName}).ToList();
        }
        public void Transition(Candidate c,string note)
        {
            Writes.Add(c.RevisionGuid);
            if(ThrowOnWrite) throw new InvalidOperationException("Simulated lost response");
            if(!IgnoreWrite) {var revision=Items.SelectMany(i=>i.Revisions).Single(r=>r.Guid==c.RevisionGuid);revision.StateGuid=c.TargetGuid;revision.StateName="Obsolete";}
            if(OnWrite!=null) OnWrite();
        }
    }
    sealed class Journal : IAudit
    {
        public bool FailIntent;
        public readonly List<string> Events=new List<string>();
        public void Write(string name,Candidate c,string detail) {if(FailIntent && name=="intent") throw new InvalidOperationException("Disk full");Events.Add(name);}
        public void Dispose() { }
    }
    static Fake Fixture() {var f=new Fake();f.Items.Add(Make("Component"));return f;}
    static List<Candidate> Scan(Fake f) {return new RetirementEngine(f).Scan(x=>{}).ToList();}
    static int Execute(Fake f,Plan p,Journal j) {return new RetirementEngine(f).Execute(p,"Retire old revisions",j,()=>false,x=>{});}
    static int Main()
    {
        try
        {
            Test("only superseded revisions for all three types",()=>{
                var f=Fixture();f.Items.Add(Make("Symbol"));f.Items.Add(Make("Footprint"));var c=Scan(f);
                Assert(c.Count==3 && c.All(x=>x.RevisionHrid=="REV-9"),"Latest revision exposed");Assert(f.Writes.Count==0,"Scan wrote data");
            });
            Test("native latest GUID wins over lexical order",()=>{
                var f=Fixture();var c=Scan(f);Assert(c.Single().RevisionHrid=="REV-9","Lexical latest error");
                f.Items[0].LatestGuid=f.Items[0].Revisions[0].Guid;Assert(Scan(f).Single().RevisionHrid=="REV-10","Ignored native latest");
            });
            Test("single revision excluded even if Obsolete",()=>{var f=Fixture();f.Items[0].Revisions.RemoveAt(0);Assert(Scan(f).Count==0,"Single revision included");});
            Test("older Obsolete revisions excluded",()=>{var f=Fixture();f.Items[0].Revisions[0].StateName="Obsolete";Assert(Scan(f).Count==0,"Obsolete included");});
            Test("hidden newest remains protected",()=>{var f=Fixture();f.Items[0].Revisions[1].StateName="Hidden";Assert(Scan(f).Single().RevisionGuid==f.Items[0].Revisions[0].Guid,"Hidden latest selected");});
            Test("older Draft revisions remain selectable when their lifecycle permits Obsolete",()=>{var f=Fixture();f.Items[0].States[0].Name="Draft";foreach(var r in f.Items[0].Revisions){r.StateName="Draft";r.Released=false;}var c=Scan(f);Assert(c.Count==1&&c[0].Selectable&&c[0].BeforeName=="Draft","Draft revision was incorrectly excluded");});
            Test("read-only revision remains listed but blocked",()=>{var f=Fixture();f.Items[0].Revisions[0].CanUpdate=false;var c=Scan(f);Assert(c.Count==1&&!c[0].Selectable,"Permissions ignored");Reject(()=>new RetirementEngine(f).Prepare(c));});
            Test("missing direct transition blocked",()=>{var f=Fixture();f.Items[0].Transitions.Clear();Assert(!Scan(f).Single().Selectable,"Missing transition allowed");});
            Test("ambiguous Obsolete state blocked",()=>{var f=Fixture();f.Items[0].States.Add(new State {Guid=G(),Name="Obsolete",DefinitionGuid=f.Items[0].DefinitionGuid});Assert(!Scan(f).Single().Selectable,"Ambiguous target allowed");});
            Test("Obsolete in another definition not used",()=>{var f=Fixture();f.Items[0].States[1].DefinitionGuid=G();Assert(!Scan(f).Single().Selectable,"Wrong lifecycle target allowed");});
            Test("incomplete history refuses entire scan",()=>{var f=Fixture();f.Items[0].Revisions.RemoveAt(1);Reject(()=>Scan(f));});
            Test("duplicate revisions refused",()=>{var f=Fixture();f.Items[0].Revisions.Add(f.Items[0].Revisions[0]);Reject(()=>Scan(f));});
            Test("unsupported item type refused",()=>{var f=Fixture();f.Items[0].Kind="Project";Reject(()=>Scan(f));});
            Test("invalid GUID refused",()=>{var f=Fixture();f.Items[0].LatestGuid="";Reject(()=>Scan(f));});
            Test("wrong parent item membership refused",()=>{var f=Fixture();f.Items[0].Revisions[0].ItemGuid=G();Reject(()=>Scan(f));});
            Test("workspace switch after scan blocks all writes",()=>{var f=Fixture();var c=Scan(f);f.Workspace.Guid=G();Reject(()=>new RetirementEngine(f).Prepare(c));Assert(f.Writes.Count==0,"Cross workspace write");});
            Test("user switch after scan blocks",()=>{var f=Fixture();var c=Scan(f);f.Workspace.UserGuid=G();Reject(()=>new RetirementEngine(f).Prepare(c));});
            Test("changed lifecycle state blocks stale selection",()=>{var f=Fixture();var c=Scan(f);f.Items[0].Revisions[0].StateGuid=G();Reject(()=>new RetirementEngine(f).Prepare(c));});
            Test("new latest protected before execution",()=>{var f=Fixture();var e=new RetirementEngine(f);var p=e.Prepare(Scan(f));f.Items[0].Revisions.RemoveAt(1);f.Items[0].LatestGuid=f.Items[0].Revisions[0].Guid;Reject(()=>Execute(f,p,new Journal()));Assert(f.Writes.Count==0,"Latest changed");});
            Test("empty and duplicate selections blocked",()=>{var f=Fixture();var e=new RetirementEngine(f);Reject(()=>e.Prepare(new Candidate[0]));var c=Scan(f).Single();Reject(()=>e.Prepare(new[]{c,c}));});
            Test("non-Obsolete unselected parent protects model",()=>{var f=Fixture();var child=Make("Symbol");f.Items.Add(child);f.Parents[child.Revisions[0].Guid]=new List<Parent>{new Parent {WorkspaceGuid=f.Workspace.Guid,RevisionGuid=f.Items[0].LatestGuid,Hrid="latest component",StateName="Released"}};Reject(()=>new RetirementEngine(f).Prepare(Scan(f)));Assert(f.Writes.Count==0,"Referenced model changed");});
            Test("selected old parent ordered before its model",()=>{
                var f=Fixture();var model=Make("Footprint");f.Items.Add(model);var parent=f.Items[0].Revisions[0];
                f.Parents[model.Revisions[0].Guid]=new List<Parent>{new Parent {WorkspaceGuid=f.Workspace.Guid,RevisionGuid=parent.Guid,Hrid=parent.Hrid,StateName=parent.StateName}};
                var p=new RetirementEngine(f).Prepare(Scan(f).AsEnumerable().Reverse());Assert(p.Candidates[0].RevisionGuid==parent.Guid,"Wrong dependency order");
                Assert(Execute(f,p,new Journal())==2,"Did not apply plan");Assert(f.Items.All(i=>i.Revisions[1].StateName=="Released"),"Latest state changed");
            });
            Test("cross workspace dependency blocked",()=>{var f=Fixture();f.Parents[f.Items[0].Revisions[0].Guid]=new List<Parent>{new Parent {WorkspaceGuid=G(),RevisionGuid=G(),Hrid="foreign",StateName="Obsolete"}};Reject(()=>new RetirementEngine(f).Prepare(Scan(f)));});
            Test("dependency cycle blocked before mutation",()=>{var f=Fixture();var b=Make("Symbol");f.Items.Add(b);var a=f.Items[0].Revisions[0];var r=b.Revisions[0];f.Parents[a.Guid]=new List<Parent>{new Parent {WorkspaceGuid=f.Workspace.Guid,RevisionGuid=r.Guid}};f.Parents[r.Guid]=new List<Parent>{new Parent {WorkspaceGuid=f.Workspace.Guid,RevisionGuid=a.Guid}};Reject(()=>new RetirementEngine(f).Prepare(Scan(f)));});
            Test("new dependency after preview prevents transition",()=>{var f=Fixture();var p=new RetirementEngine(f).Prepare(Scan(f));f.Parents[p.Candidates[0].RevisionGuid]=new List<Parent>{new Parent {WorkspaceGuid=f.Workspace.Guid,RevisionGuid=G(),Hrid="new parent",StateName="Released"}};Reject(()=>Execute(f,p,new Journal()));Assert(f.Writes.Count==0,"Dependency race ignored");});
            Test("journal intent must persist before mutation",()=>{var f=Fixture();var p=new RetirementEngine(f).Prepare(Scan(f));Reject(()=>Execute(f,p,new Journal {FailIntent=true}));Assert(f.Writes.Count==0,"Write without journal");});
            Test("lost response stops batch without retry",()=>{var f=Fixture();f.Items.Add(Make("Symbol"));var p=new RetirementEngine(f).Prepare(Scan(f));f.ThrowOnWrite=true;var j=new Journal();Reject(()=>Execute(f,p,j));Assert(f.Writes.Count==1&&j.Events.Contains("unknown-or-rejected"),"Retried uncertain write");});
            Test("missing postcondition stops batch",()=>{var f=Fixture();var p=new RetirementEngine(f).Prepare(Scan(f));f.IgnoreWrite=true;var j=new Journal();Reject(()=>Execute(f,p,j));Assert(j.Events.Contains("verification-failed"),"Missing verification");});
            Test("cancellation prevents next write",()=>{var f=Fixture();var e=new RetirementEngine(f);var p=e.Prepare(Scan(f));Assert(e.Execute(p,"note",new Journal(),()=>true,x=>{})==0&&f.Writes.Count==0,"Cancellation ignored");});
            Test("blank audit note blocks mutation",()=>{var f=Fixture();var e=new RetirementEngine(f);var p=e.Prepare(Scan(f));Reject(()=>e.Execute(p," ",new Journal(),()=>false,x=>{}));Assert(f.Writes.Count==0,"Blank note accepted");});
            Test("latest state postcondition detects unexpected side effect",()=>{var f=Fixture();var p=new RetirementEngine(f).Prepare(Scan(f));f.OnWrite=()=>f.Items[0].Revisions[1].StateGuid=G();Reject(()=>Execute(f,p,new Journal()));});
            Test("latest rechecked during preflight",()=>{var f=Fixture();var c=Scan(f);f.OnRead=()=>{f.Items[0].LatestGuid=f.Items[0].Revisions[0].Guid;};Reject(()=>new RetirementEngine(f).Prepare(c));Assert(f.Writes.Count==0,"Latest guard missing");});
            Test("successful transition emits intent and verified",()=>{var f=Fixture();var p=new RetirementEngine(f).Prepare(Scan(f));var j=new Journal();Assert(Execute(f,p,j)==1,"Transition failed");Assert(j.Events.SequenceEqual(new[]{"intent","verified"}),"Audit ordering");Assert(f.Writes.Single()!=f.Items[0].LatestGuid,"Latest written");});
            Test("all three content kinds list old Draft revisions and protect latest Draft",()=>{var f=Fixture();f.Items.Add(Make("Symbol"));f.Items.Add(Make("Footprint"));foreach(var item in f.Items){item.States[0].Name="Draft";foreach(var r in item.Revisions){r.StateName="Draft";r.Released=false;}}var c=Scan(f);Assert(c.Count==3&&c.All(r=>r.Selectable)&&c.All(r=>r.RevisionHrid=="REV-9"),"Draft history was hidden or latest included");});
            Test("Revision ID lookup takes precedence and returns the item's old history",()=>{
                var f=Fixture();f.Items.Add(Make("Symbol"));f.Items[1].Revisions.ForEach(r=>r.Hrid="OTHER-"+r.Hrid);
                var e=new RetirementEngine(f);var result=e.Scan(new RevisionQuery(" rev-10 ",null),m=>{});
                Assert(result.Count==1&&result[0].RevisionHrid=="REV-9"&&f.Lookups==1&&f.Scans==0,"Search scanned or exposed latest");
            });
            Test("unknown Revision ID never falls back to a global scan",()=>{
                var f=Fixture();Assert(new RetirementEngine(f).Scan(new RevisionQuery("missing",null),m=>{}).Count==0&&f.Scans==0,"Unknown ID triggered scan");
            });
            Test("Revision ID respects the selected folder filter",()=>{
                var f=Fixture();var e=new RetirementEngine(f);
                Assert(e.Scan(new RevisionQuery("REV-10",new[]{G()}),m=>{}).Count==0,"Search escaped folder filter");
                Assert(e.Scan(new RevisionQuery("REV-10",new[]{f.Items[0].FolderGuid}),m=>{}).Count==1&&f.Scans==0,"Search ignored selected folder");
            });
            Test("blank Revision ID scans only the selected folders",()=>{
                var f=Fixture();f.Items.Add(Make("Symbol"));var result=new RetirementEngine(f).Scan(new RevisionQuery("  ",new[]{f.Items[0].FolderGuid}),m=>{});
                Assert(result.Count==1&&result[0].Kind=="Component"&&f.Scans==1&&f.Lookups==0,"Folder scan broadened");
            });
            Test("empty folder selection never broadens to all folders",()=>{var f=Fixture();Assert(new RetirementEngine(f).Scan(new RevisionQuery(null,new string[0]),m=>{}).Count==0,"Empty filter broadened");});
            Test("wrong Revision ID response is rejected",()=>{var f=Fixture();f.LookupOverride=Make("Symbol");f.LookupOverride.Revisions.ForEach(r=>r.Hrid="WRONG");Reject(()=>new RetirementEngine(f).Scan(new RevisionQuery("REV-10",null),m=>{}));});
            Test("workspace changes during direct lookup are rejected",()=>{var f=Fixture();f.OnFind=()=>f.Workspace.Guid=G();Reject(()=>new RetirementEngine(f).Scan(new RevisionQuery("REV-10",null),m=>{}));Assert(f.Writes.Count==0,"Lookup wrote data");});
            Test("completed transitions retain rows and can be followed by another batch",()=>{
                var f=Fixture();f.Items.Add(Make("Symbol"));var e=new RetirementEngine(f);var rows=Scan(f).Select(c=>new RevisionRow(c) {Selected=true}).ToList();
                var first=rows[0];var journal=new Journal();
                using(var audit=new ObservedAudit(journal,(name,c)=>rows.Single(r=>r.Candidate.RevisionGuid==c.RevisionGuid).AuditEvent(name)))
                    Assert(e.Execute(e.Prepare(new[]{first.Candidate}),"note",audit,()=>false,m=>{})==1,"Batch failed");
                Assert(rows.Count==2&&first.CurrentState=="Obsolete"&&!first.Selectable&&!first.Selected&&rows[1].Selected,"List or remaining selection lost");
                Assert(e.Prepare(new[]{rows[1].Candidate}).Candidates.Count==1,"Remaining batch needs scan");
            });
            Test("uncertain writes stay visible and blocked without blocking unattempted rows",()=>{
                var f=Fixture();f.Items.Add(Make("Symbol"));var e=new RetirementEngine(f);var rows=Scan(f).Select(c=>new RevisionRow(c) {Selected=true}).ToList();var plan=e.Prepare(rows.Select(r=>r.Candidate));f.ThrowOnWrite=true;
                using(var audit=new ObservedAudit(new Journal(),(name,c)=>rows.Single(r=>r.Candidate.RevisionGuid==c.RevisionGuid).AuditEvent(name)))
                    Reject(()=>e.Execute(plan,"note",audit,()=>false,m=>{}));
                rows.ForEach(r=>r.FinishAttempt());
                Assert(rows.Count==2&&rows.Count(r=>r.Selectable)==1&&rows.Count(r=>r.Selected)==1&&rows.Any(r=>r.Availability.Contains("Unverified")),"Uncertain or untouched row lost");
            });
            Test("audit failure cannot falsely mark a row as verified",()=>{
                var f=Fixture();var e=new RetirementEngine(f);var row=new RevisionRow(Scan(f).Single()) {Selected=true};var plan=e.Prepare(new[]{row.Candidate});
                using(var audit=new ObservedAudit(new Journal {FailIntent=true},(name,c)=>row.AuditEvent(name))) Reject(()=>e.Execute(plan,"note",audit,()=>false,m=>{}));
                Assert(row.Selectable&&row.Selected&&f.Writes.Count==0,"Failed journal changed UI or workspace");
            });
            Test("refresh reads only listed items and retains completed and newly protected rows",()=>{
                var f=Fixture();var extra=new Revision {Guid=G(),ItemGuid=f.Items[0].Guid,Hrid="REV-8",StateGuid=f.Items[0].Revisions[0].StateGuid,StateName="Released",CanUpdate=true};f.Items[0].Revisions.Add(extra);
                var rows=Scan(f).Select(c=>new RevisionRow(c) {Selected=true}).ToList();var target=f.Items[0].Revisions[0];target.StateName="Obsolete";target.StateGuid=f.Items[0].States[1].Guid;
                int scans=f.Scans;RevisionList.Refresh(f,rows,m=>{});
                Assert(f.Scans==scans&&f.ItemReads==1&&rows.Count==2&&rows.Count(r=>r.CurrentState=="Obsolete"&&!r.Selectable)==1,"Refresh rescanned or dropped rows");
                f.Items[0].LatestGuid=extra.Guid;RevisionList.Refresh(f,rows,m=>{});
                Assert(rows.All(r=>!r.Selectable)&&rows.Any(r=>r.Availability.Contains("Protected latest")),"Refresh exposed new latest");
            });
            Test("workspace switch blocks targeted refresh before reading items",()=>{
                var f=Fixture();var rows=Scan(f).Select(c=>new RevisionRow(c)).ToList();f.Workspace.Guid=G();Reject(()=>RevisionList.Refresh(f,rows,m=>{}));
                Assert(f.ItemReads==0&&rows.All(r=>!r.Selectable)&&rows.Count==1,"Refresh crossed workspace or dropped list");
            });
            FolderInventoryTests.Run(Test);
            ComponentFolderTreeTests.Run(Test);
            ItemPagesTests.Run(Test);
            Console.WriteLine(passed+" safety and inventory tests passed; no live workspace used.");return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
