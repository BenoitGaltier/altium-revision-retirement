using System;
using System.Collections.Generic;
using System.Linq;
using DXP;

namespace AltiumRevisionRetirement
{
    // Native SDK only. All calls run on Altium's UI thread; no HTTP server or external process.
    public sealed class AltiumGateway: IGateway
    {
        static readonly string[] Types = { DXP.EDMS.ContentTypeGuid.Component, DXP.EDMS.ContentTypeGuid.Symbol, DXP.EDMS.ContentTypeGuid.PcbComponent };
        static TEDMS_VaultFlagSet ReadFlags() { return new TEDMS_VaultFlagSet(TEDMS_VaultFlag.edmsWait,TEDMS_VaultFlag.edmsForceRefresh); }
        static TEDMS_GetOptionSet Options()
        {
            return new TEDMS_GetOptionSet(TEDMS_GetOption.eGetOption_GetAllItemRevisions,TEDMS_GetOption.eGetOption_IncludeHiddenRevisions,TEDMS_GetOption.eGetOption_ExcludeDeleted,
                TEDMS_GetOption.eGetOption_ExcludeParameters);
        }
        static TEDMS_GetOptionSet InventoryOptions()
        {
            // Native item counts include deleted tombstones even with ExcludeDeleted.
            // Verify that same inventory, then remove inactive items before reading histories.
            return new TEDMS_GetOptionSet(TEDMS_GetOption.eGetOption_IncludeDeleted,TEDMS_GetOption.eGetOption_ExcludeParameters);
        }
        IEDMS_Vault Vault()
        {
            var manager=EDP.Utils.GetDXPServerManager();
            if(manager==null || !manager.IsConnected()) throw new InvalidOperationException("Sign in to the workspace in Altium Designer.");
            var vault=manager.GetEnterpriseVault();
            if(vault==null || !vault.GetEnabled()) throw new InvalidOperationException("The active workspace is unavailable.");
            Id.Of(vault.GetVaultGUID()); Id.Of(vault.GetUserGUID());
            return vault;
        }
        public Workspace ActiveWorkspace()
        {
            var v=Vault();
            if(string.IsNullOrWhiteSpace(v.GetServiceURL())) throw new InvalidOperationException("The workspace service URL is missing.");
            return new Workspace { Guid=Id.Of(v.GetVaultGUID()), Name=v.GetHRID(), UserGuid=Id.Of(v.GetUserGUID()), Url=v.GetServiceURL() };
        }
        static void Check(IEDM_AsyncBasicResult result)
        {
            // The native SDK expresses this timeout in seconds.
            if(result==null || !result.WaitUntilDone(60)) throw new InvalidOperationException("Altium returned no response or timed out. No automatic retry was attempted.");
            var output=result.GetOutput();
            if(output==null || !output.GetSuccess()) throw new InvalidOperationException("Altium rejected the operation: "+(output==null ? "no response": output.GetMessage()));
            var records=result.GetResult();
            if(records==null || !records.GetSuccess() || records.GetFirstErrorIndex()>=0)
                throw new InvalidOperationException("Incomplete or rejected result: "+(records==null ? "no result": records.GetFirstErrorMessage()));
        }
        static List<T> List<T>(IInterfaceList list) where T:class
        {
            if(list==null) throw new InvalidOperationException("The SDK returned no list.");
            var result=new List<T>();
            for(int i=0;i<list.GetCount();i++)
            { var value=list.Get(i) as T; if(value==null) throw new InvalidOperationException("The SDK returned an unexpected object."); result.Add(value); }
            return result;
        }
        static string Kind(string guid)
        {
            if(Id.Same(guid,Types[0])) return "Component";
            if(Id.Same(guid,Types[1])) return "Symbol";
            if(Id.Same(guid,Types[2])) return "Footprint";
            throw new InvalidOperationException("This content type is not supported.");
        }
        int Count(IEDMS_Vault vault, string folder, string type)
        {
            folder=Id.Native(folder); type=Id.Native(type);
            var count=vault.GetFolderItemsCount(folder,type,"","","",InventoryOptions(),ReadFlags(),null,null);
            Check(count); if(count.GetItemsCount()<0) throw new InvalidOperationException("Altium returned an invalid count.");
            return count.GetItemsCount();
        }
        static WorkspaceFolder Folder(IULB_Folder folder)
        { return new WorkspaceFolder {Guid=folder.GetGUID(),ParentGuid=folder.GetParentFolderGUID(),Name=folder.GetHRID(),SharedName=folder.GetSharedName()}; }
        static IEnumerable<IULB_Folder> SubFolders(IULB_Folder folder)
        { for(int i=0;i<folder.GetSubFolderCount();i++) yield return folder.GetSubFolder(i); }
        public IList<Item> ReadAll(Action<string> progress)
        {
            var v=Vault(); var identity=ActiveWorkspace().Key;
            v.InvalidateCache();
            var folders=v.GetAllFolders(ReadFlags(),null,null); Check(folders);
            var initial=List<IULB_Folder>(folders.GetFolders()).Select(Folder).ToList();
            var tree=ComponentFolderTree.Read(initial,root=>
            {
                if(ActiveWorkspace().Key!=identity) throw new InvalidOperationException("The active workspace changed during folder discovery.");
                var nested=v.GetFolders(root,"",true,ReadFlags(),null,null); Check(nested);
                return ComponentFolderTree.Flatten(List<IULB_Folder>(nested.GetFolders()),Folder,SubFolders);
            },progress);
            var folderIds=tree.Select(f=>f.Guid).ToList();
            var names=tree.ToDictionary(f=>Id.Of(f.Guid),f=>f.Name);
            var active=new Dictionary<string,bool>();
            var inventoryIds=FolderInventory.Read(folderIds,Types,
                (folder,type)=>
                {
                    if(ActiveWorkspace().Key!=identity) throw new InvalidOperationException("The active workspace changed during the scan.");
                    return Count(v,folder,type);
                },
                (folder,type)=>
                {
                return ItemPages.Read(Count(v,folder,type),page=>
                {
                    progress("Reading items in Components / "+names[Id.Of(folder)]);
                    var rows=v.GetItemsPerPageForFolder(Id.Native(folder),Id.Native(type),"","","",InventoryOptions(),ReadFlags(),null,null,page,250); Check(rows);
                    var found=List<IULB_Item>(rows.GetItems());
                    if(found.Any(i=>!Id.Same(i.GetContentTypeGUID(),type)))
                        throw new InvalidOperationException("Altium returned an item with a different content type.");
                    foreach(var item in found)
                    {
                        string id=Id.Of(item.GetGUID());bool previous;
                        if(active.TryGetValue(id,out previous) && previous!=item.GetIsActive())
                            throw new InvalidOperationException("An item was deleted or restored during the inventory. Scan again.");
                        active[id]=item.GetIsActive();
                    }
                    return found.Select(i=>Id.Of(i.GetGUID())).ToList();
                });
                },progress);
            var itemIds=FolderInventory.ActiveItems(inventoryIds,active);
            var items=new List<Item>();
            foreach(var id in itemIds)
            {
                if(ActiveWorkspace().Key!=identity) throw new InvalidOperationException("The active workspace changed during the scan.");
                items.Add(ReadItem(id)); progress("Reading revisions: "+items.Count+"/"+itemIds.Count);
            }
            progress("Inventory complete: "+folderIds.Count+" folders; "+items.Count+" active items; "+items.Sum(i=>i.Revisions.Count)+" revisions; "+(inventoryIds.Count-itemIds.Count)+" deleted items excluded.");
            return items;
        }
        public Item ReadItem(string itemGuid)
        {
            var v=Vault(); v.InvalidateCache(); itemGuid=Id.Native(itemGuid);
            var rows=v.GetItemsByGUID(itemGuid,Options(),ReadFlags(),null,null); Check(rows);
            var native=rows.GetItemByGUID(itemGuid);
            if(native==null || !native.GetIsActive() || !Id.Same(native.GetGUID(),itemGuid)) throw new InvalidOperationException("The item is inaccessible or deleted.");
            var revisions=List<IULB_ItemRevision>(rows.GetItemRevisions());
            var embedded=native.GetRevisions();
            if(embedded!=null) revisions.AddRange(List<IULB_ItemRevision>(embedded));
            foreach(var group in revisions.GroupBy(r=>Id.Of(r.GetGUID())))
                if(group.Select(r=>Id.Of(r.GetLifeCycleStateGUID())).Distinct().Count()!=1)
                    throw new InvalidOperationException("The revision state changed while its history was being read.");
            revisions=revisions.GroupBy(r=>Id.Of(r.GetGUID())).Select(g=>g.First()).ToList();
            if(revisions.Count==0) throw new InvalidOperationException("The API did not return the complete revision history.");
            var latest=v.GetLastItemRevision(itemGuid);
            if(latest==null || !Id.Same(latest.GetItemGUID(),itemGuid)) throw new InvalidOperationException("The latest revision could not be determined.");
            var item=new Item { Guid=Id.Of(native.GetGUID()), Hrid=native.GetHRID(), Name=native.GetDescription(),
                DefinitionGuid=Id.Of(native.GetLifeCycleDefinitionGUID()), LatestGuid=Id.Of(latest.GetGUID()), Kind=Kind(native.GetContentTypeGUID()) };
            var definitions=v.GetLifeCycleDefinitionsByGUID(Id.Native(item.DefinitionGuid),ReadFlags(),null,null); Check(definitions);
            var definition=definitions.GetLifeCycleDefinitionByGUID(Id.Native(item.DefinitionGuid));
            if(definition==null) throw new InvalidOperationException("The lifecycle definition is inaccessible.");
            for(int i=0;i<definition.GetStageCount();i++)
            {
                var stage=definition.GetStage(i);
                for(int j=0;j<stage.GetStateCount();j++)
                { var s=stage.GetState(j); item.States.Add(new State {Guid=Id.Of(s.GetGUID()),Name=s.GetHRID(),DefinitionGuid=Id.Of(s.GetDefinitionGUID())}); }
            }
            for(int i=0;i<definition.GetStateTransitionCount();i++)
            {
                var t=definition.GetStateTransition(i);
                item.Transitions.Add(new Transition { Guid=Id.Of(t.GetGUID()),DefinitionGuid=Id.Of(t.GetDefinitionGUID()),BeforeGuid=Id.Of(t.GetStateBeforeGUID()),AfterGuid=Id.Of(t.GetStateAfterGUID()) });
            }
            foreach(var r in revisions)
            {
                if(!Id.Same(r.GetItemGUID(),item.Guid)) throw new InvalidOperationException("Altium returned a revision for another item.");
                var state=item.States.SingleOrDefault(s=>Id.Same(s.Guid,r.GetLifeCycleStateGUID()));
                if(state==null) throw new InvalidOperationException("The revision state is missing from its lifecycle definition.");
                item.Revisions.Add(new Revision { Guid=Id.Of(r.GetGUID()), ItemGuid=Id.Of(r.GetItemGUID()), Hrid=r.GetHRID(),
                    StateGuid=Id.Of(r.GetLifeCycleStateGUID()), StateName=state.Name, Released=r.GetReleaseDate()>0, CanUpdate=r.GetCanUpdate() && v.GetCanUpdate() });
            }
            return item;
        }
        public IList<Parent> ReadParents(string revisionGuid)
        {
            var v=Vault(); v.InvalidateCache();
            var rows=v.GetItemRevisionLinks("",Id.Native(revisionGuid),ReadFlags(),null,null); Check(rows);
            var result=new List<Parent>();
            foreach(var link in List<IULB_ItemRevisionLink>(rows.GetItemRevisionLinks()))
            {
                if(!Id.Same(link.GetChildItemRevisionGUID(),revisionGuid) || !Id.Same(link.GetChildVaultGUID(),v.GetVaultGUID()))
                    throw new InvalidOperationException("Altium returned an inconsistent parent reference.");
                if(!Id.Same(link.GetParentVaultGUID(),v.GetVaultGUID())) throw new InvalidOperationException("A cross-workspace dependency blocks this operation.");
                var parents=v.GetItemRevisionsByGUID(link.GetParentItemRevisionGUID(),ReadFlags(),null,null); Check(parents);
                var parent=parents.GetItemRevisionByGUID(link.GetParentItemRevisionGUID());
                if(parent==null) throw new InvalidOperationException("An inaccessible parent blocks this operation.");
                var parentState=v.LifeCycleStateByGUID(parent.GetLifeCycleStateGUID());
                if(parentState==null) throw new InvalidOperationException("The parent revision state is unknown.");
                result.Add(new Parent { WorkspaceGuid=Id.Of(link.GetParentVaultGUID()),RevisionGuid=Id.Of(parent.GetGUID()),Hrid=parent.GetHRID(),StateName=parentState.GetHRID() });
            }
            return result;
        }
        public void Transition(Candidate c, string note)
        {
            if(ActiveWorkspace().Key!=c.Workspace.Key) throw new InvalidOperationException("The active workspace has changed.");
            var v=Vault();
            // Check again after the intent journal has been flushed to disk.
            var item=ReadItem(c.ItemGuid);
            var revision=item.Revisions.SingleOrDefault(r=>Id.Same(r.Guid,c.RevisionGuid));
            var current=revision==null ? null: RetirementEngine.Describe(c.Workspace,item,revision);
            if(current==null || !current.Selectable || current.BeforeGuid!=c.BeforeGuid ||
                current.TransitionGuid!=c.TransitionGuid || current.TargetGuid!=c.TargetGuid || ActiveWorkspace().Key!=c.Workspace.Key)
                throw new InvalidOperationException("The revision or workspace changed before the transition.");
            // Single native lifecycle event. Never UpdateItemRevisions, change a definition, release data, or cascade.
            var change=v.CreateLifeCycleStateChange();
            change.SetGUID(System.Guid.NewGuid().ToString("D").ToUpperInvariant());
            change.SetItemRevisionGUID(Id.Native(c.RevisionGuid));
            change.SetStateTransitionGUID(Id.Native(c.TransitionGuid));
            change.SetStateAfterGUID(Id.Native(c.TargetGuid));
            change.SetNote(note);
            var list=v.CreateInterfaceList(); list.Add(change);
            var result=v.AddLifeCycleStateChanges(list,new TEDMS_VaultFlagSet(TEDMS_VaultFlag.edmsWait,TEDMS_VaultFlag.edmsRefreshAfterAddUpdate),null,null);
            Check(result);
            var changes=List<IULB_LifeCycleStateChange>(result.GetLifeCycleStateChanges());
            if(changes.Count!=1 || !Id.Same(changes[0].GetItemRevisionGUID(),c.RevisionGuid) || !Id.Same(changes[0].GetStateAfterGUID(),c.TargetGuid))
                throw new InvalidOperationException("The transition acknowledgement is missing or unexpected.");
        }
    }
}
