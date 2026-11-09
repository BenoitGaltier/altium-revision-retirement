using System;
using System.Linq;
using AltiumRevisionRetirement;

static class ComponentFolderTreeTests
{
    sealed class NativeShape
    {
        public WorkspaceFolder Record;
        public readonly System.Collections.Generic.List<NativeShape> SubFolders=new System.Collections.Generic.List<NativeShape>();
    }
    static string G() {return Guid.NewGuid().ToString("D");}
    static void Assert(bool value) {if(!value) throw new Exception("Components tree contract failed");}
    static void Reject(Action action) {try {action();}catch(InvalidOperationException){return;}throw new Exception("Expected folder tree rejection");}
    public static void Run(Action<string,Action> test)
    {
        test("folder selector expands selected branches without including siblings",()=>{
            var root=new WorkspaceFolder {Guid=G(),Name="Components"};var a=new WorkspaceFolder {Guid=G(),Name="A",ParentGuid=root.Guid};
            var b=new WorkspaceFolder {Guid=G(),Name="B",ParentGuid=root.Guid};var deep=new WorkspaceFolder {Guid=G(),Name="Deep",ParentGuid=a.Guid};var tree=new[]{root,a,b,deep};
            var selected=ComponentFolderTree.SelectSubtrees(tree,new[]{a.Guid});Assert(selected.Count==2&&selected.Contains(deep.Guid)&&!selected.Contains(b.Guid));
            Assert(ComponentFolderTree.SelectSubtrees(tree,new[]{root.Guid}).Count==4&&ComponentFolderTree.SelectSubtrees(tree,new string[0]).Count==0);
            Reject(()=>ComponentFolderTree.SelectSubtrees(tree,new[]{G()}));
        });
        test("SDK nested SubFolder objects are flattened at every depth",()=>{
            var root=new NativeShape {Record=new WorkspaceFolder {Guid=G(),Name="Components"}};
            var symbols=new NativeShape {Record=new WorkspaceFolder {Guid=G(),Name="Symbols",ParentGuid=root.Record.Guid}};
            var family=new NativeShape {Record=new WorkspaceFolder {Guid=G(),Name="Family",ParentGuid=symbols.Record.Guid}};
            var deep=new NativeShape {Record=new WorkspaceFolder {Guid=G(),Name="Deep",ParentGuid=family.Record.Guid}};
            root.SubFolders.Add(symbols);symbols.SubFolders.Add(family);family.SubFolders.Add(deep);
            var flat=ComponentFolderTree.Flatten(new[]{root},f=>f.Record,f=>f.SubFolders);
            var result=ComponentFolderTree.Read(new[]{root.Record},id=>flat,m=>{});
            Assert(result.Count==4&&result.Any(f=>f.Guid==deep.Record.Guid));
        });
        test("Components is the explicit root and all nested folders are retained",()=>{
            var root=new WorkspaceFolder {Guid=G(),Name="Components"};
            var symbols=new WorkspaceFolder {Guid=G(),Name="Symbols",ParentGuid=root.Guid};
            var family=new WorkspaceFolder {Guid=G(),Name="Symbols family",ParentGuid=symbols.Guid};
            var deep=new WorkspaceFolder {Guid=G(),Name="Fourth level",ParentGuid=family.Guid};
            var other=new WorkspaceFolder {Guid=G(),Name="Projects"};
            string queried=null;
            var result=ComponentFolderTree.Read(new[]{root,other},id=>{queried=id;return new[]{root,symbols,family,deep};},m=>{});
            Assert(Id.Same(queried,root.Guid)&&result.Count==4&&result.Any(f=>f.Guid==deep.Guid)&&!result.Any(f=>f.Guid==other.Guid));
        });
        test("five initial SDK entries are not mistaken for the full component hierarchy",()=>{
            var roots=new[]{new WorkspaceFolder {Guid=G(),Name="Components"},new WorkspaceFolder {Guid=G(),Name="Projects"},new WorkspaceFolder {Guid=G(),Name="Templates"},new WorkspaceFolder {Guid=G(),Name="Other"},new WorkspaceFolder {Guid=G(),Name="Archive"}};
            var children=Enumerable.Range(0,15).Select(i=>new WorkspaceFolder {Guid=G(),Name="Family "+i,ParentGuid=roots[0].Guid}).ToArray();
            var result=ComponentFolderTree.Read(roots,id=>children,m=>{});Assert(result.Count==16);
        });
        test("missing Components root is reported rather than returning an empty scan",()=>{Reject(()=>ComponentFolderTree.Read(new[]{new WorkspaceFolder {Guid=G(),Name="Projects"}},id=>new WorkspaceFolder[0],m=>{}));});
        test("incomplete recursive parent chain is blocked",()=>{var root=new WorkspaceFolder {Guid=G(),Name="Components"};Reject(()=>ComponentFolderTree.Read(new[]{root},id=>new[]{new WorkspaceFolder {Guid=G(),Name="Orphan",ParentGuid=G()}},m=>{}));});
        test("recursive folder cycles are blocked",()=>{var root=new WorkspaceFolder {Guid=G(),Name="Components"};var a=new WorkspaceFolder {Guid=G(),Name="A"};var b=new WorkspaceFolder {Guid=G(),Name="B",ParentGuid=a.Guid};a.ParentGuid=b.Guid;Reject(()=>ComponentFolderTree.Read(new[]{root},id=>new[]{a,b},m=>{}));});
    }
}
