using System;
using System.Collections.Generic;
using System.Linq;
using AltiumRevisionRetirement;

static class FolderInventoryTests
{
    static string G() {return Guid.NewGuid().ToString("D");}
    static void Assert(bool value) {if(!value) throw new Exception("Folder inventory contract failed");}
    static void Reject(Action action) {try {action();}catch(InvalidOperationException){return;}throw new Exception("Expected inventory rejection");}
    public static void Run(Action<string,Action> test)
    {
        test("deleted tombstones count toward completeness but never enter revision reads",()=>{string live=G(),deleted=G();var result=FolderInventory.ActiveItems(new[]{live,deleted},new Dictionary<string,bool>{{live,true},{deleted,false}});Assert(result.Count==1&&result[0]==live);});
        test("missing native active flag blocks the inventory",()=>Reject(()=>FolderInventory.ActiveItems(new[]{G()},new Dictionary<string,bool>())));
        test("unexpected native active flag blocks the inventory",()=>Reject(()=>FolderInventory.ActiveItems(new string[0],new Dictionary<string,bool>{{G(),true}})));
        test("native SDK identifiers retain Altium uppercase GUID formatting",()=>{string id=G();Assert(Id.Native(id)==id.ToUpperInvariant());Assert(Id.Same(id,Id.Native(id)));});
        test("scan queries only real returned folders, including the real root folder",()=>{
            string root=G(),nested=G(),component=G(),symbol=G(),footprint=G(),one=G(),two=G();
            var calls=new List<string>();
            var ids=FolderInventory.Read(new[]{root,nested},new[]{component,symbol,footprint},
                (folder,type)=>{Assert(folder==root||folder==nested);Assert(!string.IsNullOrEmpty(folder));calls.Add(folder+"|"+type);return folder==root&&type==component||folder==nested&&type==symbol ? 1 : 0;},
                (folder,type)=>(IList<string>)(folder==root&&type==component ? new[]{one} : folder==nested&&type==symbol ? new[]{two} : new string[0]),message=>{});
            Assert(ids.Count==2&&ids.Contains(one)&&ids.Contains(two));
            foreach(var folder in new[]{root,nested}) foreach(var type in new[]{component,symbol,footprint}) Assert(calls.Contains(folder+"|"+type));
        });
        test("empty folder list does not synthesize a blank root query",()=>{
            Assert(FolderInventory.Read(new string[0],new[]{G()},(f,t)=>{throw new Exception("Unexpected query");},(f,t)=>{throw new Exception("Unexpected read");},m=>{}).Count==0);
        });
        test("blank folder identifier rejected before native calls",()=>{
            int calls=0;Reject(()=>FolderInventory.Read(new[]{""},new[]{G()},(f,t)=>{calls++;return 0;},(f,t)=>new string[0],m=>{}));Assert(calls==0);
        });
        test("duplicate folder identifiers are normalized and read once",()=>{
            string folder=G();int calls=0;FolderInventory.Read(new[]{folder,folder.ToUpperInvariant()},new[]{G()},(f,t)=>{calls++;return 0;},(f,t)=>new string[0],m=>{});Assert(calls==2);
        });
        test("truncated native folder response blocks the scan",()=>{Reject(()=>FolderInventory.Read(new[]{G()},new[]{G()},(f,t)=>2,(f,t)=>new[]{G()},m=>{}));});
        test("changed native folder count blocks the scan",()=>{int count=0;Reject(()=>FolderInventory.Read(new[]{G()},new[]{G()},(f,t)=>++count,(f,t)=>new[]{G()},m=>{}));});
        test("duplicate item rows block the scan",()=>{string item=G();Reject(()=>FolderInventory.Read(new[]{G()},new[]{G()},(f,t)=>2,(f,t)=>new[]{item,item},m=>{}));});
        test("missing inventory response blocks the scan",()=>{Reject(()=>FolderInventory.Read(new[]{G()},new[]{G()},(f,t)=>1,(f,t)=>null,m=>{}));});
        test("zero native count cannot silently hide returned items",()=>{Reject(()=>FolderInventory.Read(new[]{G()},new[]{G()},(f,t)=>0,(f,t)=>new[]{G()},m=>{}));});
        test("empty folders are cross-checked and report progress for cancellation",()=>{int updates=0,reads=0;FolderInventory.Read(new[]{G(),G()},new[]{G()},(f,t)=>0,(f,t)=>{reads++;return new string[0];},m=>updates++);Assert(updates==2&&reads==2);});
    }
}
