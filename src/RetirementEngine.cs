using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public static class Id
    {
        public static string Of(string value)
        {
            Guid result;
            if (!Guid.TryParse(value, out result) || result == Guid.Empty)
                throw new InvalidOperationException("Missing or invalid Altium identifier.");
            return result.ToString("D");
        }
        public static bool Same(string a, string b) { return Of(a) == Of(b); }
        public static string Native(string value) { return Of(value).ToUpperInvariant(); }
    }
    public sealed class Workspace
    {
        public string Guid, Url, UserGuid, Name;
        public string Key { get { return Id.Of(Guid) + "|" + Url.TrimEnd('/').ToLowerInvariant() + "|" + Id.Of(UserGuid); } }
    }
    public sealed class Revision
    {
        public string Guid, ItemGuid, Hrid, StateGuid, StateName;
        public bool CanUpdate, Released;
    }
    public sealed class Transition
    {
        public string Guid, DefinitionGuid, BeforeGuid, AfterGuid;
    }
    public sealed class State
    {
        public string Guid, DefinitionGuid, Name;
    }
    public sealed class Item
    {
        public string Guid, Hrid, Name, Kind, DefinitionGuid, LatestGuid;
        public readonly List<Revision> Revisions = new List<Revision>();
        public readonly List<State> States = new List<State>();
        public readonly List<Transition> Transitions = new List<Transition>();
    }
    public sealed class Candidate
    {
        public Workspace Workspace;
        public string ItemGuid, RevisionGuid, ItemHrid, RevisionHrid, Name, Kind, LatestHrid;
        public string BeforeGuid, BeforeName, TargetGuid, TransitionGuid, Reason;
        public bool Selectable { get { return string.IsNullOrEmpty(Reason); } }
    }
    public sealed class Parent
    {
        public string WorkspaceGuid, RevisionGuid, Hrid, StateName;
    }
    public interface IGateway
    {
        Workspace ActiveWorkspace();
        IList<Item> ReadAll(Action<string> progress);
        Item ReadItem(string itemGuid);
        IList<Parent> ReadParents(string revisionGuid);
        void Transition(Candidate candidate, string note);
    }
    public interface IAudit: IDisposable
    {
        void Write(string eventName, Candidate candidate, string detail);
    }
    public sealed class Plan
    {
        internal readonly List<Candidate> Ordered = new List<Candidate>();
        public IList<Candidate> Candidates { get { return Ordered.AsReadOnly(); } }
    }
    public sealed class RetirementEngine
    {
        readonly IGateway gateway;
        public RetirementEngine(IGateway gateway) { this.gateway = gateway; }
        public static bool Obsolete(string name) { return string.Equals(name, "Obsolete", StringComparison.OrdinalIgnoreCase); }
        static void Validate(Item item)
        {
            Id.Of(item.Guid); Id.Of(item.DefinitionGuid); Id.Of(item.LatestGuid);
            if (item.Kind != "Component" && item.Kind != "Symbol" && item.Kind != "Footprint")
                throw new InvalidOperationException("This content type is not permitted.");
            if (item.Revisions.Count == 0 || item.Revisions.Select(r => Id.Of(r.Guid)).Distinct().Count() != item.Revisions.Count ||
                item.Revisions.Count(r => Id.Same(r.Guid, item.LatestGuid)) != 1 ||
                item.Revisions.Any(r => !Id.Same(r.ItemGuid, item.Guid)))
                throw new InvalidOperationException("The revision history is incomplete or inconsistent: " + item.Hrid);
            foreach (var r in item.Revisions) Id.Of(r.StateGuid);
        }
        public static Candidate Describe(Workspace workspace, Item item, Revision revision)
        {
            Validate(item);
            if (!item.Revisions.Contains(revision)) throw new InvalidOperationException("This revision does not belong to the item.");
            if (Id.Same(revision.Guid, item.LatestGuid) || Obsolete(revision.StateName)) return null;
            var c = new Candidate { Workspace=workspace, ItemGuid=Id.Of(item.Guid), RevisionGuid=Id.Of(revision.Guid),
                ItemHrid=item.Hrid, RevisionHrid=revision.Hrid, Name=item.Name, Kind=item.Kind,
                LatestHrid=item.Revisions.Single(r => Id.Same(r.Guid,item.LatestGuid)).Hrid, BeforeGuid=Id.Of(revision.StateGuid), BeforeName=revision.StateName };
            var targets = item.States.Where(s => Obsolete(s.Name) && Id.Same(s.DefinitionGuid,item.DefinitionGuid)).ToList();
            if (!revision.Released) c.Reason = "Planned / unreleased revision";
            else if (!revision.CanUpdate) c.Reason = "Insufficient write permissions";
            else if (targets.Count != 1) c.Reason = "The Obsolete state is missing or ambiguous in this lifecycle";
            else
            {
                c.TargetGuid=Id.Of(targets[0].Guid);
                var routes=item.Transitions.Where(t => Id.Same(t.DefinitionGuid,item.DefinitionGuid) &&
                    Id.Same(t.BeforeGuid,revision.StateGuid) && Id.Same(t.AfterGuid,c.TargetGuid)).ToList();
                if (routes.Count != 1) c.Reason="The direct transition to Obsolete is missing or ambiguous";
                else c.TransitionGuid=Id.Of(routes[0].Guid);
            }
            return c;
        }
        public IList<Candidate> Scan(Action<string> progress)
        {
            var workspace=gateway.ActiveWorkspace(); var key=workspace.Key;
            var items=gateway.ReadAll(progress); CheckWorkspace(key);
            if (items.Select(i => Id.Of(i.Guid)).Distinct().Count() != items.Count)
                throw new InvalidOperationException("The scan returned duplicate items.");
            var result=new List<Candidate>();
            foreach(var item in items)
                foreach(var revision in item.Revisions)
                { var c=Describe(workspace,item,revision); if(c!=null) result.Add(c); }
            return result.OrderBy(c=>c.Kind).ThenBy(c=>c.ItemHrid).ThenBy(c=>c.RevisionHrid).ToList();
        }
        void CheckWorkspace(string key)
        {
            if (gateway.ActiveWorkspace().Key != key)
                throw new InvalidOperationException("The active workspace or user changed. Scan again.");
        }
        Candidate Refresh(Candidate expected)
        {
            Item snapshot;
            return Refresh(expected,out snapshot);
        }
        Candidate Refresh(Candidate expected, out Item item)
        {
            CheckWorkspace(expected.Workspace.Key);
            item=gateway.ReadItem(expected.ItemGuid); Validate(item);
            if (!Id.Same(item.Guid,expected.ItemGuid)) throw new InvalidOperationException("Altium returned a different item.");
            var revision=item.Revisions.SingleOrDefault(r=>Id.Same(r.Guid,expected.RevisionGuid));
            var actual=revision==null ? null: Describe(expected.Workspace,item,revision);
            if(actual==null || !actual.Selectable || actual.BeforeGuid!=expected.BeforeGuid ||
                actual.TargetGuid!=expected.TargetGuid || actual.TransitionGuid!=expected.TransitionGuid || actual.Kind!=expected.Kind)
                throw new InvalidOperationException("This revision is protected or changed since the scan: "+expected.RevisionHrid+". Scan again.");
            CheckWorkspace(expected.Workspace.Key);
            return actual;
        }
        public Plan Prepare(IEnumerable<Candidate> selection)
        {
            return Prepare(selection,message=>{});
        }
        public Plan Prepare(IEnumerable<Candidate> selection, Action<string> progress)
        {
            var selected=selection.ToList();
            if(selected.Count==0) throw new InvalidOperationException("Select at least one revision.");
            var key=selected[0].Workspace.Key;
            if(selected.Any(c=>!c.Selectable || c.Workspace.Key!=key) ||
                selected.Select(c=>Id.Of(c.RevisionGuid)).Distinct().Count()!=selected.Count)
                throw new InvalidOperationException("The selection is inconsistent or contains a blocked revision.");
            foreach(var c in selected) { Refresh(c); progress("Checking: "+c.RevisionHrid); }
            var plan=new Plan(); var visiting=new HashSet<string>(); var done=new HashSet<string>();
            var byId=selected.ToDictionary(c=>Id.Of(c.RevisionGuid));
            foreach(var c in selected) { Visit(c,byId,visiting,done,plan); progress("Dependencies: "+c.RevisionHrid); }
            CheckWorkspace(key);
            return plan;
        }
        void Visit(Candidate c, Dictionary<string,Candidate> selected, HashSet<string> visiting, HashSet<string> done, Plan plan)
        {
            if(done.Contains(c.RevisionGuid)) return;
            if(!visiting.Add(c.RevisionGuid)) throw new InvalidOperationException("A dependency cycle blocks this operation.");
            foreach(var parent in gateway.ReadParents(c.RevisionGuid))
            {
                if(!Id.Same(parent.WorkspaceGuid,c.Workspace.Guid)) throw new InvalidOperationException("Dependency in another workspace: "+parent.Hrid);
                if(Obsolete(parent.StateName)) continue;
                Candidate prerequisite;
                if(!selected.TryGetValue(Id.Of(parent.RevisionGuid),out prerequisite))
                    throw new InvalidOperationException(c.RevisionHrid+" is used by "+parent.Hrid+" which is not Obsolete. Also select this eligible old parent revision, or keep the model unchanged.");
                Visit(prerequisite,selected,visiting,done,plan);
            }
            visiting.Remove(c.RevisionGuid); done.Add(c.RevisionGuid); plan.Ordered.Add(c);
        }
        public int Execute(Plan plan, string note, IAudit audit, Func<bool> cancelled, Action<string> progress)
        {
            if(plan==null || plan.Ordered.Count==0 || string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("A plan and a change note are required.");
            int count=0;
            foreach(var expected in plan.Ordered)
            {
                if(cancelled()) break;
                var actual=Refresh(expected);
                foreach(var parent in gateway.ReadParents(actual.RevisionGuid))
                    if(!Id.Same(parent.WorkspaceGuid,actual.Workspace.Guid) || !Obsolete(parent.StateName))
                        throw new InvalidOperationException("A non-Obsolete parent protects "+actual.RevisionHrid+": "+parent.Hrid);
                // Repeat after dependency reads, immediately before the only mutation.
                Item before;
                actual=Refresh(actual,out before);
                audit.Write("intent",actual,note);
                try { gateway.Transition(actual,note); }
                catch(Exception ex)
                { audit.Write("unknown-or-rejected",actual,ex.Message); throw new InvalidOperationException("The batch stopped. Check the result in Altium for "+actual.RevisionHrid+". No automatic retry was attempted.",ex); }
                var after=gateway.ReadItem(actual.ItemGuid); Validate(after);
                var updated=after.Revisions.Single(r=>Id.Same(r.Guid,actual.RevisionGuid));
                if(!Id.Same(updated.StateGuid,actual.TargetGuid) || Id.Same(after.LatestGuid,actual.RevisionGuid) ||
                    before.Revisions.Where(r=>!Id.Same(r.Guid,actual.RevisionGuid)).Any(r=>
                        !after.Revisions.Any(a=>Id.Same(a.Guid,r.Guid) && Id.Same(a.StateGuid,r.StateGuid))))
                { audit.Write("verification-failed",actual,"Unexpected state after the transition"); throw new InvalidOperationException("The transition could not be verified. The batch stopped."); }
                CheckWorkspace(actual.Workspace.Key);
                audit.Write("verified",actual,note); count++;
                progress(actual.RevisionHrid+" → Obsolete ("+count+"/"+plan.Ordered.Count+")");
            }
            return count;
        }
    }
}
