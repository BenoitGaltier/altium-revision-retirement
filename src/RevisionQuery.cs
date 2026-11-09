using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public sealed class RevisionQuery
    {
        readonly List<string> folders;
        public readonly string RevisionId;
        public bool HasRevisionId { get { return RevisionId.Length>0; } }
        // Null means the complete Components tree. An empty selection means no folders.
        public IList<string> FolderGuids { get { return folders==null ? null : folders.AsReadOnly(); } }
        public RevisionQuery():this(null,null) { }
        public RevisionQuery(string revisionId,IEnumerable<string> folderGuids)
        {
            RevisionId=(revisionId??"").Trim();
            folders=folderGuids==null ? null : folderGuids.Select(Id.Of).Distinct().ToList();
        }
        public bool Includes(string folderGuid)
        { return folders==null || folders.Contains(Id.Of(folderGuid)); }
    }
    public sealed class RevisionRow
    {
        public Candidate Candidate { get; private set; }
        public string CurrentState { get; private set; }
        public string Availability { get; private set; }
        public bool Selected;
        bool retired, uncertain;
        public bool Selectable { get { return !retired && !uncertain && Candidate.Selectable; } }
        public RevisionRow(Candidate candidate) { Refresh(candidate); }
        public void AuditEvent(string name,Candidate actual=null)
        {
            if(actual!=null) Candidate=actual;
            if(name=="intent") { uncertain=true;Selected=false;Availability="Transition pending verification"; }
            else if(name=="verified") { uncertain=false;retired=true;Selected=false;CurrentState="Obsolete";Availability="Transition verified; retained in the list"; }
            else if(name=="unknown-or-rejected" || name=="verification-failed")
            { uncertain=true;Selected=false;Availability="Unverified result; check the Altium timeline, then refresh this list"; }
        }
        public void FinishAttempt()
        { if(uncertain) Availability="Unverified result; check the Altium timeline, then refresh this list"; }
        public void Refresh(Candidate candidate)
        {
            Candidate=candidate;CurrentState=candidate.BeforeName;retired=false;uncertain=false;
            Availability=candidate.Selectable ? "Direct transition available; dependencies will be checked before applying" : candidate.Reason;
            if(!Selectable) Selected=false;
        }
        public void Block(string state,string reason)
        { CurrentState=state;uncertain=true;Selected=false;Availability=reason; }
        public void MarkObsolete()
        { uncertain=false;retired=true;Selected=false;CurrentState="Obsolete";Availability="Obsolete state confirmed by refresh; retained in the list"; }
    }
    // Persist the audit record before changing the display. This observer never writes to Altium.
    internal sealed class ObservedAudit: IAudit
    {
        readonly IAudit inner;
        readonly Action<string,Candidate> observe;
        public ObservedAudit(IAudit inner,Action<string,Candidate> observe) { this.inner=inner;this.observe=observe; }
        public void Write(string name,Candidate candidate,string detail) { inner.Write(name,candidate,detail);observe(name,candidate); }
        public void Dispose() { } // Non-owning observer; the caller disposes the journal.
    }
}
