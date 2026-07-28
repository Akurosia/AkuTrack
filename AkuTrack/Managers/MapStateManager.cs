using System;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AkuTrack.Managers
{
    public class MapStateManager
    {
        private IClientState clientState;
        private IDataManager dataManager;

        private ObjTrackManager objTrackManager;

        public string filterExpression = string.Empty;
        public bool filterEnabled = false;
        
        public event Action<uint>? RegionSelectedItemChanged;
        public event Action<uint>? PlaceSelectedItemChanged;
        
        public event Action<uint>? SubSelectedItemChanged;
        public event Action<Map>? CurrentMapChanged;
        
        public enum FilteredRegions : uint
        {
            Null = 0,
            Hydaelin = 20,
            Eorzea = 21,
            TheSource = 3700,
            TheFirst = 3701
        }

        public Map currentMap { get; private set; }

        public MapStateManager(
        IClientState clientState,
        IDataManager dataManager,
        ObjTrackManager objTrackManager
        ) {
            this.clientState = clientState;
            this.dataManager = dataManager;
            this.objTrackManager = objTrackManager;
            clientState.MapIdChanged += MapChanged;
            SwitchMap(clientState.MapId);
        }

        public void RegionChange(uint rowId)
        {
            RegionSelectedItemChanged?.Invoke(rowId);
        }
        
        public void PlaceChange(uint rowId)
        {
            PlaceSelectedItemChanged?.Invoke(rowId);
        }
        
        public void SubChange(uint rowId)
        {
            SubSelectedItemChanged?.Invoke(rowId);
        }

        private async void MapChanged(uint newMapId)
        {
            SwitchMap(newMapId);
        }

        public async void SwitchMap(uint mapId) {
            if (!dataManager.GetExcelSheet<Map>().TryGetRow(mapId, out var nextMap))
            {
                return;
            }

            currentMap = nextMap;
            CurrentMapChanged?.Invoke(currentMap);
            var objs = await objTrackManager.FetchAkuGameObjectsFromAkuAPI(mapId);
            objTrackManager.downloadHashList.Clear();
            foreach (var obj in objs)
            {
                var uniqueId = obj.GetUniqueId();
                if (uniqueId is not null)
                {
                    objTrackManager.downloadHashList.TryAdd(uniqueId, obj);
                }
            }
        }
    }
}
