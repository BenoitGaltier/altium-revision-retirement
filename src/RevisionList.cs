using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public static class RevisionList
    {
        public static void Refresh(IGateway gateway,IList<RevisionRow> rows,Action<string> progress)
        {
            foreach(var group in rows.GroupBy(r=>r.Candidate.ItemGuid))
            {
                var expected=group.First().Candidate;
                try
                {
                    if(group.Any(r=>r.Candidate.Workspace.Key!=expected.Workspace.Key) || gateway.ActiveWorkspace().Key!=expected.Workspace.Key)
                        throw new InvalidOperationException("The active workspace or user changed. Search or scan again.");
                    progress("Refreshing listed item: "+expected.ItemHrid);
                    if(gateway.ActiveWorkspace().Key!=expected.Workspace.Key) throw new InvalidOperationException("The active workspace changed.");
                    var item=gateway.ReadItem(expected.ItemGuid);
                    if(!Id.Same(item.Guid,expected.ItemGuid) || gateway.ActiveWorkspace().Key!=expected.Workspace.Key)
                        throw new InvalidOperationException("The item or active workspace changed during refresh.");
                    foreach(var row in group)
                    {
                        var revision=item.Revisions.SingleOrDefault(r=>Id.Same(r.Guid,row.Candidate.RevisionGuid));
                        if(revision==null) {row.Block("Unknown","Revision unavailable; check Altium before continuing");continue;}
                        // Describe validates the complete history and native latest GUID before updating the row.
                        var candidate=RetirementEngine.Describe(expected.Workspace,item,revision);
                        row.Candidate.LatestHrid=item.Revisions.Single(r=>Id.Same(r.Guid,item.LatestGuid)).Hrid;
                        if(candidate!=null) row.Refresh(candidate);
                        else if(Id.Same(revision.Guid,item.LatestGuid)) row.Block(revision.StateName,"Protected latest revision; retained in the list");
                        else row.MarkObsolete();
                    }
                }
                catch
                {
                    foreach(var row in group) row.Block(row.CurrentState,"Refresh incomplete; check Altium and refresh this list before selecting");
                    throw;
                }
            }
        }
    }
}
