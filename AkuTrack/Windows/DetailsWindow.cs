using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class DetailsWindow : Window, IDisposable
{
    private static readonly Vector2 ItemIconSize = new(60, 60);
    private const float SortComboWidth = 110.0f;
    private const float ClassFilterComboWidth = 180.0f;

    private enum ChestRewardSortMode
    {
        Name,
        ItemLevel,
        RequiredLevel,
        Percentage,
    }

    private sealed class RewardDisplayInfo
    {
        public required string Name { get; init; }
        public required uint IconId { get; init; }
        public required string Details { get; init; }
        public required ChestDropReward Reward { get; init; }
        public int? ItemLevel { get; init; }
        public int? RequiredLevel { get; init; }
        public string ClassText { get; init; } = string.Empty;
        public HashSet<uint> CompatibleClassJobIds { get; init; } = [];
        public HashSet<uint> DirectClassJobIds { get; init; } = [];
        public HashSet<uint> GroupFilterIds { get; init; } = [];
    }

    private readonly WindowSystem windowSystem;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly UploadManager uploadManager;
    private readonly AllaganToolsIpc allaganToolsIpc;
    private readonly ChestRewardClassFilter classFilter;
    private readonly EnpcShopResolver enpcShopResolver;
    private readonly EnpcShopRenderer enpcShopRenderer;

    private readonly AkuGameObject obj;
    private ChestRewardSortMode chestRewardSortMode = ChestRewardSortMode.Name;
    private bool sortAscending = true;
    private uint selectedClassJobFilter;
    private bool includeClassGroups;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public DetailsWindow(WindowSystem windowSystem, IPluginLog log, IFramework framework, IClientState clienState, IDataManager dataManager, ITextureProvider textureProvider, UploadManager uploadManager, AllaganToolsIpc allaganToolsIpc, AkuGameObject obj) : base($"AkuTrack2 - Details for {obj.t} {obj.bid}##akutrack_details_{obj.t}_{obj.bid}")
    {
        this.windowSystem = windowSystem;
        this.log = log;
        this.framework = framework;
        this.clientState = clienState;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.uploadManager = uploadManager;
        this.allaganToolsIpc = allaganToolsIpc;
        this.classFilter = new ChestRewardClassFilter(dataManager, clienState.ClientLanguage);
        this.enpcShopResolver = new EnpcShopResolver(dataManager, clienState.ClientLanguage);
        this.enpcShopRenderer = new EnpcShopRenderer(textureProvider, ItemIconSize);
        this.log.Debug("Construct Window");
        this.obj = obj;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(250, 350),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() {   }

    public override void OnClose()
    {
        log.Debug("CLOSE");
        windowSystem.RemoveWindow(this);
    }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        /*
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
        */
    }

    public override void Draw()
    {
        if (obj.t == "EventNpc")
        {
            DrawENpcDetails();
        }
        else if (obj.t == "BattleNpc")
        {
            ImGui.LabelText("", "BattleNpc");
        }
        else if (obj.t == "EventObj" || obj.t == "Treasure") {
            DrawEventObjDetails();
        }
        else if (obj.t == "Aetheryte")
        {
            if(!dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().TryGetRow(obj.bid, out var aetheryte)) {
                return;
            }
            ImGui.LabelText("", "Aetheryte");

        }
        else if (obj.t == "GatheringPoint") {
            DrawGatheringPointDetails();
        }
        else if (obj.t == "Fishingspot")
        {
            DrawFishingSpotDetails();
        }
        else if (obj.t == "SpearfishingNotebook")
        {
            DrawSpearfishingSpotDetails();
        }
        else if (obj.t == "Quest")
        {
            DrawQuestDetails();
        }
        else if (obj.t == "HousingMapMarkerInfo")
        {
            DrawBasicMapElementDetails("Housing map marker");
        }
        else if (obj.t is "MapMarker" or "PlacedMapMarker" or "Flag" or "FieldMarker")
        {
            DrawBasicMapElementDetails(obj.t);
        }
        else if (obj.t == "TreasureMaps")
        {
            DrawTreasureMapDetails();
        }
        else if (obj.t == "FATE" || obj.t == "CEs")
        {
            DrawFateDetails();
        }
    }

    private void DrawBasicMapElementDetails(string title)
    {
        ImGui.LabelText("", title);
        ImGui.LabelText("", $"Name: {obj.name}");
        ImGui.LabelText("", $"BaseId: {obj.bid}");
        ImGui.LabelText("", $"Position: {obj.pos.X:F1}, {obj.pos.Z:F1}");
    }

    private void DrawQuestDetails()
    {
        ImGui.LabelText("", "Quest");
        if (!dataManager.GetExcelSheet<Quest>(clientState.ClientLanguage).TryGetRow(obj.bid, out var quest))
        {
            DrawBasicMapElementDetails("Quest");
            return;
        }

        ImGui.LabelText("", $"Name: {quest.Name}");
        if (quest.IssuerStart.TryGetValue<ENpcResident>(out var issuer))
        {
            ImGui.LabelText("", $"Issuer: {issuer.Singular}");
        }
        if (quest.JournalGenre.IsValid)
        {
            ImGui.LabelText("", $"Journal: {quest.JournalGenre.Value.Name}");
        }
        ImGui.LabelText("", $"Level: {quest.ClassJobLevel.FirstOrDefault()}");
    }

    private void DrawFishingSpotDetails()
    {
        ImGui.LabelText("", "Fishing spot");
        if (!dataManager.GetExcelSheet<FishingSpot>(clientState.ClientLanguage).TryGetRow(obj.bid, out var spot))
        {
            DrawBasicMapElementDetails("Fishing spot");
            return;
        }

        ImGui.LabelText("", $"Name: {spot.PlaceName.Value.Name}");
        ImGui.LabelText("", $"Level: {spot.GatheringLevel}");
        ImGui.LabelText("", $"Radius: {spot.Radius}");
        DrawItemRefs(spot.Item);
    }

    private void DrawSpearfishingSpotDetails()
    {
        ImGui.LabelText("", "Spearfishing spot");
        if (!dataManager.GetExcelSheet<SpearfishingNotebook>(clientState.ClientLanguage).TryGetRow(obj.bid, out var spot))
        {
            DrawBasicMapElementDetails("Spearfishing spot");
            return;
        }

        ImGui.LabelText("", $"Name: {spot.PlaceName.Value.Name}");
        ImGui.LabelText("", $"Level: {spot.GatheringLevel}");
        ImGui.LabelText("", $"Radius: {spot.Radius}");
        DrawGatheringPointBaseItems(spot.GatheringPointBase.Value);
    }

    private void DrawTreasureMapDetails()
    {
        ImGui.LabelText("", "Treasure map");
        if (!dataManager.GetExcelSheet<TreasureHuntRank>(clientState.ClientLanguage).TryGetRow(obj.bid, out var rank))
        {
            DrawBasicMapElementDetails("Treasure map");
            return;
        }

        ImGui.LabelText("", $"Name: {rank.ItemName.Value.Name}");
        ImGui.LabelText("", $"Max party size: {rank.MaxPartySize}");
        ImGui.LabelText("", $"Position: {obj.pos.X:F1}, {obj.pos.Z:F1}");
    }

    private void DrawFateDetails()
    {
        ImGui.LabelText("", obj.t == "CEs" ? "Critical engagement" : "FATE");
        if (dataManager.GetExcelSheet<Fate>(clientState.ClientLanguage).TryGetRow(obj.bid, out var fate))
        {
            ImGui.LabelText("", $"Name: {fate.Name}");
            ImGui.LabelText("", $"Level: {fate.ClassJobLevel}");
            if (!string.IsNullOrWhiteSpace(fate.Description.ToString()))
            {
                ImGui.TextWrapped(fate.Description.ToString());
            }
            return;
        }

        DrawBasicMapElementDetails(obj.t);
    }

    private void DrawENpcDetails() {
        ImGui.LabelText("", "EventNpc");
        if(!dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>().TryGetRow(obj.bid, out var eNpcResident)) {
            return;
        }
        ImGui.LabelText("", $"Name: {StringExtensions.ToUpper(eNpcResident.Singular.ToString(), true, true, false, clientState.ClientLanguage)}");

        var shops = enpcShopResolver.Resolve(obj.bid);
        if (shops.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        enpcShopRenderer.Draw(shops);
    }

    private void DrawGatheringPointDetails() {
        ImGui.LabelText("", "GatheringPoint");
        if(!dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().TryGetRow(obj.bid, out var gatheringPointRow)) {
            return;
        }
        ImGui.LabelText("", $"Type: {gatheringPointRow.GatheringPointBase.Value.GatheringType.Value.Name}");
        ImGui.LabelText("", $"Level: {gatheringPointRow.GatheringPointBase.Value.GatheringLevel}");
        ImGui.LabelText("", $"PlaceName: {gatheringPointRow.PlaceName.Value.Name}");
        DrawGatheringPointBaseItems(gatheringPointRow.GatheringPointBase.Value);
    }

    private void DrawGatheringPointBaseItems(GatheringPointBase gatheringPointBase)
    {
        var c = 0;
        foreach (var item in gatheringPointBase.Item)
        {
            c++;
            if (item.TryGetValue<GatheringItem>(out var gatheringItemRow))
            {
                if (gatheringItemRow.RowId == 0)
                    continue;
                if (gatheringItemRow.Item.TryGetValue<Item>(out var itemRow))
                {
                    var texture = textureProvider.GetFromGameIcon(new GameIconLookup(itemRow.Icon)).GetWrapOrEmpty();
                    ImGui.Image(texture.Handle, texture.Size / 2.0f);
                    ImGui.SameLine();
                    ImGui.LabelText("", $"Item {c}: {itemRow.Name} ({gatheringItemRow.GatheringItemLevel.Value.GatheringItemLevel})");
                }
                else if (gatheringItemRow.Item.TryGetValue<EventItem>(out var eventItemRow))
                {
                    var texture = textureProvider.GetFromGameIcon(new GameIconLookup(eventItemRow.Icon)).GetWrapOrEmpty();
                    ImGui.Image(texture.Handle, texture.Size / 2.0f);
                    ImGui.SameLine();
                    ImGui.LabelText("", $"Item {c}: {eventItemRow.Name} ({gatheringItemRow.GatheringItemLevel.Value.GatheringItemLevel}) | EventItem");
                }
            }
        }
    }

    private void DrawEventObjDetails()
    {
        ImGui.LabelText("", obj.t);
        ImGui.LabelText("", $"Name: {obj.name}");
        ImGui.LabelText("", $"BaseId: {obj.bid}");

        var chestEntries = FindMatchingChestEntries();
        if (chestEntries.Count == 0)
        {
            ImGui.Separator();
            ImGui.LabelText("", "Chest data: no matching entries found for this object.");
            return;
        }

        ImGui.Separator();
        DrawRewardSortControls();

        foreach (var chest in chestEntries)
        {

            if (!string.IsNullOrWhiteSpace(chest.DutyName))
            {
                ImGui.LabelText("", $"Duty: {chest.DutyName}");
            }

            if (!string.IsNullOrWhiteSpace(chest.PlaceNameSub))
            {
                ImGui.LabelText("", $"Area: {chest.PlaceNameSub}");
            }

            var rewardDisplayInfo = SortRewardDisplayInfos(FilterRewardDisplayInfos((chest.Rewards ?? []).Select(BuildRewardDisplayInfo))).ToList();
            foreach (var reward in rewardDisplayInfo)
            {
                var texture = textureProvider.GetFromGameIcon(new GameIconLookup(reward.IconId)).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, ItemIconSize);
                ImGui.SameLine();
                ImGui.BeginGroup();
                try
                {
                    ImGui.TextUnformatted(reward.Name);
                    if (!string.IsNullOrWhiteSpace(reward.Details))
                    {
                        ImGui.TextDisabled(reward.Details.TrimStart(' ', '|'));
                    }
                    DrawOpenItemExtraDataButton(reward.Reward.Id);
                }
                finally
                {
                    ImGui.EndGroup();
                }
            }
        }
    }

    private void DrawItemRefs(IEnumerable<Lumina.Excel.RowRef<Item>> items)
    {
        var c = 0;
        foreach (var itemRef in items)
        {
            c++;
            if (!itemRef.IsValid)
            {
                continue;
            }

            var item = itemRef.Value;
            if (item.RowId == 0)
            {
                continue;
            }

            var texture = textureProvider.GetFromGameIcon(new GameIconLookup(item.Icon)).GetWrapOrEmpty();
            ImGui.Image(texture.Handle, texture.Size / 2.0f);
            ImGui.SameLine();
            ImGui.LabelText("", $"Item {c}: {item.Name} ({item.LevelItem.RowId})");
            DrawOpenItemExtraDataButton(item.RowId);
        }
    }

    private List<ChestDropEntry> FindMatchingChestEntries()
    {
        var mapEntries = uploadManager.GetChestDropsForMap(obj.mid).ToList();
        if (mapEntries.Count == 0)
        {
            return [];
        }

        var entries = mapEntries
            .Where(x => x.TerritoryId == obj.zid)
            .ToList();

        if (entries.Count == 0)
        {
            entries = mapEntries;
        }

        var chestIdMatches = entries
            .Where(x => (uint)x.Id == obj.bid)
            .ToList();

        if (chestIdMatches.Count > 0)
        {
            return chestIdMatches;
        }

        return mapEntries
            .Where(x => (uint)x.Id == obj.bid)
            .ToList();
    }

    private void DrawRewardSortControls()
    {
        var sortLabels = new[] { "Name", "iLvl", "Level", "Percentage" };
        var selectedSort = (int)chestRewardSortMode;
        ImGui.SetNextItemWidth(SortComboWidth);
        if (ImGui.Combo("Sort", ref selectedSort, sortLabels, sortLabels.Length))
        {
            chestRewardSortMode = (ChestRewardSortMode)selectedSort;
        }

        ImGui.SameLine();
        ImGui.Checkbox("ASC", ref sortAscending);

        var classFilters = GetAvailableClassFilters().ToArray();
        var selectedClassIndex = Array.FindIndex(classFilters, option => option.RowId == selectedClassJobFilter);
        if (selectedClassIndex < 0)
        {
            selectedClassIndex = 0;
        }

        var classFilterLabels = classFilters.Select(option => option.Label).ToArray();
        ImGui.SetNextItemWidth(ClassFilterComboWidth);
        if (ImGui.Combo("Class Filter", ref selectedClassIndex, classFilterLabels, classFilterLabels.Length))
        {
            selectedClassJobFilter = classFilters[selectedClassIndex].RowId;
        }

        ImGui.SameLine();
        ImGui.Checkbox("Groups", ref includeClassGroups);

        ImGui.SameLine();
        if (ImGui.Button("Select current class"))
        {
            var currentClassJobId = Plugin.PlayerState.ClassJob.RowId;
            if (currentClassJobId != 0)
            {
                selectedClassJobFilter = currentClassJobId;
            }
        }
    }

    private IEnumerable<RewardDisplayInfo> SortRewardDisplayInfos(IEnumerable<RewardDisplayInfo> rewards)
    {
        Func<RewardDisplayInfo, object> keySelector = chestRewardSortMode switch
        {
            ChestRewardSortMode.ItemLevel => reward => reward.ItemLevel ?? int.MinValue,
            ChestRewardSortMode.RequiredLevel => reward => reward.RequiredLevel ?? int.MinValue,
            ChestRewardSortMode.Percentage => reward => reward.Reward.Pct,
            _ => reward => reward.Name,
        };

        var sorted = sortAscending
            ? rewards.OrderBy(keySelector).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            : rewards.OrderByDescending(keySelector).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

        return sorted;
    }

    private IEnumerable<RewardDisplayInfo> FilterRewardDisplayInfos(IEnumerable<RewardDisplayInfo> rewards)
    {
        if (selectedClassJobFilter == ChestRewardClassFilter.AllClassJobFilter)
        {
            return rewards;
        }

        if (ChestRewardClassFilter.IsGroupFilter(selectedClassJobFilter))
        {
            if (!includeClassGroups)
            {
                return [];
            }

            return rewards.Where(reward => reward.GroupFilterIds.Contains(selectedClassJobFilter));
        }

        return includeClassGroups
            ? rewards.Where(reward => reward.CompatibleClassJobIds.Contains(selectedClassJobFilter))
            : rewards.Where(reward => reward.DirectClassJobIds.Contains(selectedClassJobFilter));
    }

    private IEnumerable<ChestRewardClassFilter.ClassFilterOption> GetAvailableClassFilters()
    {
        var filters = new List<ChestRewardClassFilter.ClassFilterOption>
        {
            new() { RowId = ChestRewardClassFilter.AllClassJobFilter, Label = "All" },
        };

        filters.AddRange(
            classFilter.GetSelectableClassJobs()
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase));

        if (includeClassGroups)
        {
            filters.AddRange(classFilter.GetGroupFilterOptions());
        }

        return filters;
    }

    private RewardDisplayInfo BuildRewardDisplayInfo(ChestDropReward reward)
    {
        var details = $"Amount: {reward.Min}-{reward.Max} | Chance: {reward.Pct:P1}";

        if (dataManager.GetExcelSheet<Item>(clientState.ClientLanguage).TryGetRow(reward.Id, out var itemRow))
        {
            var classText = string.Empty;
            var compatibleClassJobIds = new HashSet<uint>();
            var directClassJobIds = new HashSet<uint>();
            var groupFilterIds = new HashSet<uint>();
            if (itemRow.EquipSlotCategory.RowId != 0)
            {
                classText = itemRow.ClassJobCategory.ValueNullable?.Name.ToString() ?? string.Empty;
                details += $"\nilvl {itemRow.LevelItem.RowId} | req lvl {itemRow.LevelEquip}";
                if (!string.IsNullOrWhiteSpace(classText))
                {
                    details += $"\n{classText}";
                }

                var filterMatch = classFilter.GetCompatibleFilterIds(itemRow.ClassJobCategory.Value);
                compatibleClassJobIds = filterMatch.CompatibleClassJobIds;
                directClassJobIds = filterMatch.DirectClassJobIds;
                groupFilterIds = filterMatch.GroupFilterIds;
            }

            return new RewardDisplayInfo
            {
                Name = itemRow.Name.ToString(),
                IconId = itemRow.Icon,
                Details = details,
                Reward = reward,
                ItemLevel = (int)itemRow.LevelItem.RowId,
                RequiredLevel = itemRow.LevelEquip,
                ClassText = classText ?? string.Empty,
                CompatibleClassJobIds = compatibleClassJobIds,
                DirectClassJobIds = directClassJobIds,
                GroupFilterIds = groupFilterIds,
            };
        }

        if (dataManager.GetExcelSheet<EventItem>(clientState.ClientLanguage).TryGetRow(reward.Id, out var eventItemRow))
        {
            return new RewardDisplayInfo
            {
                Name = eventItemRow.Name.ToString(),
                IconId = eventItemRow.Icon,
                Details = $"{details}\nEventItem",
                Reward = reward,
                ClassText = "EventItem",
                CompatibleClassJobIds = [],
                DirectClassJobIds = [],
                GroupFilterIds = [],
            };
        }

        return new RewardDisplayInfo
        {
            Name = $"Item #{reward.Id}",
            IconId = 0,
            Details = details,
            Reward = reward,
            ClassText = string.Empty,
            CompatibleClassJobIds = [],
            DirectClassJobIds = [],
            GroupFilterIds = [],
        };
    }

    private void DrawOpenItemExtraDataButton(uint itemId)
    {
        if (ImGui.SmallButton($"Extra item data##extra_item_data_{itemId}"))
        {
            OpenItemExtraDataWindow(itemId);
        }
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
