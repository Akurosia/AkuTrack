using AkuTrack.ApiTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AkuTrack.Managers
{
    public class ObjTrackManager : IDisposable
    {
        private readonly IChatGui chat;
        private readonly IPluginLog log;
        private readonly IObjectTable objectTable;
        private readonly IFramework framework;
        private readonly IClientState clientState;
        private readonly IDataManager dataManager;
        private readonly UploadManager uploadManager;

        public Dictionary<string, AkuGameObject> seenHashList = new();
        public Dictionary<ulong, AkuGameObject> seenUIDList = new();
        public List<AkuGameObject> liveAkuObjects = new();
        public List<AkuGameObject> toUpload = new();

        public ConcurrentDictionary<string, AkuGameObject> downloadHashList = new();
        public ConcurrentDictionary<string, AkuGameObject> currentMapDownloadHashList = new();
        private bool isDownloadActive = false;

        private TimeSpan lastUpdate = new(0);
        private TimeSpan execDelay = new(0, 0, 1);

        public ObjTrackManager(
            IFramework framework,
            IClientState clientState,
            IDataManager dataManager,
            IDalamudPluginInterface pluginInterface,
            IChatGui chat,
            IPluginLog log,
            IObjectTable objectTable,
            UploadManager uploadManager
        )
        {
            log.Debug($"ObjTrackManager online!");
            this.chat = chat;
            this.log = log;
            this.objectTable = objectTable;
            this.framework = framework;
            this.clientState = clientState;
            this.dataManager = dataManager;
            this.uploadManager = uploadManager;

            framework.Update += Tick;
            clientState.MapIdChanged += MapChanged;
            MapChanged(clientState.MapId);
        }

        public void CleanSeen() {
            seenHashList.Clear();
            seenUIDList.Clear();
            toUpload.Clear();
        }

        public void Dispose()
        {
            framework.Update -= Tick;
            clientState.MapIdChanged -= MapChanged;
        }

        private async void MapChanged(uint newMapId) {
            log.Debug($"Player changed map to {newMapId}!");
            isDownloadActive = true;
            var objs = await FetchAkuGameObjectsFromAkuAPI(newMapId);
            currentMapDownloadHashList.Clear();
            foreach (var obj in objs)
            {
                var uniqueId = obj.GetUniqueId();
                if (uniqueId is not null)
                {
                    currentMapDownloadHashList.TryAdd(uniqueId, obj);
                }
            }
            isDownloadActive = false;
        }

        private void Tick(IFramework framework)
        {
            lastUpdate += framework.UpdateDelta;
            if (lastUpdate > execDelay)
            {
                DoUpdate(framework);
                lastUpdate = new(0);
            }
        }

        private async void DoUpdate(IFramework framework)
        {
            //log.Debug("Tick!");
            var aObjs = GetAkuGameObjects();
            liveAkuObjects = aObjs;
            //log.Debug($"{liveAkuObjects.Count} live");
            if(isDownloadActive) {
                return;
            }
            var stepOne = FilterUploadIgnore(aObjs);
            //log.Debug($"{stepOne.Count} after stepOne");
            var stepTwo = FilterSeen(stepOne);
            //log.Debug($"{stepTwo.Count} after stepTwo");
            var ups = FilterDownloaded(stepTwo);
            //log.Debug($"{ups.Count} left");

            if (ups.Count > 0)
            {
                toUpload.AddRange(ups);
                var res = await uploadManager.DoUpload("duckit/", toUpload);
                //log.Debug($"Uploading was {res}");
                if (res)
                {
                    toUpload.Clear();
                }
                else
                {
                    log.Debug($"Uploading failed!");
                }
            }
        }

        private List<AkuGameObject> GetAkuGameObjects() {
            List<AkuGameObject> res = new();
            foreach (var obj in objectTable) {
                if (obj is null)
                {
                    continue;
                }

                var uid = AkuGameObject.GetUniqueId(obj);
                if (uid == null)
                {
                    log.Debug($"ERROR: Could not GetUniqueId from obj.bid {obj.BaseId} name {obj.Name}");
                    continue;
                }
                var aObj = new AkuGameObject(obj, clientState);
                res.Add(aObj);
            }
            return res;
        }

        private List<AkuGameObject> FilterDownloaded(List<AkuGameObject> input) {
            List<AkuGameObject> res = new();
            foreach (var obj in input)
            {
                var uniqueId = obj.GetUniqueId();
                if(uniqueId is not null && currentMapDownloadHashList.ContainsKey(uniqueId)) {
                    log.Verbose($"Not uploading {obj.bid} ({uniqueId}) because it was in download.");
                    continue;
                }
                res.Add(obj);
            }
            return res;
        }

        private List<AkuGameObject> FilterUploadIgnore(List<AkuGameObject> input) {
            List<AkuGameObject> res = new();
            foreach (var obj in input)
            {
                // no players, mounts, minion pets, housing items, wings/umbrellas, retainers
                if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ||
                    obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount ||
                    obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion ||
                    obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.HousingEventObject ||
                    obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Ornament ||
                    obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Retainer
                    )
                {
                    continue;
                }
                if (obj.battleNpcSubKind is not null)
                {
                    if (obj.battleNpcSubKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet ||
                        obj.battleNpcSubKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Buddy ||
                        obj.battleNpcSubKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.RaceChocobo ||
                        obj.battleNpcSubKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.NpcPartyMember)
                    {
                        continue;
                    }
                }
                // Check if this object is owned by a player (e.g. a battlepet) or has been aggroed
                var owner = objectTable.SearchById(obj.ownerId);
                if (owner != null && owner.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
                {
                    log.Debug($"Obj {obj.name} [{obj.bid}] is player owned. Not sending. @ x/y/z: {obj.pos.X}/{obj.pos.Y}/{obj.pos.Z}");
                    continue;
                }
                res.Add(obj);
            }
            return res;
        }

        private List<AkuGameObject> FilterSeen(List<AkuGameObject> input)
        {
            List<AkuGameObject> objects = new();
            foreach (var obj in input)
            {
                ///
                /// Check if we know the hash of this obj already
                ///

                var uid = obj.GetUniqueId();
                if(uid == null) {
                    log.Error($"Something terrible happened! -> uid was null for {obj.bid}");
                    continue;
                }
                if (seenHashList.ContainsKey(uid))
                {
                    var oldObj = seenHashList[uid];
                    if (uid != oldObj.GetUniqueId())
                    {
                        log.Error($"Something terrible happened! -> Check {uid} but seenList fetches {oldObj.GetUniqueId()}");
                    }
                    if (obj.objectKind.ToString() != oldObj.t || obj.bid != oldObj.bid)
                    {
                        log.Error($"Something terrible happened! oldObj and obj:");
                        log.Error($"{oldObj.t} || {obj.objectKind.ToString()}");
                        log.Error($"{oldObj.mid} || {clientState.MapId}");
                        log.Error($"{oldObj.bid} || {obj.bid}");
                    }
                    if(obj.nid != oldObj.nid) {
                        log.Error($"Something terrible happened! oldObj and obj:");
                        log.Error($"{oldObj.nid} || {obj.nid}");
                    }
                    continue;
                }

                ///
                /// End hash check
                ///

                if(obj.unique_ingame_id is null) {
                    log.Error("Something terrible happened! AkuGameObject without unique id.");
                    continue;
                }

                // Check if there has been an object in this slot already and if it is likely still the same one but has moved (moving changes the uid hash)
                if (seenUIDList.ContainsKey((ulong)obj.unique_ingame_id)) {
                    var oldObj = seenUIDList[(ulong)obj.unique_ingame_id];
                    if(!HasTableContentChanged(oldObj, obj))
                        continue;
                }
                seenHashList.Add(uid, obj);
                // Remove here because it could also be that the go was here and changed
                seenUIDList.Remove((ulong)obj.unique_ingame_id);
                seenUIDList.Add((ulong)obj.unique_ingame_id, obj);
                objects.Add(obj);
            }
            return objects;
        }

        private bool HasTableContentChanged(AkuGameObject oldObj, AkuGameObject obj) {
            if(obj.bid == oldObj.bid && obj.objectKind == oldObj.objectKind) {
                return false;
            }
            log.Debug($"Obj changed in table old: {obj.name}({obj.bid}) new: {obj.name}/{obj.bid}");
            return true;
        }

        public async Task<List<AkuGameObject>> FetchAkuGameObjectsFromAkuAPI(uint mid)
        {
            List<AkuGameObject> res = new();
            var objs = await uploadManager.DownloadMapContentFromAPI(mid);
            var downloadedTypes = objs
                .GroupBy(obj => obj.t)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}");
            log.Debug($"Downloaded map {mid} content types: {string.Join(", ", downloadedTypes)}");
            foreach (var obj in objs)
            {
                if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)
                {
                    try
                    {
                        var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>(clientState.ClientLanguage).GetRow(obj.bid);
                        obj.name = StringExtensions.ToUpper(y.Singular.ToString(), true, true, false, clientState.ClientLanguage);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        log.Debug($"{obj.t} ID {obj.bid} is not in range of ENpcResident");
                    }
                }
                if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
                {
                    if (obj.nid == null)
                        continue;
                    try
                    {
                        var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>(clientState.ClientLanguage).GetRow((uint)obj.nid);
                        obj.name = StringExtensions.ToUpper(y.Singular.ToString(), true, true, false, clientState.ClientLanguage);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        log.Debug($"{obj.t} ID {obj.nid} is not in range of BNpcName");
                    }
                }
                if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
                {
                    try
                    {
                        var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.EObjName>(clientState.ClientLanguage).GetRow(obj.bid);
                        obj.name = StringExtensions.ToUpper(y.Singular.ToString(), true, true, false, clientState.ClientLanguage);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        log.Debug($"{obj.t} ID {obj.bid} is not in range of EObjName");
                    }
                }
                if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint)
                {
                    try
                    {
                        var y = dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>(clientState.ClientLanguage).GetRow(obj.bid);
                        //FIXME: Find the gathering node's name. It is in GatheringPointName but how to get there?
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        log.Debug($"{obj.t} ID {obj.bid} is not in range of GatheringPoint");
                    }
                }
                if (obj.GetUniqueId() == null)
                {
                    log.Debug($"ERROR: Could not GetUniqueId of obj.bid {obj.bid} name {obj.name}");
                    continue;
                }
                res.Add(obj);
            }
            return res;
        }
    }
}
