using System;
using System.Collections.Generic;
using System.Linq;

namespace AltiumRevisionRetirement
{
    public sealed class WorkspaceFolder
    {
        public string Guid, ParentGuid, Name, SharedName;
        internal bool IsComponentsRoot
        {
            get { return string.Equals((Name??"").Trim().Trim('/','\\'),"Components",StringComparison.OrdinalIgnoreCase) ||
                string.Equals((SharedName??"").Trim().Trim('/','\\'),"Components",StringComparison.OrdinalIgnoreCase); }
        }
    }
    public static class ComponentFolderTree
    {
        public static IList<WorkspaceFolder> Flatten<T>(IEnumerable<T> roots,Func<T,WorkspaceFolder> describe,Func<T,IEnumerable<T>> children)
        {
            var pending=new Stack<T>(roots);var found=new Dictionary<string,WorkspaceFolder>();
            while(pending.Count>0)
            {
                var native=pending.Pop();var folder=describe(native);string id=Id.Of(folder.Guid);
                if(found.ContainsKey(id)) continue;
                found.Add(id,folder);
                foreach(var child in children(native)) pending.Push(child);
            }
            return found.Values.ToList();
        }
        public static IList<WorkspaceFolder> Read(IEnumerable<WorkspaceFolder> initial,
            Func<string,IList<WorkspaceFolder>> recursiveRead, Action<string> progress)
        {
            var seeds=initial.ToList();
            var roots=seeds.Where(f=>f.IsComponentsRoot).GroupBy(f=>Id.Of(f.Guid)).Select(g=>g.First()).ToList();
            if(roots.Count!=1)
                throw new InvalidOperationException("The Components root folder could not be identified unambiguously. SDK folders: "+string.Join(", ",seeds.Select(f=>f.Name??f.SharedName??"(unnamed)")));
            var root=roots[0];string rootId=Id.Of(root.Guid);
            progress("Discovering the complete Components folder tree...");
            var descendants=recursiveRead(Id.Native(rootId));
            if(descendants==null) throw new InvalidOperationException("Altium returned no recursive Components folder response.");
            var byId=new Dictionary<string,WorkspaceFolder>();byId[rootId]=root;
            foreach(var folder in descendants) byId[Id.Of(folder.Guid)]=folder;
            foreach(var pair in byId)
            {
                var current=pair.Value;var visited=new HashSet<string>();
                while(!Id.Same(current.Guid,rootId))
                {
                    if(!visited.Add(Id.Of(current.Guid))) throw new InvalidOperationException("Altium returned a cycle in the Components folder tree.");
                    WorkspaceFolder parent;
                    if(string.IsNullOrWhiteSpace(current.ParentGuid) || !byId.TryGetValue(Id.Of(current.ParentGuid),out parent))
                        throw new InvalidOperationException("The recursive Components folder response is incomplete: "+current.Name);
                    current=parent;
                }
            }
            progress("Components folder tree: "+byId.Count+" folders, including all returned descendants.");
            return byId.Values.OrderBy(f=>f.Name,StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
