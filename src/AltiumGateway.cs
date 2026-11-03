using System;
using System.Collections.Generic;
using System.Linq;
using DXP;

namespace AltiumRevisionRetirement
{
    // Native SDK only. All calls run on Altium's UI thread; no HTTP server or external process.
    public sealed class AltiumGateway : IGateway
    {
        static readonly string[] Types = { DXP.EDMS.ContentTypeGuid.Component, DXP.EDMS.ContentTypeGuid.Symbol, DXP.EDMS.ContentTypeGuid.PcbComponent };
        static TEDMS_VaultFlagSet ReadFlags() { return new TEDMS_VaultFlagSet(TEDMS_VaultFlag.edmsWait,TEDMS_VaultFlag.edmsForceRefresh); }
        static TEDMS_GetOptionSet Options()
        {
            return new TEDMS_GetOptionSet(TEDMS_GetOption.eGetOption_GetAllItemRevisions,
                TEDMS_GetOption.eGetOption_IncludeHiddenRevisions,TEDMS_GetOption.eGetOption_ExcludeDeleted,
                TEDMS_GetOption.eGetOption_ExcludeParameters);
        }
        IEDMS_Vault Vault()
        {
            var manager=EDP.Utils.GetDXPServerManager();
            if(manager==null || !manager.IsConnected()) throw new InvalidOperationException("Se connecter au workspace dans Altium Designer.");
            var vault=manager.GetEnterpriseVault();
            if(vault==null || !vault.GetEnabled()) throw new InvalidOperationException("Le workspace actif est indisponible.");
            Id.Of(vault.GetVaultGUID()); Id.Of(vault.GetUserGUID());
            return vault;
        }
        public Workspace ActiveWorkspace()
        {
            var v=Vault();
            if(string.IsNullOrWhiteSpace(v.GetServiceURL())) throw new InvalidOperationException("URL du workspace absente.");
            return new Workspace { Guid=Id.Of(v.GetVaultGUID()), Name=v.GetHRID(), UserGuid=Id.Of(v.GetUserGUID()), Url=v.GetServiceURL() };
        }
        static void Check(IEDM_AsyncBasicResult result)
        {
            if(result==null || !result.WaitUntilDone(60000)) throw new InvalidOperationException("Réponse Altium absente ou délai dépassé. Aucun nouvel essai automatique.");
            var output=result.GetOutput();
            if(output==null || !output.GetSuccess()) throw new InvalidOperationException("Altium a refusé l'opération : "+(output==null ? "réponse absente" : output.GetMessage()));
            var records=result.GetResult();
            if(records==null || !records.GetSuccess() || records.GetFirstErrorIndex()>=0)
                throw new InvalidOperationException("Résultat incomplet/refusé : "+(records==null ? "résultat absent" : records.GetFirstErrorMessage()));
        }
        static List<T> List<T>(IInterfaceList list) where T:class
        {
            if(list==null) throw new InvalidOperationException("Liste SDK absente.");
            var result=new List<T>();
            for(int i=0;i<list.GetCount();i++)
            { var value=list.Get(i) as T; if(value==null) throw new InvalidOperationException("Objet SDK inattendu."); result.Add(value); }
            return result;
        }
        static string Kind(string guid)
        {
            if(Id.Same(guid,Types[0])) return "Component";
            if(Id.Same(guid,Types[1])) return "Symbol";
            if(Id.Same(guid,Types[2])) return "Footprint";
            throw new InvalidOperationException("Type de contenu non pris en charge.");
        }
        int Count(IEDMS_Vault vault, string folder, string type)
        {
            var count=vault.GetFolderItemsCount(folder,type,"","","",Options(),ReadFlags(),null,null);
            Check(count); if(count.GetItemsCount()<0) throw new InvalidOperationException("Comptage invalide.");
            return count.GetItemsCount();
        }
        public IList<Item> ReadAll(Action<string> progress)
        {
            var v=Vault(); var identity=ActiveWorkspace().Key;
            v.InvalidateCache();
            var folders=v.GetAllFolders(ReadFlags(),null,null); Check(folders);
            var folderIds=List<IULB_Folder>(folders.GetFolders()).Select(f=>Id.Of(f.GetGUID())).Distinct().ToList();
            folderIds.Insert(0,""); // Root items; duplicates are reconciled by GUID, never by name.
            var itemIds=new HashSet<string>();
            foreach(var folder in folderIds)
            foreach(var type in Types)
            {
                if(ActiveWorkspace().Key!=identity) throw new InvalidOperationException("Le workspace a changé pendant l'analyse.");
                int before=Count(v,folder,type);
                if(before==0) continue;
                var rows=v.GetItemsForFolder(folder,type,Options(),ReadFlags(),null,null); Check(rows);
                var found=List<IULB_Item>(rows.GetItems());
                var distinct=found.Select(i=>Id.Of(i.GetGUID())).Distinct().ToList();
                if(distinct.Count!=found.Count || distinct.Count!=before || Count(v,folder,type)!=before ||
                    found.Any(i=>!Id.Same(i.GetContentTypeGUID(),type)))
                    throw new InvalidOperationException("Inventaire partiel ou modifié dans un dossier. Relancer l'analyse.");
                foreach(var id in distinct) itemIds.Add(id);
                progress("Inventaire : "+itemIds.Count+" items");
            }
            var items=new List<Item>();
            foreach(var id in itemIds)
            {
                if(ActiveWorkspace().Key!=identity) throw new InvalidOperationException("Le workspace a changé pendant l'analyse.");
                items.Add(ReadItem(id)); progress("Lecture des révisions : "+items.Count+"/"+itemIds.Count);
            }
            return items;
        }
        public Item ReadItem(string itemGuid)
        {
            var v=Vault(); v.InvalidateCache();
            var rows=v.GetItemsByGUID(Id.Of(itemGuid),Options(),ReadFlags(),null,null); Check(rows);
            var native=rows.GetItemByGUID(itemGuid);
            if(native==null || !Id.Same(native.GetGUID(),itemGuid)) throw new InvalidOperationException("Item inaccessible ou supprimé.");
            var revisions=List<IULB_ItemRevision>(rows.GetItemRevisions());
            if(revisions.Count==0) throw new InvalidOperationException("L'API n'a pas retourné l'historique complet.");
            var latest=v.GetLastItemRevision(itemGuid);
            if(latest==null || !Id.Same(latest.GetItemGUID(),itemGuid)) throw new InvalidOperationException("Dernière révision indéterminable.");
            var item=new Item { Guid=Id.Of(native.GetGUID()), Hrid=native.GetHRID(), Name=native.GetDescription(),
                DefinitionGuid=Id.Of(native.GetLifeCycleDefinitionGUID()), LatestGuid=Id.Of(latest.GetGUID()), Kind=Kind(native.GetContentTypeGUID()) };
            var definitions=v.GetLifeCycleDefinitionsByGUID(item.DefinitionGuid,ReadFlags(),null,null); Check(definitions);
            var definition=definitions.GetLifeCycleDefinitionByGUID(item.DefinitionGuid);
            if(definition==null) throw new InvalidOperationException("Définition de lifecycle inaccessible.");
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
                if(!Id.Same(r.GetItemGUID(),item.Guid)) throw new InvalidOperationException("Révision retournée pour un autre item.");
                var state=item.States.SingleOrDefault(s=>Id.Same(s.Guid,r.GetLifeCycleStateGUID()));
                if(state==null) throw new InvalidOperationException("État absent de la définition de lifecycle.");
                item.Revisions.Add(new Revision { Guid=Id.Of(r.GetGUID()), ItemGuid=Id.Of(r.GetItemGUID()), Hrid=r.GetHRID(),
                    StateGuid=Id.Of(r.GetLifeCycleStateGUID()), StateName=state.Name, Released=r.GetReleaseDate()>0, CanUpdate=r.GetCanUpdate() && v.GetCanUpdate() });
            }
            return item;
        }
        public IList<Parent> ReadParents(string revisionGuid)
        {
            var v=Vault(); v.InvalidateCache();
            var rows=v.GetItemRevisionLinks("",Id.Of(revisionGuid),ReadFlags(),null,null); Check(rows);
            var result=new List<Parent>();
            foreach(var link in List<IULB_ItemRevisionLink>(rows.GetItemRevisionLinks()))
            {
                if(!Id.Same(link.GetChildItemRevisionGUID(),revisionGuid) || !Id.Same(link.GetChildVaultGUID(),v.GetVaultGUID()))
                    throw new InvalidOperationException("Dépendance retournée incohérente.");
                if(!Id.Same(link.GetParentVaultGUID(),v.GetVaultGUID())) throw new InvalidOperationException("Dépendance inter-workspaces : opération bloquée.");
                var parents=v.GetItemRevisionsByGUID(link.GetParentItemRevisionGUID(),ReadFlags(),null,null); Check(parents);
                var parent=parents.GetItemRevisionByGUID(link.GetParentItemRevisionGUID());
                if(parent==null) throw new InvalidOperationException("Parent inaccessible : opération bloquée.");
                var parentState=v.LifeCycleStateByGUID(parent.GetLifeCycleStateGUID());
                if(parentState==null) throw new InvalidOperationException("État du parent inconnu.");
                result.Add(new Parent { WorkspaceGuid=Id.Of(link.GetParentVaultGUID()),RevisionGuid=Id.Of(parent.GetGUID()),Hrid=parent.GetHRID(),StateName=parentState.GetHRID() });
            }
            return result;
        }
        public void Transition(Candidate c, string note)
        {
            if(ActiveWorkspace().Key!=c.Workspace.Key) throw new InvalidOperationException("Workspace actif différent.");
            var v=Vault();
            // Check again after the intent journal has been flushed to disk.
            var item=ReadItem(c.ItemGuid);
            var revision=item.Revisions.SingleOrDefault(r=>Id.Same(r.Guid,c.RevisionGuid));
            var current=revision==null ? null : RetirementEngine.Describe(c.Workspace,item,revision);
            if(current==null || !current.Selectable || current.BeforeGuid!=c.BeforeGuid ||
                current.TransitionGuid!=c.TransitionGuid || current.TargetGuid!=c.TargetGuid || ActiveWorkspace().Key!=c.Workspace.Key)
                throw new InvalidOperationException("La révision ou le workspace a changé avant la transition.");
            // Single native lifecycle event. Never UpdateItemRevisions, change a definition, release data, or cascade.
            var change=v.CreateLifeCycleStateChange();
            change.SetGUID(System.Guid.NewGuid().ToString("D"));
            change.SetItemRevisionGUID(Id.Of(c.RevisionGuid));
            change.SetStateTransitionGUID(Id.Of(c.TransitionGuid));
            change.SetStateAfterGUID(Id.Of(c.TargetGuid));
            change.SetNote(note);
            var list=v.CreateInterfaceList(); list.Add(change);
            var result=v.AddLifeCycleStateChanges(list,new TEDMS_VaultFlagSet(TEDMS_VaultFlag.edmsWait,TEDMS_VaultFlag.edmsRefreshAfterAddUpdate),null,null);
            Check(result);
            var changes=List<IULB_LifeCycleStateChange>(result.GetLifeCycleStateChanges());
            if(changes.Count!=1 || !Id.Same(changes[0].GetItemRevisionGUID(),c.RevisionGuid) || !Id.Same(changes[0].GetStateAfterGUID(),c.TargetGuid))
                throw new InvalidOperationException("Confirmation de transition absente ou inattendue.");
        }
    }
}
