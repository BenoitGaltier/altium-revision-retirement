using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public static class ItemPages
    {
        // Native releases may treat page zero as the first page or alias it to page one.
        // Accept that initial alias only; verify coverage against the independent item count.
        public static IList<string> Read(int expected, Func<int,IList<string>> read)
        {
            if(expected<0) throw new InvalidOperationException("Invalid paged inventory count.");
            var found=new HashSet<string>();
            string first=null;
            for(int page=0;page<=expected+2;page++)
            {
                var rows=read(page);
                if(rows==null) throw new InvalidOperationException("Altium returned no inventory page.");
                var ids=rows.Select(Id.Of).ToList();
                if(ids.Distinct().Count()!=ids.Count) throw new InvalidOperationException("Altium returned duplicate items within an inventory page.");
                string signature=string.Join("|",ids.OrderBy(id=>id,StringComparer.Ordinal));
                if(page==0) first=signature;
                if(page==1 && signature==first && ids.Count>0) continue;
                int before=found.Count;
                foreach(string id in ids) found.Add(id);
                if(found.Count>expected) throw new InvalidOperationException("The folder inventory changed while reading its pages.");
                if(found.Count==expected) return found.ToList();
                if(page==0 && ids.Count==0) continue;
                if(found.Count==before) break;
            }
            throw new InvalidOperationException("The paged folder inventory is incomplete. Expected "+expected+" items; received "+found.Count+".");
        }
    }
}
