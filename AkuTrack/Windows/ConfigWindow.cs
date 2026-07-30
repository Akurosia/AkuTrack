using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace AkuTrack.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Configuration configuration, IDataManager dataManager, IPluginLog log) : base("AkuTrack2 - Config###akutrack_config")
    {
        /*Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;
                */

        this.log = log;
        this.configuration = configuration;
        this.dataManager = dataManager;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

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
        if (!ImGui.BeginTabBar("akutrack_config_tabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Map"))
        {
            DrawMapBehaviorSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Players"))
        {
            DrawPlayerSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("World"))
        {
            DrawWorldContentSettings(MapContentScope.World);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Content Finder"))
        {
            DrawWorldContentSettings(MapContentScope.ContentFinder);
            ImGui.Separator();
            DrawContentFinderConditionSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Appearance"))
        {
            DrawAppearanceSettings();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawMapBehaviorSettings()
    {
        DrawCheckbox("Center on player when opening", configuration.CenterOnPlayerWhenOpening, value => configuration.CenterOnPlayerWhenOpening = value);
        DrawCheckbox("Fully replace game map", configuration.ReplaceGameMap, value =>
        {
            configuration.ReplaceGameMap = value;
            if (value)
            {
                configuration.ToggleMapWithGameMap = false;
            }
        });
        DrawGameMapModifierCombo();
        DrawCheckbox("Keep player centered until manual pan", configuration.KeepPlayerCentered, value => configuration.KeepPlayerCentered = value);
        ImGui.BeginDisabled(configuration.ReplaceGameMap);
        DrawCheckbox("Sync with game map (M)", configuration.ToggleMapWithGameMap, value => configuration.ToggleMapWithGameMap = value);
        ImGui.EndDisabled();
    }

    private void DrawPlayerSettings()
    {
        DrawCheckbox("Color player markers by class", configuration.ColorPlayerMarkersByClass, value => configuration.ColorPlayerMarkersByClass = value);
        DrawCheckbox("Show camera cone", configuration.DrawCameraCone, value => configuration.DrawCameraCone = value);
        DrawCheckbox("Show other players", configuration.DrawOtherPlayers, value => configuration.DrawOtherPlayers = value);
        DrawCheckbox("Show party members", configuration.DrawPartyMembers, value => configuration.DrawPartyMembers = value);
    }

    private void DrawWorldContentSettings(MapContentScope scope)
    {
        DrawSourceMasterToggles(scope);
        DrawCheckbox("Only show downloaded NPCs with unique ingame id", configuration.OnlyDrawDownloadedNpcsWithUniqueIngameId, value => configuration.OnlyDrawDownloadedNpcsWithUniqueIngameId = value);
        ImGui.Separator();
        if (scope == MapContentScope.ContentFinder)
        {
            DrawObjectCategorySettings(scope, "BattleNpc", "Show battle NPCs", GetContentToggle(scope, "BattleNpc"), value => SetContentToggle(scope, "BattleNpc", value));
            DrawCheckbox("Show critical engagements", GetContentToggle(scope, "CriticalEngagements"), value => SetContentToggle(scope, "CriticalEngagements", value));
            DrawObjectCategorySettings(scope, "EventNpc", "Show event NPCs", GetContentToggle(scope, "EventNpc"), value => SetContentToggle(scope, "EventNpc", value));
            DrawObjectCategoryWithSources(scope, "EventObj", "Show event objects", GetEventObjIconOptions());
            DrawCheckbox("Show FATEs", GetContentToggle(scope, "FATE"), value => SetContentToggle(scope, "FATE", value));
            DrawCheckbox("Show map markers with icons and labels", GetContentToggle(scope, "MapMarkersWithIcons"), value => SetContentToggle(scope, "MapMarkersWithIcons", value));
            DrawCheckbox("Show map markers with labels only", GetContentToggle(scope, "MapMarkerLabelsOnly"), value => SetContentToggle(scope, "MapMarkerLabelsOnly", value));
            DrawQuestMarkerSettings(scope);
            DrawObjectCategorySettings(scope, "Treasure", "Show treasure", GetContentToggle(scope, "Treasure"), value => SetContentToggle(scope, "Treasure", value));
        } else
        {
            DrawObjectCategorySettings(scope, "BattleNpc", "Show battle NPCs", GetContentToggle(scope, "BattleNpc"), value => SetContentToggle(scope, "BattleNpc", value));
            DrawCheckbox("Show critical engagements", GetContentToggle(scope, "CriticalEngagements"), value => SetContentToggle(scope, "CriticalEngagements", value));
            DrawObjectCategorySettings(scope, "EventNpc", "Show event NPCs", GetContentToggle(scope, "EventNpc"), value => SetContentToggle(scope, "EventNpc", value));
            DrawObjectCategoryWithSources(scope, "EventObj", "Show event objects", GetEventObjIconOptions());
            DrawCheckbox("Show FATEs", GetContentToggle(scope, "FATE"), value => SetContentToggle(scope, "FATE", value));
            DrawObjectCategoryWithSources(scope, "GatheringPoint", "Show gathering points", GetGatheringPointIconOptions());
            DrawCheckbox("Show housing map markers", GetContentToggle(scope, "HousingMapMarkerInfo"), value => SetContentToggle(scope, "HousingMapMarkerInfo", value));
            DrawCheckbox("Show map markers with icons and labels", GetContentToggle(scope, "MapMarkersWithIcons"), value => SetContentToggle(scope, "MapMarkersWithIcons", value));
            DrawCheckbox("Show map markers with labels only", GetContentToggle(scope, "MapMarkerLabelsOnly"), value => SetContentToggle(scope, "MapMarkerLabelsOnly", value));
            DrawQuestMarkerSettings(scope);
            DrawCheckbox("Show remote markers", GetContentToggle(scope, "RemoteMarker"), value => SetContentToggle(scope, "RemoteMarker", value));
            DrawCheckbox("Show sightseeing log entries", GetContentToggle(scope, "SightseeingLog"), value => SetContentToggle(scope, "SightseeingLog", value));
            DrawObjectCategorySettings(scope, "Treasure", "Show treasure", GetContentToggle(scope, "Treasure"), value => SetContentToggle(scope, "Treasure", value));
            DrawTreasureMapSettings(scope);
        }
    }

    private void DrawSourceMasterToggles(MapContentScope scope)
    {
        DrawSourceMasterToggle(scope, MapObjectSource.Downloaded, "Downloaded entries");
        ImGui.SameLine();
        DrawSourceMasterToggle(scope, MapObjectSource.SelfFound, "Self-found entries");
    }

    private void DrawQuestMarkerSettings(MapContentScope scope)
    {
        DrawCheckbox("Show quest markers", GetContentToggle(scope, "Quest"), value => SetContentToggle(scope, "Quest", value));
        if (GetContentToggle(scope, "Quest"))
        {
            ImGui.Indent();
            DrawIconCategorySettings(scope, "Quest", "Quest marker types", GetQuestIconOptions());
            ImGui.Unindent();
        }
    }

    private void DrawSourceMasterToggle(MapContentScope scope, MapObjectSource source, string label)
    {
        var enabled = SourceToggleCategories.All(category => configuration.IsObjectSourceEnabled(scope, category, source));
        if (ImGui.Checkbox($"{label}##{scope}_{source}_all", ref enabled))
        {
            foreach (var category in SourceToggleCategories)
            {
                configuration.SetObjectSourceEnabled(scope, category, source, enabled);
            }

            configuration.Save();
        }
    }

    private void DrawAppearanceSettings()
    {
        DrawCheckbox("Draw debug squares", configuration.DrawDebugSquares, value => configuration.DrawDebugSquares = value);

        ImGui.TextUnformatted("Map text color");
        ImGui.SameLine();
        var textColor = configuration.TextColor;
        if (ImGui.ColorEdit4("##map_text_color", ref textColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.DefaultOptions))
        {
            log.Debug($"Set TextColor to {textColor}");
            configuration.TextColor = textColor;
            configuration.Save();
        }
    }

    private void DrawSection(string label, bool currentOpen, Action<bool> setOpen, Action drawContents)
    {
        ImGui.SetNextItemOpen(currentOpen, ImGuiCond.Once);
        var open = ImGui.CollapsingHeader(label);
        if (open != currentOpen)
        {
            setOpen(open);
            configuration.Save();
        }

        if (open)
        {
            ImGui.Indent();
            drawContents();
            ImGui.Unindent();
        }
    }

    private bool GetContentToggle(MapContentScope scope, string category)
    {
        return scope switch
        {
            MapContentScope.World => category switch
            {
                "BattleNpc" => configuration.DrawBNpc,
                "CriticalEngagements" => configuration.DrawCriticalEngagements,
                "EventNpc" => configuration.DrawENpc,
                "FATE" => configuration.DrawFates,
                "HousingMapMarkerInfo" => configuration.DrawHousingMapMarkers,
                "MapMarkerLabelsOnly" => configuration.DrawMapMarkerLabelsOnly,
                "MapMarkersWithIcons" => configuration.DrawMapMarkersWithIcons,
                "RemoteMarker" => configuration.DrawRemoteMarker,
                "SightseeingLog" => configuration.DrawSightseeingLogEntries,
                "Treasure" => configuration.DrawTreasure,
                "TreasureMaps" => configuration.DrawTreasureMaps,
                _ => true,
            },
            MapContentScope.ContentFinder => category switch
            {
                "BattleNpc" => configuration.DrawContentFinderBNpc,
                "CriticalEngagements" => configuration.DrawContentFinderCriticalEngagements,
                "EventNpc" => configuration.DrawContentFinderENpc,
                "FATE" => configuration.DrawContentFinderFates,
                "HousingMapMarkerInfo" => configuration.DrawContentFinderHousingMapMarkers,
                "MapMarkerLabelsOnly" => configuration.DrawContentFinderMapMarkerLabelsOnly,
                "MapMarkersWithIcons" => configuration.DrawContentFinderMapMarkersWithIcons,
                "RemoteMarker" => configuration.DrawContentFinderRemoteMarker,
                "SightseeingLog" => configuration.DrawContentFinderSightseeingLogEntries,
                "Treasure" => configuration.DrawContentFinderTreasure,
                "TreasureMaps" => configuration.DrawContentFinderTreasureMaps,
                _ => true,
            },
            _ => true,
        };
    }

    private void SetContentToggle(MapContentScope scope, string category, bool value)
    {
        if (scope == MapContentScope.World)
        {
            switch (category)
            {
                case "BattleNpc": configuration.DrawBNpc = value; break;
                case "CriticalEngagements": configuration.DrawCriticalEngagements = value; break;
                case "EventNpc": configuration.DrawENpc = value; break;
                case "FATE": configuration.DrawFates = value; break;
                case "HousingMapMarkerInfo": configuration.DrawHousingMapMarkers = value; break;
                case "MapMarkerLabelsOnly": configuration.DrawMapMarkerLabelsOnly = value; break;
                case "MapMarkersWithIcons": configuration.DrawMapMarkersWithIcons = value; break;
                case "RemoteMarker": configuration.DrawRemoteMarker = value; break;
                case "SightseeingLog": configuration.DrawSightseeingLogEntries = value; break;
                case "Treasure": configuration.DrawTreasure = value; break;
                case "TreasureMaps": configuration.DrawTreasureMaps = value; break;
            }
            return;
        }

        switch (category)
        {
            case "BattleNpc": configuration.DrawContentFinderBNpc = value; break;
            case "CriticalEngagements": configuration.DrawContentFinderCriticalEngagements = value; break;
            case "EventNpc": configuration.DrawContentFinderENpc = value; break;
            case "FATE": configuration.DrawContentFinderFates = value; break;
            case "HousingMapMarkerInfo": configuration.DrawContentFinderHousingMapMarkers = value; break;
            case "MapMarkerLabelsOnly": configuration.DrawContentFinderMapMarkerLabelsOnly = value; break;
            case "MapMarkersWithIcons": configuration.DrawContentFinderMapMarkersWithIcons = value; break;
            case "RemoteMarker": configuration.DrawContentFinderRemoteMarker = value; break;
            case "SightseeingLog": configuration.DrawContentFinderSightseeingLogEntries = value; break;
            case "Treasure": configuration.DrawContentFinderTreasure = value; break;
            case "TreasureMaps": configuration.DrawContentFinderTreasureMaps = value; break;
        }
    }

    private void DrawTreasureMapSettings(MapContentScope scope)
    {
        var ranks = GetTreasureMapRanks().OrderBy(rank => rank.Name).ToList();
        if (ranks.Count <= 0)
        {
            ImGui.TextDisabled("Show treasure map spots");
            return;
        }

        DrawCheckbox("Show treasure map spots", GetContentToggle(scope, "TreasureMaps"), value => SetContentToggle(scope, "TreasureMaps", value));

        var open = configuration.IsIconCategoryOpen($"{scope}:TreasureMaps");
        ImGui.SetNextItemOpen(open, ImGuiCond.Once);
        var nextOpen = ImGui.TreeNode($"Treasure map types##{scope}_TreasureMaps_types");
        if (nextOpen != open)
        {
            configuration.SetIconCategoryOpen($"{scope}:TreasureMaps", nextOpen);
            configuration.Save();
        }

        if (!nextOpen)
        {
            return;
        }

        ImGui.Indent();
        var allChecked = ranks.All(rank => configuration.IsTreasureMapRankEnabled(rank.Id));
        if (ImGui.Checkbox("All##TreasureMaps_all", ref allChecked))
        {
            foreach (var rank in ranks)
            {
                configuration.SetTreasureMapRankEnabled(rank.Id, allChecked);
            }
            configuration.Save();
        }

        foreach (var rank in ranks)
        {
            DrawCheckbox($"{rank.Name}##TreasureMaps_{rank.Id}", configuration.IsTreasureMapRankEnabled(rank.Id), value => configuration.SetTreasureMapRankEnabled(rank.Id, value));
        }

        ImGui.Unindent();
        ImGui.TreePop();
    }

    private void DrawObjectCategorySettings(MapContentScope scope, string category, string label, bool currentValue, Action<bool> setValue)
    {
        DrawCheckbox(label, currentValue, setValue);
        DrawSourceToggles(scope, category);
    }

    private void DrawObjectCategoryWithSources(MapContentScope scope, string category, string label, IEnumerable<IconCategoryOption> optionsSource)
    {
        DrawIconCategorySettings(scope, category, label, optionsSource);
        DrawSourceToggles(scope, category);
    }

    private void DrawSourceToggles(MapContentScope scope, string category)
    {
        ImGui.Indent();
        DrawCheckbox($"Downloaded entries##{scope}_{category}_downloaded", configuration.IsObjectSourceEnabled(scope, category, MapObjectSource.Downloaded), value => configuration.SetObjectSourceEnabled(scope, category, MapObjectSource.Downloaded, value));
        ImGui.SameLine();
        DrawCheckbox($"Self-found entries##{scope}_{category}_selffound", configuration.IsObjectSourceEnabled(scope, category, MapObjectSource.SelfFound), value => configuration.SetObjectSourceEnabled(scope, category, MapObjectSource.SelfFound, value));
        ImGui.Unindent();
    }

    private void DrawContentFinderConditionSettings()
    {
        DrawCheckbox("Show content finder map markers", configuration.DrawContentFinderConditionMarkers, value => configuration.DrawContentFinderConditionMarkers = value);
        ImGui.Separator();

        var types = GetContentFinderConditionTypes().OrderBy(type => type.Name).ToList();
        if (types.Count <= 0)
        {
            ImGui.TextDisabled("No content finder condition types found.");
            return;
        }

        var allChecked = types.All(type => configuration.IsContentFinderConditionTypeEnabled(type.Id));
        if (ImGui.Checkbox("All##content_finder_all", ref allChecked))
        {
            foreach (var type in types)
            {
                configuration.SetContentFinderConditionTypeEnabled(type.Id, allChecked);
            }
            configuration.Save();
        }

        foreach (var type in types)
        {
            DrawCheckbox($"{type.Name}##content_finder_{type.Id}", configuration.IsContentFinderConditionTypeEnabled(type.Id), value => configuration.SetContentFinderConditionTypeEnabled(type.Id, value));
        }
    }

    private IEnumerable<ContentFinderConditionTypeConfigInfo> GetContentFinderConditionTypes()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>()
            .Where(row => row.RowId != 0 && row.ContentType.IsValid)
            .Select(row =>
            {
                var contentType = row.ContentType.Value;
                var label = contentType.Name.ToString();
                return new ContentFinderConditionTypeConfigInfo(contentType.RowId, string.IsNullOrWhiteSpace(label) ? $"Type {contentType.RowId}" : label);
            })
            .DistinctBy(type => type.Id);
    }

    private IEnumerable<TreasureMapRankConfigInfo> GetTreasureMapRanks()
    {
        var spots = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.TreasureSpot>();
        foreach (var rank in dataManager.GetExcelSheet<Lumina.Excel.Sheets.TreasureHuntRank>().OrderBy(rank => rank.RowId))
        {
            if (rank.RowId == 0 || rank.Icon == 0 || !rank.ItemName.IsValid || !spots.HasRow(rank.RowId))
            {
                continue;
            }

            var name = rank.ItemName.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new TreasureMapRankConfigInfo(rank.RowId, name);
        }
    }

    private void DrawIconCategorySettings(MapContentScope scope, string category, string label, IEnumerable<IconCategoryOption> optionsSource)
    {
        var options = optionsSource.OrderBy(option => option.IconId).ThenBy(option => option.Label).ToList();
        if (options.Count <= 1)
        {
            foreach (var option in options)
            {
                DrawCheckbox(label, configuration.IsIconCategoryEntryEnabled(scope, category, option.IconId), value => configuration.SetIconCategoryEntryEnabled(scope, category, option.IconId, value));
            }
            return;
        }

        var scopedCategory = $"{scope}:{category}";
        var open = configuration.IsIconCategoryOpen(scopedCategory);
        ImGui.SetNextItemOpen(open, ImGuiCond.Once);
        var nextOpen = ImGui.TreeNode($"{label}##{scope}_{category}_icon_types");
        if (nextOpen != open)
        {
            configuration.SetIconCategoryOpen(scopedCategory, nextOpen);
            configuration.Save();
        }

        if (!nextOpen)
        {
            return;
        }

        ImGui.Indent();
        var allChecked = options.All(option => configuration.IsIconCategoryEntryEnabled(scope, category, option.IconId));
        if (ImGui.Checkbox($"All##{scope}_{category}_all", ref allChecked))
        {
            foreach (var option in options)
            {
                configuration.SetIconCategoryEntryEnabled(scope, category, option.IconId, allChecked);
            }
            configuration.Save();
        }

        foreach (var option in options)
        {
            DrawCheckbox($"{option.Label} ({option.IconId:000000})##{scope}_{category}_{option.IconId}", configuration.IsIconCategoryEntryEnabled(scope, category, option.IconId), value => configuration.SetIconCategoryEntryEnabled(scope, category, option.IconId, value));
        }

        ImGui.Unindent();
        ImGui.TreePop();
    }

    private IEnumerable<IconCategoryOption> GetGatheringPointIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>()
            .Where(row => row.RowId != 0 && row.GatheringPointBase.IsValid && row.GatheringPointBase.Value.GatheringType.IsValid)
            .SelectMany(row =>
            {
                var gatheringType = row.GatheringPointBase.Value.GatheringType.Value;
                var label = gatheringType.Name.ToString();
                var iconId = (uint)gatheringType.IconMain;
                return new[] { new IconCategoryOption(iconId, string.IsNullOrWhiteSpace(label) ? "Gathering point" : label) };
            })
            .Where(option => option.IconId != 0)
            .DistinctBy(option => option.IconId);
    }

    private IEnumerable<IconCategoryOption> GetFishingSpotIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>()
            .Where(row => row.RowId != 0)
            .Select(row => new IconCategoryOption(row.Rare ? 60466u : 60465u, row.Rare ? "Rare fishing spot" : "Fishing spot"))
            .DistinctBy(option => option.IconId);
    }

    private IEnumerable<IconCategoryOption> GetEventObjIconOptions()
    {
        yield return new IconCategoryOption(60353, "Event object");
        yield return new IconCategoryOption(60033, "Windätherquelle");
        yield return new IconCategoryOption(60425, "Summoning bell");
        yield return new IconCategoryOption(60460, "Company chest");
        yield return new IconCategoryOption(60570, "Market board");
    }

    private IEnumerable<IconCategoryOption> GetSpearfishingSpotIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingNotebook>()
            .Where(row => row.RowId != 0)
            .Select(row => new IconCategoryOption(row.IsShadowNode ? 60930u : 60929u, row.IsShadowNode ? "Shadow spearfishing spot" : "Spearfishing spot"))
            .DistinctBy(option => option.IconId);
    }

    private IEnumerable<IconCategoryOption> GetQuestIconOptions()
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>()
            .Where(row => row.RowId != 0 && row.EventIconType.IsValid)
            .Select(row => new IconCategoryOption(GetQuestMapIconId(row), GetQuestIconLabel(row.EventIconType.RowId)))
            .Where(option => option.IconId != 0)
            .DistinctBy(option => option.IconId);
    }

    private static uint GetQuestMapIconId(Lumina.Excel.Sheets.Quest quest)
    {
        if (quest.EventIconType.IsValid)
        {
            var iconId = quest.EventIconType.RowId switch
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

            return quest.EventIconType.Value.MapIconAvailable;
        }

        return quest.Icon;
    }

    private static string GetQuestIconLabel(uint eventIconTypeId)
    {
        return eventIconTypeId switch
        {
            1 => "Side quest",
            3 => "Main scenario quest",
            4 => "Small side quest",
            8 or 10 => "Feature quest",
            33 => "Old Sharlayan quest",
            34 => "Tuliyollal quest",
            _ => $"Quest type {eventIconTypeId}",
        };
    }

    private static string GetIconCategoryLabel(string category)
    {
        return category switch
        {
            "GatheringPoint" => "Gathering point",
            "EventObj" => "Event object",
            "Fishingspot" => "Fishing spot",
            "SpearfishingNotebook" => "Spearfishing",
            "Quest" => "Quest marker",
            _ => category,
        };
    }

    private void DrawCheckbox(string label, bool currentValue, Action<bool> setValue)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
        {
            setValue(value);
            configuration.Save();
        }
    }

    private void DrawGameMapModifierCombo()
    {
        ImGui.BeginDisabled(!configuration.ReplaceGameMap);
        if (ImGui.BeginCombo("Open game map modifier", GetGameMapModifierLabel(configuration.ReplaceGameMapModifier)))
        {
            foreach (var modifier in GameMapModifiers)
            {
                var selected = configuration.ReplaceGameMapModifier == modifier;
                if (ImGui.Selectable(GetGameMapModifierLabel(modifier), selected))
                {
                    configuration.ReplaceGameMapModifier = modifier;
                    configuration.Save();
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

    private static string GetGameMapModifierLabel(GameMapOpenModifier modifier)
    {
        return modifier switch
        {
            GameMapOpenModifier.Ctrl => "Ctrl",
            GameMapOpenModifier.Shift => "Shift",
            GameMapOpenModifier.Alt => "Alt",
            _ => "Ctrl",
        };
    }

    private readonly record struct TreasureMapRankConfigInfo(uint Id, string Name);
    private readonly record struct IconCategoryOption(uint IconId, string Label);
    private readonly record struct ContentFinderConditionTypeConfigInfo(uint Id, string Name);

    private static readonly string[] SourceToggleCategories =
    [
        "BattleNpc",
        "EventNpc",
        "EventObj",
        "GatheringPoint",
        "Treasure",
    ];

    private static readonly GameMapOpenModifier[] GameMapModifiers =
    [
        GameMapOpenModifier.Ctrl,
        GameMapOpenModifier.Shift,
        GameMapOpenModifier.Alt,
    ];
}
