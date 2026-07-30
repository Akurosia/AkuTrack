using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace AkuTrack;

public enum GameMapOpenModifier
{
    Ctrl,
    Shift,
    Alt,
}

public enum MapObjectSource
{
    Downloaded,
    SelfFound,
}

public enum MapContentScope
{
    World,
    ContentFinder,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DrawRemoteMarker { get; set; } = true;
    public bool DrawBNpc { get; set; } = true;
    public bool DrawENpc { get; set; } = true;
    public bool DrawEObj { get; set; } = true;
    public bool DrawGatheringPoint { get; set; } = true;
    public bool DrawTreasure { get; set; } = true;
    public bool DrawTreasureMaps { get; set; } = false;
    public bool DrawFishingSpots { get; set; } = false;
    public bool DrawSpearfishingSpots { get; set; } = false;
    public bool DrawQuestMarkers { get; set; } = true;
    public bool HideCompletedQuestMarkers { get; set; } = false;
    public bool DrawHousingMapMarkers { get; set; } = false;
    public bool DrawCriticalEngagements { get; set; } = true;
    public bool DrawFates { get; set; } = true;
    public bool DrawMapMarkersWithIcons { get; set; } = true;
    public bool DrawMapMarkerLabelsOnly { get; set; } = true;
    public bool OnlyDrawDownloadedNpcsWithUniqueIngameId { get; set; } = false;
    public bool DrawContentFinderConditionMarkers { get; set; } = true;
    public bool DrawContentFinderRemoteMarker { get; set; } = true;
    public bool DrawContentFinderBNpc { get; set; } = true;
    public bool DrawContentFinderENpc { get; set; } = true;
    public bool DrawContentFinderEObj { get; set; } = true;
    public bool DrawContentFinderGatheringPoint { get; set; } = true;
    public bool DrawContentFinderTreasure { get; set; } = true;
    public bool DrawContentFinderTreasureMaps { get; set; } = false;
    public bool DrawContentFinderFishingSpots { get; set; } = false;
    public bool DrawContentFinderSpearfishingSpots { get; set; } = false;
    public bool DrawContentFinderQuestMarkers { get; set; } = true;
    public bool DrawContentFinderHousingMapMarkers { get; set; } = false;
    public bool DrawContentFinderCriticalEngagements { get; set; } = true;
    public bool DrawContentFinderFates { get; set; } = true;
    public bool DrawContentFinderMapMarkersWithIcons { get; set; } = true;
    public bool DrawContentFinderMapMarkerLabelsOnly { get; set; } = true;
    public bool DrawContentFinderSightseeingLogEntries { get; set; } = false;
    public bool DrawSightseeingLogEntries { get; set; } = false;
    public bool DrawPartyMembers { get; set; } = true;
    public bool DrawOtherPlayers { get; set; } = false;
    public bool ColorPlayerMarkersByClass { get; set; } = false;
    public bool DrawCameraCone { get; set; } = true;
    public bool DrawDebugSquares { get; set; } = false;
    public bool ToggleMapWithGameMap { get; set; } = false;
    public bool ReplaceGameMap { get; set; } = false;
    public GameMapOpenModifier ReplaceGameMapModifier { get; set; } = GameMapOpenModifier.Ctrl;
    public bool CenterOnPlayerWhenOpening { get; set; } = false;
    public bool KeepPlayerCentered { get; set; } = false;
    public bool MapSearchFilterEnabled { get; set; } = false;
    public string MapSearchFilterText { get; set; } = string.Empty;
    public bool ConfigMapBehaviorOpen { get; set; } = false;
    public bool ConfigPlayersOpen { get; set; } = false;
    public bool ConfigWorldContentOpen { get; set; } = false;
    public bool ConfigMapMarkersOpen { get; set; } = false;
    public bool ConfigAppearanceDebugOpen { get; set; } = false;
    public Dictionary<uint, bool> TreasureMapRankToggles { get; set; } = new();
    public Dictionary<string, bool> IconCategoryToggles { get; set; } = new();
    public Dictionary<string, bool> ConfigIconCategoryOpen { get; set; } = new();
    public Dictionary<string, bool> ObjectSourceToggles { get; set; } = new();
    public Dictionary<uint, bool> ContentFinderConditionTypeToggles { get; set; } = new();

    public Vector4 TextColor { get; set; } = new Vector4(1.0f, 0.0f, 1.0f, 1.0f);

    public bool IsTreasureMapRankEnabled(uint rankId)
    {
        return !TreasureMapRankToggles.TryGetValue(rankId, out var enabled) || enabled;
    }

    public void SetTreasureMapRankEnabled(uint rankId, bool enabled)
    {
        TreasureMapRankToggles[rankId] = enabled;
    }

    public bool IsIconCategoryEntryEnabled(string category, uint iconId)
    {
        return IsIconCategoryEntryEnabled(MapContentScope.World, category, iconId);
    }

    public bool IsIconCategoryEntryEnabled(MapContentScope scope, string category, uint iconId)
    {
        if (IconCategoryToggles.TryGetValue(GetIconCategoryKey(scope, category, iconId), out var enabled))
        {
            return enabled;
        }

        return scope == MapContentScope.World && IconCategoryToggles.TryGetValue(GetLegacyIconCategoryKey(category, iconId), out enabled)
            ? enabled
            : GetDefaultIconCategoryEntryEnabled(category);
    }

    public void SetIconCategoryEntryEnabled(string category, uint iconId, bool enabled)
    {
        SetIconCategoryEntryEnabled(MapContentScope.World, category, iconId, enabled);
    }

    public void SetIconCategoryEntryEnabled(MapContentScope scope, string category, uint iconId, bool enabled)
    {
        IconCategoryToggles[GetIconCategoryKey(scope, category, iconId)] = enabled;
    }

    public bool IsIconCategoryOpen(string category)
    {
        return ConfigIconCategoryOpen.TryGetValue(category, out var open) && open;
    }

    public void SetIconCategoryOpen(string category, bool open)
    {
        ConfigIconCategoryOpen[category] = open;
    }

    public bool IsObjectSourceEnabled(string category, MapObjectSource source)
    {
        return IsObjectSourceEnabled(MapContentScope.World, category, source);
    }

    public bool IsObjectSourceEnabled(MapContentScope scope, string category, MapObjectSource source)
    {
        if (ObjectSourceToggles.TryGetValue(GetObjectSourceKey(scope, category, source), out var enabled))
        {
            return enabled;
        }

        return scope == MapContentScope.World && ObjectSourceToggles.TryGetValue(GetLegacyObjectSourceKey(category, source), out enabled)
            ? enabled
            : true;
    }

    public void SetObjectSourceEnabled(string category, MapObjectSource source, bool enabled)
    {
        SetObjectSourceEnabled(MapContentScope.World, category, source, enabled);
    }

    public void SetObjectSourceEnabled(MapContentScope scope, string category, MapObjectSource source, bool enabled)
    {
        ObjectSourceToggles[GetObjectSourceKey(scope, category, source)] = enabled;
    }

    public bool IsContentFinderConditionTypeEnabled(uint contentTypeId)
    {
        return ContentFinderConditionTypeToggles.TryGetValue(contentTypeId, out var enabled) ? enabled : true;
    }

    public void SetContentFinderConditionTypeEnabled(uint contentTypeId, bool enabled)
    {
        ContentFinderConditionTypeToggles[contentTypeId] = enabled;
    }

    private static string GetIconCategoryKey(string category, uint iconId) => GetIconCategoryKey(MapContentScope.World, category, iconId);
    private static string GetIconCategoryKey(MapContentScope scope, string category, uint iconId) => $"{scope}:{category}:{iconId}";
    private static string GetLegacyIconCategoryKey(string category, uint iconId) => $"{category}:{iconId}";
    private static string GetObjectSourceKey(MapContentScope scope, string category, MapObjectSource source) => $"{scope}:{category}:{source}";
    private static string GetLegacyObjectSourceKey(string category, MapObjectSource source) => $"{category}:{source}";

    private static bool GetDefaultIconCategoryEntryEnabled(string category)
    {
        return category is "GatheringPoint" or "EventObj" or "Quest";
    }

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
