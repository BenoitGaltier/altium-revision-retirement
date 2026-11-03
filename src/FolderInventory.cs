using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public static class FolderInventory
    {
        // An empty string is not the vault root: native folder queries require real folder GUIDs.
        public static IList<string> Read(IEnumerable<string> folderGuids, IEnumerable<string> contentTypes,
            Func<string,string,int> count, Func<string,string,IList<string>> read, Action<string> progress)
        {
            var folders=folderGuids.Select(Id.Of).Distinct().ToList();
            var types=contentTypes.Select(Id.Of).Distinct().ToList();
            var found=new HashSet<string>();
            for(int index=0;index<folders.Count;index++)
            foreach(var type in types)
            {
                string folder=folders[index];
                progress("Reading folders: "+(index+1)+"/"+folders.Count+"; "+found.Count+" items found");
                int before=count(folder,type);
                if(before<0) throw new InvalidOperationException("Altium returned an invalid item count.");
                var rows=read(folder,type);
                if(rows==null) throw new InvalidOperationException("Altium returned no inventory response.");
                var ids=rows.Select(Id.Of).ToList();
                if(ids.Distinct().Count()!=ids.Count || ids.Count!=before || count(folder,type)!=before)
                    throw new InvalidOperationException("A folder inventory is incomplete or changed during the scan. Scan again.");
                foreach(var id in ids) found.Add(id);
            }
            return found.OrderBy(id=>id,StringComparer.Ordinal).ToList();
        }
    }
}
