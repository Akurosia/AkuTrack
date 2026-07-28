using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AkuTrack.Windows
{
    public class TopBar
    {
        private const float ComboBoxWidth = 200.0f;

        private readonly IPluginLog log;
        private readonly IDataManager dataManager;
        private readonly MapStateManager mapStateManager;
        private readonly Vector4 panelColor = new(0.11f, 0.085f, 0.043f, 0.92f);
        private readonly Vector4 textColor = new(1.0f, 0.92f, 0.68f, 1.0f);

        private readonly Dictionary<uint, string> regions = new();
        private readonly Dictionary<uint, string> places = new();
        private readonly Dictionary<uint, string> subs = new();
        private int selectedRegionIndex;
        private int selectedPlacesIndex;
        private int selectedSubsIndex;
        private string typeAheadCombo = string.Empty;
        private string typeAheadBuffer = string.Empty;
        private int typeAheadTargetIndex = -1;
        private bool typeAheadScrollPending;
        private bool typeAheadFocusPending;

        public TopBar(
            IPluginLog log,
            IDataManager dataManager,
            MapStateManager mapStateManager)
        {
            this.log = log;
            this.dataManager = dataManager;
            this.mapStateManager = mapStateManager;

            mapStateManager.RegionSelectedItemChanged += RegionChanged;
            mapStateManager.PlaceSelectedItemChanged += PlaceChanged;
            mapStateManager.SubSelectedItemChanged += SubChanged;
            mapStateManager.CurrentMapChanged += SyncSelectionToMap;

            foreach (var map in dataManager.GetExcelSheet<Map>())
            {
                var region = map.PlaceNameRegion.Value;
                if (region.RowId == 0 || Enum.IsDefined(typeof(MapStateManager.FilteredRegions), region.RowId))
                {
                    continue;
                }

                regions.TryAdd(region.RowId, region.Name.ToString());
            }

            SyncSelectionToMap(mapStateManager.currentMap);
        }

        public void Draw(string mapPath, string cursorPositionText)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var barSize = new Vector2(ImGui.GetContentRegionAvail().X, 58.0f * scale);

            using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, panelColor);
            using var topBar = ImRaii.Child("top_child", barSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!topBar)
            {
                return;
            }

            ImGui.SetCursorPos(new Vector2(8.0f * scale, 5.0f * scale));
            DrawMapPicker();

            var textY = 35.0f * scale;
            ImGui.SetCursorPos(new Vector2(8.0f * scale, textY));
            ImGui.TextColored(textColor, mapPath);

            if (!string.IsNullOrWhiteSpace(cursorPositionText))
            {
                ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(cursorPositionText).X - 12.0f * scale);
                ImGui.TextColored(textColor, cursorPositionText);
            }
        }

        private void DrawMapPicker()
        {
            if (regions.Count == 0)
            {
                return;
            }

            selectedRegionIndex = Math.Clamp(selectedRegionIndex, 0, regions.Count - 1);
            DrawTypeAheadCombo("Region", regions, selectedRegionIndex, index => selectedRegionIndex = index, rowId => mapStateManager.RegionChange(rowId));

            if (places.Count == 0)
            {
                return;
            }

            ImGui.SameLine();
            selectedPlacesIndex = Math.Clamp(selectedPlacesIndex, 0, places.Count - 1);
            DrawTypeAheadCombo("Place", places, selectedPlacesIndex, index => selectedPlacesIndex = index, rowId => mapStateManager.PlaceChange(rowId));

            if (subs.Count == 0)
            {
                return;
            }

            ImGui.SameLine();
            selectedSubsIndex = Math.Clamp(selectedSubsIndex, 0, subs.Count - 1);
            DrawTypeAheadCombo("Sub", subs, selectedSubsIndex, index => selectedSubsIndex = index, rowId => mapStateManager.SubChange(rowId));
        }

        private void DrawTypeAheadCombo(string label, Dictionary<uint, string> entries, int selectedIndex, Action<int> setSelectedIndex, Action<uint> selectRow)
        {
            ImGui.SetNextItemWidth(ComboBoxWidth * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo(label, entries.ElementAt(selectedIndex).Value))
            {
                if (typeAheadCombo != label)
                {
                    typeAheadCombo = label;
                    typeAheadBuffer = string.Empty;
                    typeAheadTargetIndex = -1;
                    typeAheadScrollPending = false;
                    typeAheadFocusPending = true;
                }

                ImGui.SetNextItemWidth(ComboBoxWidth * ImGuiHelpers.GlobalScale);
                if (typeAheadFocusPending)
                {
                    ImGui.SetKeyboardFocusHere();
                    typeAheadFocusPending = false;
                }

                if (ImGui.InputText($"##{label}_location_search", ref typeAheadBuffer, 128))
                {
                    UpdateTypeAheadTarget(entries, selectedIndex);
                }

                if (!string.IsNullOrWhiteSpace(typeAheadBuffer))
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Clear##{label}_location_search_clear"))
                    {
                        typeAheadBuffer = string.Empty;
                        typeAheadTargetIndex = -1;
                        typeAheadScrollPending = false;
                    }
                }

                ImGui.Separator();

                var focusIndex = typeAheadTargetIndex >= 0 ? typeAheadTargetIndex : selectedIndex;
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries.ElementAt(i);
                    var isSelected = selectedIndex == i;
                    var isTypeAheadTarget = typeAheadCombo == label && typeAheadTargetIndex == i;
                    if (ImGui.Selectable(entry.Value, isSelected || isTypeAheadTarget))
                    {
                        setSelectedIndex(i);
                        selectRow(entry.Key);
                        ResetTypeAhead();
                    }

                    if (focusIndex == i)
                    {
                        ImGui.SetItemDefaultFocus();
                        if (typeAheadScrollPending)
                        {
                            ImGui.SetScrollHereY(0.5f);
                            typeAheadScrollPending = false;
                        }
                    }
                }

                ImGui.EndCombo();
                return;
            }

            if (typeAheadCombo == label)
            {
                ResetTypeAhead();
            }
        }

        private void UpdateTypeAheadTarget(Dictionary<uint, string> entries, int selectedIndex)
        {
            typeAheadTargetIndex = FindNextMatchingEntry(entries, typeAheadBuffer, selectedIndex, true);
            typeAheadScrollPending = typeAheadTargetIndex >= 0;
        }

        private static int FindNextMatchingEntry(Dictionary<uint, string> entries, string search, int startIndex, bool includeStart)
        {
            if (string.IsNullOrWhiteSpace(search) || entries.Count == 0)
            {
                return -1;
            }

            for (var offset = includeStart ? 0 : 1; offset <= entries.Count; offset++)
            {
                var index = (startIndex + offset) % entries.Count;
                if (entries.ElementAt(index).Value.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private void ResetTypeAhead()
        {
            typeAheadCombo = string.Empty;
            typeAheadBuffer = string.Empty;
            typeAheadTargetIndex = -1;
            typeAheadScrollPending = false;
            typeAheadFocusPending = false;
        }

        private void RegionChanged(uint rowId)
        {
            log.Debug($"The region has been changed to {rowId}");
            RebuildPlaces(rowId);
        }

        private void PlaceChanged(uint rowId)
        {
            log.Debug($"The place has been changed to {rowId}");
            if (!dataManager.GetExcelSheet<Map>().TryGetRow(rowId, out var selectedMap))
            {
                return;
            }

            var name = selectedMap.PlaceName.Value.Name.ToString();
            selectedSubsIndex = 0;
            RebuildSubsForPlaceName(name);

            mapStateManager.SwitchMap(rowId);
        }

        private void SubChanged(uint rowId)
        {
            mapStateManager.SwitchMap(rowId);
        }

        private void SyncSelectionToMap(Map map)
        {
            if (map.RowId == 0)
            {
                return;
            }

            selectedRegionIndex = GetIndexByKey(regions, map.PlaceNameRegion.RowId);
            RebuildPlaces(map.PlaceNameRegion.RowId);

            var placeName = map.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
            selectedPlacesIndex = GetIndexByValue(places, placeName);
            RebuildSubsForPlaceName(placeName);
            selectedSubsIndex = subs.Count == 0 ? 0 : GetIndexByKey(subs, map.RowId);
        }

        private void RebuildPlaces(uint regionId)
        {
            places.Clear();
            subs.Clear();
            selectedPlacesIndex = 0;
            selectedSubsIndex = 0;

            foreach (var map in dataManager.GetExcelSheet<Map>()
                .Where(map => map.PlaceNameRegion.RowId == regionId)
                .OrderBy(map => map.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty)
                .ThenBy(map => map.RowId))
            {
                var placeName = map.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(placeName) || places.ContainsValue(placeName))
                {
                    continue;
                }

                places.Add(map.RowId, placeName);
            }
        }

        private void RebuildSubsForPlaceName(string placeName)
        {
            subs.Clear();
            if (string.IsNullOrWhiteSpace(placeName))
            {
                return;
            }

            var maps = dataManager.GetExcelSheet<Map>()
                .Where(map => map.PlaceName.ValueNullable?.Name.ToString() == placeName)
                .OrderBy(map => map.PlaceNameSub.ValueNullable?.Name.ToString() ?? string.Empty)
                .ThenBy(map => map.RowId)
                .ToList();

            if (maps.Count <= 1)
            {
                return;
            }

            foreach (var map in maps)
            {
                var subName = map.PlaceNameSub.ValueNullable?.Name.ToString();
                subs.TryAdd(map.RowId, $"{(string.IsNullOrWhiteSpace(subName) ? placeName : subName)} ({map.RowId})");
            }
        }

        private static int GetIndexByKey(Dictionary<uint, string> values, uint key)
        {
            var index = values.Keys.ToList().IndexOf(key);
            return index < 0 ? 0 : index;
        }

        private static int GetIndexByValue(Dictionary<uint, string> values, string value)
        {
            var index = values.Values.ToList().FindIndex(entry => entry == value);
            return index < 0 ? 0 : index;
        }
    }
}
