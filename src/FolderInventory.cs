using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public static class FolderInventory
    {
        public static IList<string> ActiveItems(IEnumerable<string> inventory,IDictionary<string,bool> active)
        {
            var ids=inventory.Select(Id.Of).ToList();
            if(ids.Any(id=>!active.ContainsKey(id)) || active.Keys.Any(id=>!ids.Contains(Id.Of(id))))
                throw new InvalidOperationException("The native active-item inventory is incomplete.");
            return ids.Where(id=>active[id]).ToList();
        }
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
                int after=count(folder,type),unique=ids.Distinct().Count();
                if(unique!=ids.Count || ids.Count!=before || after!=before)
                    throw new InvalidOperationException("Folder inventory verification failed. Expected "+before+" items; received "+ids.Count+" ("+unique+" unique); final count "+after+". Folder "+Id.Native(folder)+", content type "+Id.Native(type)+".");
                foreach(var id in ids) found.Add(id);
            }
            return found.OrderBy(id=>id,StringComparer.Ordinal).ToList();
        }
    }
}
