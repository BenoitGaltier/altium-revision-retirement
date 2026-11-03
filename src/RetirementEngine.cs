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
                throw new InvalidOperationException("Identifiant Altium absent ou invalide.");
            return result.ToString("D");
        }
        public static bool Same(string a, string b) { return Of(a) == Of(b); }
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
    public interface IAudit : IDisposable
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
                throw new InvalidOperationException("Type de contenu non autorisé.");
            if (item.Revisions.Count == 0 || item.Revisions.Select(r => Id.Of(r.Guid)).Distinct().Count() != item.Revisions.Count ||
                item.Revisions.Count(r => Id.Same(r.Guid, item.LatestGuid)) != 1 ||
                item.Revisions.Any(r => !Id.Same(r.ItemGuid, item.Guid)))
                throw new InvalidOperationException("Historique des révisions incomplet ou incohérent : " + item.Hrid);
            foreach (var r in item.Revisions) Id.Of(r.StateGuid);
        }
        public static Candidate Describe(Workspace workspace, Item item, Revision revision)
        {
            Validate(item);
            if (!item.Revisions.Contains(revision)) throw new InvalidOperationException("Révision étrangère à l'item.");
            if (Id.Same(revision.Guid, item.LatestGuid) || Obsolete(revision.StateName)) return null;
            var c = new Candidate { Workspace=workspace, ItemGuid=Id.Of(item.Guid), RevisionGuid=Id.Of(revision.Guid),
                ItemHrid=item.Hrid, RevisionHrid=revision.Hrid, Name=item.Name, Kind=item.Kind,
                LatestHrid=item.Revisions.Single(r => Id.Same(r.Guid,item.LatestGuid)).Hrid, BeforeGuid=Id.Of(revision.StateGuid), BeforeName=revision.StateName };
            var targets = item.States.Where(s => Obsolete(s.Name) && Id.Same(s.DefinitionGuid,item.DefinitionGuid)).ToList();
            if (!revision.Released) c.Reason = "Révision planifiée / non publiée";
            else if (!revision.CanUpdate) c.Reason = "Droits de modification insuffisants";
            else if (targets.Count != 1) c.Reason = "État Obsolete absent ou ambigu dans ce lifecycle";
            else
            {
                c.TargetGuid=Id.Of(targets[0].Guid);
                var routes=item.Transitions.Where(t => Id.Same(t.DefinitionGuid,item.DefinitionGuid) &&
                    Id.Same(t.BeforeGuid,revision.StateGuid) && Id.Same(t.AfterGuid,c.TargetGuid)).ToList();
                if (routes.Count != 1) c.Reason="Transition directe vers Obsolete absente ou ambiguë";
                else c.TransitionGuid=Id.Of(routes[0].Guid);
            }
            return c;
        }
        public IList<Candidate> Scan(Action<string> progress)
        {
            var workspace=gateway.ActiveWorkspace(); var key=workspace.Key;
            var items=gateway.ReadAll(progress); CheckWorkspace(key);
            if (items.Select(i => Id.Of(i.Guid)).Distinct().Count() != items.Count)
                throw new InvalidOperationException("Items dupliqués dans l'analyse.");
            var result=new List<Candidate>();
            foreach(var item in items)
                foreach(var revision in item.Revisions)
                { var c=Describe(workspace,item,revision); if(c!=null) result.Add(c); }
            return result.OrderBy(c=>c.Kind).ThenBy(c=>c.ItemHrid).ThenBy(c=>c.RevisionHrid).ToList();
        }
        void CheckWorkspace(string key)
        {
            if (gateway.ActiveWorkspace().Key != key)
                throw new InvalidOperationException("Le workspace actif ou l'utilisateur a changé. Relancer l'analyse.");
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
            if (!Id.Same(item.Guid,expected.ItemGuid)) throw new InvalidOperationException("Item retourné incorrect.");
            var revision=item.Revisions.SingleOrDefault(r=>Id.Same(r.Guid,expected.RevisionGuid));
            var actual=revision==null ? null : Describe(expected.Workspace,item,revision);
            if(actual==null || !actual.Selectable || actual.BeforeGuid!=expected.BeforeGuid ||
                actual.TargetGuid!=expected.TargetGuid || actual.TransitionGuid!=expected.TransitionGuid || actual.Kind!=expected.Kind)
                throw new InvalidOperationException("Révision protégée ou modifiée depuis l'analyse : "+expected.RevisionHrid+". Relancer l'analyse.");
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
            if(selected.Count==0) throw new InvalidOperationException("Sélectionner au moins une révision.");
            var key=selected[0].Workspace.Key;
            if(selected.Any(c=>!c.Selectable || c.Workspace.Key!=key) ||
                selected.Select(c=>Id.Of(c.RevisionGuid)).Distinct().Count()!=selected.Count)
                throw new InvalidOperationException("Sélection incohérente ou révision bloquée.");
            foreach(var c in selected) { Refresh(c); progress("Vérification : "+c.RevisionHrid); }
            var plan=new Plan(); var visiting=new HashSet<string>(); var done=new HashSet<string>();
            var byId=selected.ToDictionary(c=>Id.Of(c.RevisionGuid));
            foreach(var c in selected) { Visit(c,byId,visiting,done,plan); progress("Dépendances : "+c.RevisionHrid); }
            CheckWorkspace(key);
            return plan;
        }
        void Visit(Candidate c, Dictionary<string,Candidate> selected, HashSet<string> visiting, HashSet<string> done, Plan plan)
        {
            if(done.Contains(c.RevisionGuid)) return;
            if(!visiting.Add(c.RevisionGuid)) throw new InvalidOperationException("Cycle de dépendances : opération bloquée.");
            foreach(var parent in gateway.ReadParents(c.RevisionGuid))
            {
                if(!Id.Same(parent.WorkspaceGuid,c.Workspace.Guid)) throw new InvalidOperationException("Dépendance vers un autre workspace : "+parent.Hrid);
                if(Obsolete(parent.StateName)) continue;
                Candidate prerequisite;
                if(!selected.TryGetValue(Id.Of(parent.RevisionGuid),out prerequisite))
                    throw new InvalidOperationException(c.RevisionHrid+" est utilisé par "+parent.Hrid+" non Obsolete. Sélectionner aussi cette ancienne révision, ou conserver le modèle.");
                Visit(prerequisite,selected,visiting,done,plan);
            }
            visiting.Remove(c.RevisionGuid); done.Add(c.RevisionGuid); plan.Ordered.Add(c);
        }
        public int Execute(Plan plan, string note, IAudit audit, Func<bool> cancelled, Action<string> progress)
        {
            if(plan==null || plan.Ordered.Count==0 || string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("Plan ou note absent.");
            int count=0;
            foreach(var expected in plan.Ordered)
            {
                if(cancelled()) break;
                var actual=Refresh(expected);
                foreach(var parent in gateway.ReadParents(actual.RevisionGuid))
                    if(!Id.Same(parent.WorkspaceGuid,actual.Workspace.Guid) || !Obsolete(parent.StateName))
                        throw new InvalidOperationException("Une dépendance non Obsolete protège "+actual.RevisionHrid+" : "+parent.Hrid);
                // Repeat after dependency reads, immediately before the only mutation.
                Item before;
                actual=Refresh(actual,out before);
                audit.Write("intent",actual,note);
                try { gateway.Transition(actual,note); }
                catch(Exception ex)
                { audit.Write("unknown-or-rejected",actual,ex.Message); throw new InvalidOperationException("Opération arrêtée. Résultat à vérifier dans Altium pour "+actual.RevisionHrid+". Aucun nouvel essai automatique.",ex); }
                var after=gateway.ReadItem(actual.ItemGuid); Validate(after);
                var updated=after.Revisions.Single(r=>Id.Same(r.Guid,actual.RevisionGuid));
                if(!Id.Same(updated.StateGuid,actual.TargetGuid) || Id.Same(after.LatestGuid,actual.RevisionGuid) ||
                    before.Revisions.Where(r=>!Id.Same(r.Guid,actual.RevisionGuid)).Any(r=>
                        !after.Revisions.Any(a=>Id.Same(a.Guid,r.Guid) && Id.Same(a.StateGuid,r.StateGuid))))
                { audit.Write("verification-failed",actual,"État inattendu après transition"); throw new InvalidOperationException("Vérification après transition impossible. Arrêt du lot."); }
                CheckWorkspace(actual.Workspace.Key);
                audit.Write("verified",actual,note); count++;
                progress(actual.RevisionHrid+" → Obsolete ("+count+"/"+plan.Ordered.Count+")");
            }
            return count;
        }
    }
}
