using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace AkuTrack.Windows
{
    public class SearchWindow : Window, IDisposable
    {
        private enum SearchResultAction
        {
            None,
            ItemExtraData,
            OpenMap,
            OpenMapAndFlag,
            FindDownloadedObjectAndFlag,
        }

        private sealed record SearchResult(
            string Category,
            string Name,
            string Subtitle,
            uint RowId,
            uint IconId,
            uint MapId,
            uint TerritoryId,
            Vector3? Position,
            SearchResultAction Action,
            uint? MatchBaseId = null,
            string? MatchType = null);

        private readonly IPluginLog log;
        private readonly IFramework framework;
        private readonly IDataManager dataManager;
        private readonly ITextureProvider textureProvider;
        private readonly WindowSystem windowSystem;
        private readonly UploadManager uploadManager;
        private readonly AllaganToolsIpc allaganToolsIpc;
        private readonly Configuration configuration;
        private bool de;
        private bool en;
        private bool fr;
        private bool ja;
        private bool searchItems = true;
        private bool searchQuests = true;
        private bool searchGathering = true;
        private bool searchFishing = true;
        private bool searchSpearfishing = true;
        private bool searchSightseeing = true;
        private bool searchDuties = true;
        private bool searchMapMarkers = true;
        private string input = string.Empty;
        private List<SearchResult>? results;

        public SearchWindow(
            IPluginLog log,
            IFramework framework,
            IDataManager dataManager,
            ITextureProvider textureProvider,
            WindowSystem windowSystem,
            UploadManager uploadManager,
            AllaganToolsIpc allaganToolsIpc,
            ConfigWindow configWindow,
            Configuration configuration) : base("AkuTrack2 - Search##akutrack_search")
        {
            this.log = log;
            this.framework = framework;
            this.dataManager = dataManager;
            this.textureProvider = textureProvider;
            this.windowSystem = windowSystem;
            this.uploadManager = uploadManager;
            this.allaganToolsIpc = allaganToolsIpc;
            this.configuration = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(360, 360),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            ImGui.TextUnformatted("Search");
            DrawLanguageControls();
            DrawTypeControls();

            ImGui.SetNextItemWidth(MathF.Max(120.0f, ImGui.GetContentRegionAvail().X - 84.0f * ImGuiHelpers.GlobalScale));
            if (ImGui.InputTextWithHint("##akutrack_search_input", "Name, id, type, location...", ref input, 128, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                ExecuteSearch();
            }
            ImGui.SameLine();
            if (ImGui.Button("Search"))
            {
                ExecuteSearch();
            }

            ImGui.Separator();
            DrawResults();
        }

        private void DrawLanguageControls()
        {
            ImGui.Checkbox("de", ref de);
            ImGui.SameLine();
            ImGui.Checkbox("en", ref en);
            ImGui.SameLine();
            ImGui.Checkbox("fr", ref fr);
            ImGui.SameLine();
            ImGui.Checkbox("ja", ref ja);
        }

        private void DrawTypeControls()
        {
            ImGui.Checkbox("Items", ref searchItems);
            ImGui.SameLine();
            ImGui.Checkbox("Quests", ref searchQuests);
            ImGui.SameLine();
            ImGui.Checkbox("Gathering", ref searchGathering);
            ImGui.SameLine();
            ImGui.Checkbox("Fishing", ref searchFishing);

            ImGui.Checkbox("Spearfishing", ref searchSpearfishing);
            ImGui.SameLine();
            ImGui.Checkbox("Sightseeing", ref searchSightseeing);
            ImGui.SameLine();
            ImGui.Checkbox("Duties", ref searchDuties);
            ImGui.SameLine();
            ImGui.Checkbox("Map markers", ref searchMapMarkers);
        }

        private void ExecuteSearch()
        {
            var query = input.Trim();
            if (query.Length == 0)
            {
                results = [];
                return;
            }

            log.Debug($"Search {query}");
            var nextResults = new List<SearchResult>();
            if (searchItems)
            {
                AddItemResults(nextResults, query);
            }
            if (searchQuests)
            {
                AddQuestResults(nextResults, query);
            }
            if (searchGathering)
            {
                AddGatheringResults(nextResults, query);
            }
            if (searchFishing)
            {
                AddFishingResults(nextResults, query);
            }
            if (searchSpearfishing)
            {
                AddSpearfishingResults(nextResults, query);
            }
            if (searchSightseeing)
            {
                AddSightseeingResults(nextResults, query);
            }
            if (searchDuties)
            {
                AddDutyResults(nextResults, query);
            }
            if (searchMapMarkers)
            {
                AddMapMarkerResults(nextResults, query);
            }

            results = nextResults
                .GroupBy(result => $"{result.Category}:{result.RowId}:{result.MapId}:{result.TerritoryId}:{result.Position}")
                .Select(group => group.First())
                .OrderBy(result => result.Category)
                .ThenBy(result => result.Name)
                .Take(300)
                .ToList();
        }

        private void DrawResults()
        {
            if (results is null)
            {
                return;
            }

            ImGui.TextDisabled($"{results.Count} result{(results.Count == 1 ? string.Empty : "s")}");
            using var child = ImRaii.Child("search_results", new Vector2(0, 0), false);
            if (!child)
            {
                return;
            }

            foreach (var result in results)
            {
                using var id = ImRaii.PushId($"{result.Category}_{result.RowId}_{result.MapId}_{result.TerritoryId}_{result.Name}");
                DrawResult(result);
            }
        }

        private void DrawResult(SearchResult result)
        {
            if (result.IconId != 0)
            {
                var texture = textureProvider.GetFromGameIcon(new GameIconLookup(result.IconId)).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, texture.Size / 2.0f);
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            try
            {
                ImGui.TextUnformatted(result.Name);
                ImGui.TextDisabled($"{result.Category} #{result.RowId}{(string.IsNullOrWhiteSpace(result.Subtitle) ? string.Empty : $" | {result.Subtitle}")}");
                DrawResultAction(result);
            }
            finally
            {
                ImGui.EndGroup();
            }
        }

        private void DrawResultAction(SearchResult result)
        {
            switch (result.Action)
            {
                case SearchResultAction.ItemExtraData:
                    if (ImGui.SmallButton("Extra item data"))
                    {
                        OpenItemExtraDataWindow(result.RowId);
                    }
                    break;
                case SearchResultAction.OpenMap:
                    if (ImGui.SmallButton("Open map"))
                    {
                        OpenMap(result);
                    }
                    break;
                case SearchResultAction.OpenMapAndFlag:
                    if (ImGui.SmallButton("Reveal on map"))
                    {
                        RevealOnMap(result);
                    }
                    break;
                case SearchResultAction.FindDownloadedObjectAndFlag:
                    if (ImGui.SmallButton("Find on map"))
                    {
                        _ = FindDownloadedObjectAndRevealAsync(result);
                    }
                    break;
            }
        }

        private void AddItemResults(List<SearchResult> output, string query)
        {
            foreach (var language in GetSearchLanguages())
            {
                output.AddRange(dataManager.GetExcelSheet<Item>(language)
                    .Where(row => row.RowId != 0 && Matches(row.Name.ToString(), row.RowId.ToString(), query))
                    .Take(100)
                    .Select(row => new SearchResult("Item", row.Name.ToString(), $"iLvl {row.LevelItem.RowId}", row.RowId, row.Icon, 0, 0, null, SearchResultAction.ItemExtraData)));
            }
        }

        private void AddQuestResults(List<SearchResult> output, string query)
        {
            foreach (var quest in dataManager.GetExcelSheet<Quest>(GetPrimaryLanguage()))
            {
                if (quest.RowId == 0 || !Matches(quest.Name.ToString(), quest.RowId.ToString(), query) || !quest.IssuerLocation.IsValid)
                {
                    continue;
                }

                var level = quest.IssuerLocation.Value;
                if (level.Map.RowId == 0 || level.X == 0 && level.Z == 0)
                {
                    continue;
                }

                output.Add(new SearchResult("Quest", GetNameOrFallback(quest.Name.ToString(), "Quest", quest.RowId), GetMapName(level.Map.RowId), quest.RowId, GetQuestMapIconId(quest), level.Map.RowId, level.Territory.RowId, new Vector3(level.X, level.Y, level.Z), SearchResultAction.OpenMapAndFlag));
            }
        }

        private void AddGatheringResults(List<SearchResult> output, string query)
        {
            foreach (var point in dataManager.GetExcelSheet<GatheringPoint>(GetPrimaryLanguage()))
            {
                if (point.RowId == 0 || !point.TerritoryType.IsValid || !point.TerritoryType.Value.Map.IsValid)
                {
                    continue;
                }

                var place = point.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
                var gatheringType = point.GatheringPointBase.ValueNullable?.GatheringType.ValueNullable?.Name.ToString() ?? "Gathering";
                if (!Matches(place, gatheringType, point.RowId.ToString(), point.GatheringPointBase.RowId.ToString(), query))
                {
                    continue;
                }

                output.Add(new SearchResult("Gathering", string.IsNullOrWhiteSpace(place) ? $"Gathering point #{point.RowId}" : place, gatheringType, point.RowId, (uint)(point.GatheringPointBase.ValueNullable?.GatheringType.ValueNullable?.IconMain ?? 60434), point.TerritoryType.Value.Map.RowId, point.TerritoryType.RowId, null, SearchResultAction.FindDownloadedObjectAndFlag, point.GatheringPointBase.RowId, "GatheringPoint"));
            }
        }

        private void AddFishingResults(List<SearchResult> output, string query)
        {
            foreach (var spot in dataManager.GetExcelSheet<FishingSpot>(GetPrimaryLanguage()))
            {
                if (spot.RowId == 0 || !spot.TerritoryType.IsValid || !spot.TerritoryType.Value.Map.IsValid)
                {
                    continue;
                }

                var name = GetNameOrFallback(spot.PlaceName.ValueNullable?.Name.ToString(), "Fishing spot", spot.RowId);
                if (!Matches(name, spot.RowId.ToString(), query))
                {
                    continue;
                }

                output.Add(new SearchResult("Fishing", name, GetMapName(spot.TerritoryType.Value.Map.RowId), spot.RowId, spot.Rare ? 60466u : 60465u, spot.TerritoryType.Value.Map.RowId, spot.TerritoryType.RowId, null, SearchResultAction.OpenMapAndFlag, null, $"map:{spot.X}:{spot.Z}"));
            }
        }

        private void AddSpearfishingResults(List<SearchResult> output, string query)
        {
            foreach (var spot in dataManager.GetExcelSheet<SpearfishingNotebook>(GetPrimaryLanguage()))
            {
                if (spot.RowId == 0 || !spot.TerritoryType.IsValid || !spot.TerritoryType.Value.Map.IsValid)
                {
                    continue;
                }

                var name = GetNameOrFallback(spot.PlaceName.ValueNullable?.Name.ToString(), "Spearfishing spot", spot.RowId);
                if (!Matches(name, spot.RowId.ToString(), query))
                {
                    continue;
                }

                output.Add(new SearchResult("Spearfishing", name, GetMapName(spot.TerritoryType.Value.Map.RowId), spot.RowId, spot.IsShadowNode ? 60930u : 60929u, spot.TerritoryType.Value.Map.RowId, spot.TerritoryType.RowId, null, SearchResultAction.OpenMapAndFlag, null, $"map:{spot.X}:{spot.Y}"));
            }
        }

        private void AddSightseeingResults(List<SearchResult> output, string query)
        {
            foreach (var entry in dataManager.GetExcelSheet<Adventure>(GetPrimaryLanguage()))
            {
                if (entry.RowId == 0 || !entry.Level.IsValid)
                {
                    continue;
                }

                var name = GetNameOrFallback(entry.Name.ToString(), "Vista", entry.RowId);
                if (!Matches(name, entry.Description.ToString(), entry.RowId.ToString(), query))
                {
                    continue;
                }

                var level = entry.Level.Value;
                if (level.Map.RowId == 0 || level.X == 0 && level.Z == 0)
                {
                    continue;
                }

                output.Add(new SearchResult("Sightseeing", name, GetMapName(level.Map.RowId), entry.RowId, 60071, level.Map.RowId, level.Territory.RowId, new Vector3(level.X, level.Y, level.Z), SearchResultAction.OpenMapAndFlag));
            }
        }

        private void AddDutyResults(List<SearchResult> output, string query)
        {
            foreach (var content in dataManager.GetExcelSheet<ContentFinderCondition>(GetPrimaryLanguage()))
            {
                if (content.RowId == 0 || !content.TerritoryType.IsValid || !content.TerritoryType.Value.Map.IsValid)
                {
                    continue;
                }

                var name = GetNameOrFallback(content.Name.ToString(), "Duty", content.RowId);
                var contentType = content.ContentType.ValueNullable?.Name.ToString() ?? "Content";
                if (!Matches(name, contentType, content.RowId.ToString(), query))
                {
                    continue;
                }

                output.Add(new SearchResult("Duty", name, contentType, content.RowId, content.ContentType.ValueNullable?.Icon ?? 0, content.TerritoryType.Value.Map.RowId, content.TerritoryType.RowId, null, SearchResultAction.OpenMap));
            }
        }

        private void AddMapMarkerResults(List<SearchResult> output, string query)
        {
            foreach (var map in dataManager.GetExcelSheet<Map>(GetPrimaryLanguage()))
            {
                if (map.RowId == 0 || map.MapMarkerRange == 0)
                {
                    continue;
                }

                IReadOnlyList<MapMarker> markers;
                try
                {
                    markers = dataManager.GetSubrowExcelSheet<MapMarker>().GetRow(map.MapMarkerRange).ToList();
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                foreach (var marker in markers)
                {
                    if (marker.X == 0 && marker.Y == 0)
                    {
                        continue;
                    }

                    var name = marker.PlaceNameSubtext.ValueNullable?.Name.ToString() ?? string.Empty;
                    if (!Matches(name, map.PlaceName.ValueNullable?.Name.ToString(), marker.PlaceNameSubtext.RowId.ToString(), marker.Icon.ToString(), query))
                    {
                        continue;
                    }

                    output.Add(new SearchResult("Map marker", GetNameOrFallback(name, "Map marker", marker.RowId), GetMapName(map.RowId), marker.RowId, marker.Icon, map.RowId, map.TerritoryType.RowId, null, SearchResultAction.OpenMapAndFlag, null, $"map:{marker.X}:{marker.Y}"));
                }
            }
        }

        private IReadOnlyList<ClientLanguage> GetSearchLanguages()
        {
            var languages = new List<ClientLanguage>();
            if (de)
            {
                languages.Add(ClientLanguage.German);
            }
            if (en)
            {
                languages.Add(ClientLanguage.English);
            }
            if (fr)
            {
                languages.Add(ClientLanguage.French);
            }
            if (ja)
            {
                languages.Add(ClientLanguage.Japanese);
            }

            return languages.Count == 0 ? [GetPrimaryLanguage()] : languages;
        }

        private ClientLanguage GetPrimaryLanguage()
        {
            return de ? ClientLanguage.German
                : en ? ClientLanguage.English
                : fr ? ClientLanguage.French
                : ja ? ClientLanguage.Japanese
                : dataManager.Language;
        }

        private static bool Matches(string? value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool Matches(string? value1, string? value2, string query) => Matches(value1, query) || Matches(value2, query);
        private static bool Matches(string? value1, string? value2, string? value3, string query) => Matches(value1, query) || Matches(value2, query) || Matches(value3, query);
        private static bool Matches(string? value1, string? value2, string? value3, string? value4, string query) => Matches(value1, query) || Matches(value2, query) || Matches(value3, query) || Matches(value4, query);
        private static bool Matches(string? value1, string? value2, string? value3, string? value4, string? value5, string query) => Matches(value1, query) || Matches(value2, query) || Matches(value3, query) || Matches(value4, query) || Matches(value5, query);

        private string GetMapName(uint mapId)
        {
            return dataManager.GetExcelSheet<Map>(GetPrimaryLanguage()).TryGetRow(mapId, out var map)
                ? GetNameOrFallback(map.PlaceName.ValueNullable?.Name.ToString(), "Map", mapId)
                : $"Map #{mapId}";
        }

        private static string GetNameOrFallback(string? name, string fallback, uint rowId)
        {
            return string.IsNullOrWhiteSpace(name) ? $"{fallback} #{rowId}" : name;
        }

        private static uint GetQuestMapIconId(Quest quest)
        {
            if (quest.EventIconType.IsValid)
            {
                var eventIconType = quest.EventIconType.Value;
                var iconId = eventIconType.RowId switch
                {
                    1 => 71221u,
                    3 => 71201u,
                    4 => 71222u,
                    8 or 10 => 71341u,
                    33 => 62521u,
                    34 => 62523u,
                    _ => 0u,
                };
                if (iconId != 0)
                {
                    return iconId;
                }

                if (eventIconType.MapIconAvailable != 0)
                {
                    return eventIconType.MapIconAvailable;
                }
            }

            return quest.Icon != 0 ? quest.Icon : 71221;
        }

        private unsafe void OpenMap(SearchResult result)
        {
            var agentMap = AgentMap.Instance();
            if (agentMap == null || result.MapId == 0 || result.TerritoryId == 0)
            {
                return;
            }

            agentMap->OpenMap(result.MapId, result.TerritoryId, result.Name, FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.Centered);
        }

        private void RevealOnMap(SearchResult result)
        {
            OpenMap(result);
            if (result.Position is { } worldPosition)
            {
                _ = framework.RunOnTick(() => SetFlag(result, worldPosition));
                return;
            }

            if (TryGetMapCoordinate(result, out var mapCoordinate))
            {
                _ = framework.RunOnTick(() =>
                {
                    var worldPosition = MapWindow.GetWorldPositionForMapCoordinate(mapCoordinate);
                    SetFlag(result, worldPosition);
                });
            }
        }

        private async Task FindDownloadedObjectAndRevealAsync(SearchResult result)
        {
            OpenMap(result);
            if (result.MapId == 0 || result.MatchBaseId is null || string.IsNullOrWhiteSpace(result.MatchType))
            {
                return;
            }

            try
            {
                var mapObjects = await uploadManager.DownloadMapContentFromAPI(result.MapId);
                var positionedObject = mapObjects.FirstOrDefault(obj => obj.t == result.MatchType && (obj.bid == result.RowId || obj.bid == result.MatchBaseId));
                if (positionedObject is null)
                {
                    log.Warning("Could not find positioned search result {Type} {RowId} on map {MapId}.", result.MatchType, result.RowId, result.MapId);
                    return;
                }

                _ = framework.RunOnTick(() => SetFlag(result, positionedObject.pos));
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to reveal search result {Category} {RowId}.", result.Category, result.RowId);
            }
        }

        private static unsafe void SetFlag(SearchResult result, Vector3 position)
        {
            var agentMap = AgentMap.Instance();
            if (agentMap == null || result.MapId == 0 || result.TerritoryId == 0)
            {
                return;
            }

            agentMap->OpenMap(result.MapId, result.TerritoryId, result.Name, FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.Centered);
            agentMap->SetFlagMapMarker(result.TerritoryId, result.MapId, position);
        }

        private static bool TryGetMapCoordinate(SearchResult result, out Vector2 mapCoordinate)
        {
            mapCoordinate = Vector2.Zero;
            if (string.IsNullOrWhiteSpace(result.MatchType) || !result.MatchType.StartsWith("map:", StringComparison.Ordinal))
            {
                return false;
            }

            var parts = result.MatchType.Split(':');
            if (parts.Length != 3
                || !float.TryParse(parts[1], out var x)
                || !float.TryParse(parts[2], out var y))
            {
                return false;
            }

            mapCoordinate = new Vector2(x, y);
            return true;
        }

        private void OpenItemExtraDataWindow(uint itemId)
        {
            var newName = $"akutrack_item_extra_{itemId}";
            foreach (var window in windowSystem.Windows)
            {
                var splitName = window.WindowName.Split("##");
                if (splitName.Length == 2 && splitName[1] == newName)
                {
                    window.IsOpen = true;
                    return;
                }
            }

            var itemWindow = new ItemExtraDataWindow(windowSystem, log, framework, dataManager, textureProvider, uploadManager, allaganToolsIpc, itemId);
            windowSystem.AddWindow(itemWindow);
            itemWindow.IsOpen = true;
        }
    }
}
