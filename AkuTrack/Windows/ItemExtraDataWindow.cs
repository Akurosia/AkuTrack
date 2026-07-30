using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
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

namespace AkuTrack.Windows;

public class ItemExtraDataWindow : Window, IDisposable
{
    private static readonly Vector2 ItemIconSize = new(48, 48);
    private sealed record GatheringNodeInfo(uint RowId, uint BaseId, uint MapId, uint TerritoryId, string PlaceName, string GatheringType, byte Level);

    private readonly WindowSystem windowSystem;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly UploadManager uploadManager;
    private readonly AllaganToolsIpc allaganToolsIpc;
    private readonly uint itemId;

    public ItemExtraDataWindow(
        WindowSystem windowSystem,
        IPluginLog log,
        IFramework framework,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        UploadManager uploadManager,
        AllaganToolsIpc allaganToolsIpc,
        uint itemId)
        : base($"AkuTrack2 - Item extra data {itemId}##akutrack_item_extra_{itemId}")
    {
        this.windowSystem = windowSystem;
        this.log = log;
        this.framework = framework;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.uploadManager = uploadManager;
        this.allaganToolsIpc = allaganToolsIpc;
        this.itemId = itemId;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public override void OnClose()
    {
        log.Debug("CLOSE");
        windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        DrawItemHeader();
        ImGui.Separator();
        DrawAllaganToolsData();
        ImGui.Separator();
        DrawDropSources();
        ImGui.Separator();
        DrawGatheringNodes();
        ImGui.Separator();
        DrawExternalLinks();
    }

    private void DrawItemHeader()
    {
        if (!dataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item))
        {
            ImGui.TextUnformatted($"Item #{itemId}");
            return;
        }

        var texture = textureProvider.GetFromGameIcon(new GameIconLookup(item.Icon)).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, ItemIconSize);
        ImGui.SameLine();
        ImGui.BeginGroup();
        try
        {
            ImGui.TextUnformatted(item.Name.ToString());
            ImGui.TextDisabled($"Item #{item.RowId} | iLvl {item.LevelItem.RowId} | req lvl {item.LevelEquip}");
        }
        finally
        {
            ImGui.EndGroup();
        }
    }

    private void DrawAllaganToolsData()
    {
        if (allaganToolsIpc.TryGetOwnedItemCount(itemId, out var ownedCount))
        {
            ImGui.LabelText("", $"Allagan Tools owned: {ownedCount}");
            return;
        }

        ImGui.TextDisabled("Allagan Tools owned: unavailable");
    }

    private void DrawDropSources()
    {
        var sources = uploadManager.GetChestDropsForItem(itemId)
            .Where(source => !string.IsNullOrWhiteSpace(source.DutyName))
            .GroupBy(source => new
            {
                Duty = source.DutyName ?? string.Empty,
                Area = source.PlaceNameSub ?? string.Empty,
            })
            .Select(group =>
            {
                var chance = group
                    .SelectMany(chest => chest.Rewards ?? [])
                    .Where(reward => reward.Id == itemId)
                    .Select(reward => reward.Pct)
                    .DefaultIfEmpty()
                    .Max();
                return (group.Key.Duty, group.Key.Area, Chance: chance);
            })
            .OrderBy(source => source.Duty)
            .ThenBy(source => source.Area)
            .ToList();

        if (sources.Count == 0)
        {
            ImGui.TextDisabled("Known drops: none in loaded chest data");
            return;
        }

        ImGui.TextUnformatted("Known drops");
        foreach (var source in sources)
        {
            var area = string.IsNullOrWhiteSpace(source.Area) ? string.Empty : $" - {source.Area}";
            var chance = source.Chance > 0 ? $" ({source.Chance:P1})" : string.Empty;
            ImGui.BulletText($"{source.Duty}{area}{chance}");
        }
    }

    private void DrawGatheringNodes()
    {
        var nodes = GetGatheringNodesForItem().ToList();
        var label = nodes.Count == 0 ? "Gathering nodes (0)" : $"Gathering nodes ({nodes.Count})";
        if (!ImGui.CollapsingHeader(label))
        {
            return;
        }

        if (nodes.Count == 0)
        {
            ImGui.TextDisabled("No gathering nodes found for this item.");
            return;
        }

        var childHeight = MathF.Min(180.0f, 28.0f * nodes.Count);
        using var child = ImRaii.Child("gathering_nodes_child", new Vector2(-1, childHeight), true);
        if (!child)
        {
            return;
        }

        foreach (var node in nodes)
        {
            var buttonLabel = $"{node.GatheringType} Lv. {node.Level} - {node.PlaceName}##gathering_node_{node.RowId}";
            if (ImGui.SmallButton(buttonLabel))
            {
                _ = OpenGatheringNodeOnMapAsync(node);
            }
        }
    }

    private IEnumerable<GatheringNodeInfo> GetGatheringNodesForItem()
    {
        foreach (var gatheringPoint in dataManager.GetExcelSheet<GatheringPoint>())
        {
            if (!gatheringPoint.TerritoryType.IsValid || !gatheringPoint.TerritoryType.Value.Map.IsValid || !GatheringPointContainsItem(gatheringPoint, itemId))
            {
                continue;
            }

            var type = gatheringPoint.GatheringPointBase.ValueNullable?.GatheringType.ValueNullable?.Name.ToString();
            var place = gatheringPoint.PlaceName.ValueNullable?.Name.ToString();
            yield return new GatheringNodeInfo(
                gatheringPoint.RowId,
                gatheringPoint.GatheringPointBase.RowId,
                gatheringPoint.TerritoryType.Value.Map.RowId,
                gatheringPoint.TerritoryType.RowId,
                string.IsNullOrWhiteSpace(place) ? $"Gathering point #{gatheringPoint.RowId}" : place,
                string.IsNullOrWhiteSpace(type) ? "Gathering" : type,
                gatheringPoint.GatheringPointBase.ValueNullable?.GatheringLevel ?? 0);
        }
    }

    private static bool GatheringPointContainsItem(GatheringPoint gatheringPoint, uint targetItemId)
    {
        foreach (var item in gatheringPoint.GatheringPointBase.Value.Item)
        {
            if (item.TryGetValue<GatheringItem>(out var gatheringItem)
                && gatheringItem.Item.TryGetValue<Item>(out var itemRow)
                && itemRow.RowId == targetItemId)
            {
                return true;
            }
        }

        return false;
    }

    private async Task OpenGatheringNodeOnMapAsync(GatheringNodeInfo node)
    {
        try
        {
            _ = framework.RunOnTick(() => OpenGatheringNodeMap(node));
            var mapObjects = await uploadManager.DownloadMapContentFromAPI(node.MapId);
            var positionedNode = mapObjects.FirstOrDefault(obj => obj.t == "GatheringPoint" && (obj.bid == node.RowId || obj.bid == node.BaseId));
            if (positionedNode is null || positionedNode.GetUniqueId() is null)
            {
                log.Warning("Could not find positioned gathering node {NodeId} on map {MapId}.", node.RowId, node.MapId);
                return;
            }

            _ = framework.RunOnTick(() => SetGatheringNodeFlag(node, positionedNode.pos));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to open gathering node {NodeId} on map {MapId}.", node.RowId, node.MapId);
        }
    }

    private static unsafe void OpenGatheringNodeMap(GatheringNodeInfo node)
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        agentMap->OpenMap(node.MapId, node.TerritoryId, node.PlaceName, FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.Centered);
    }

    private static unsafe void SetGatheringNodeFlag(GatheringNodeInfo node, Vector3 position)
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        agentMap->OpenMap(node.MapId, node.TerritoryId, node.PlaceName, FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.Centered);
        agentMap->SetFlagMapMarker(node.TerritoryId, node.MapId, position);
    }

    private void DrawExternalLinks()
    {
        if (ImGui.Button("Garland Tools"))
        {
            Util.OpenLink($"https://www.garlandtools.org/db/#item/{itemId}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Teamcraft"))
        {
            Util.OpenLink($"https://ffxivteamcraft.com/db/en/item/{itemId}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Universalis"))
        {
            Util.OpenLink($"https://universalis.app/market/{itemId}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Allagan"))
        {
            Util.OpenLink($"https://allagan.app/items/{itemId}");
        }
    }
}
