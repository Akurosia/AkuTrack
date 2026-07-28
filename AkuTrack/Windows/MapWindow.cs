using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Files;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace AkuTrack.Windows;

public class MapWindow : Window, IDisposable
{
    private readonly record struct ClickedAetheryte(string Name, uint AetheryteId, byte SubIndex, uint GilCost);
    private readonly record struct TreasureMapSpotInfo(uint RankId, ushort SpotIndex, string RankName, byte TextureId, Vector3 Position);

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly MapStateManager mapStateManager;
    private readonly ObjTrackManager objTrackManager;
    private readonly UploadManager uploadManager;
    private readonly WindowSystem windowSystem;
    private readonly IFramework framework;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IFateTable fateTable;
    private readonly IAetheryteList aetheryteList;
    private readonly IPluginLog log;
    private readonly ITextureProvider textureProvider;
    private readonly ITextureSubstitutionProvider textureSubstitutionProvider;
    private readonly EnpcShopResolver enpcShopResolver;

    private float Scale { get; set; } = 1;
    private const uint FlagTextCommandParamId = 1048;
    public Vector2 DrawOffset { get; set; }
    public HoverFlags HoveredFlags { get; private set; }
    public Vector2 DrawPosition { get; private set; }
    private Vector2 lastWindowSize;
    private bool isDragStarted = false;
    private bool keepPlayerCenteredPaused = false;

    private IDalamudTextureWrap? blendedTexture;
    private uint lastRenderedMapId;
    private bool isBlendedTexture;
    private string currentMapBgPath;
    private string currentMapFgPath;
    private uint capturedAgentMapId;
    private string capturedAgentMapBgPath = string.Empty;
    private string capturedAgentMapFgPath = string.Empty;
    private Vector2 capturedAgentRawMapOffset;
    private float capturedAgentMapScaleFactor;
    private uint pendingPlacedMarkerFocusMapId;
    private Vector2 pendingPlacedMarkerFocusPosition;
    private int pendingPlacedMarkerFocusFrames;
    private bool suppressFlagPlacement;
    private bool pendingFlagFocus;
    private (uint TerritoryId, uint MapId, float X, float Y)? lastFocusedFlag;

    private Vector2 currentMapPixelSize = new(0, 0);
    private Vector2 currentMapScreenPosition = new(0, 0);

    public float ZoomSpeed = 0.25f;

    private List<AkuGameObject> clickedObjects = new();
    private List<Lumina.Excel.Sheets.MapMarker> clickedMarkers = new();
    private HashSet<uint>? contentFinderTerritoryIds;
    private HashSet<uint>? questMapIconIds;
    private readonly HashSet<int> missingPlacedMapIconIds = new();
    private readonly HashSet<string> loggedCriticalEngagementMarkers = new();
    private readonly HashSet<uint> loggedFateIds = new();
    private readonly HashSet<string> loggedDynamicEventIds = new();
    private uint treasureMapSpotCacheMap;
    private List<TreasureMapSpotInfo> treasureMapSpotCache = new();
    private Vector2 contextMenuMapCoordinate;

    private readonly MapContextMenu mapContextMenu = new();
    private readonly TopBar topBar;
    private readonly BottomBar bottomBar;
    private string currentCursorPositionText = string.Empty;

    public enum IconIds : int {
        Aetheryte = 60453,
        AethernetShard = 60430,
        CompanyChest = 60460,
        MarketBoard = 60570,
        SummoningBell = 60425,
        Treasure = 60354,
        Unknown = 60515,
        EventNpc = 60424,
        EventObj = 60353,
        BattleNpc = 60422,
        Hover = 60429,
        MapChanger = 60441
    }

    public bool IsMapMarker(int iconid) {
        if (iconid == (int)IconIds.Aetheryte || iconid == (int)IconIds.AethernetShard || iconid == (int)IconIds.SummoningBell || iconid == (int)IconIds.MarketBoard || iconid == (int)IconIds.CompanyChest)
            return true;
        return false;
    }

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MapWindow(
        Plugin plugin,
        Configuration configuration,
        MapStateManager mapStateManager,
        ObjTrackManager objTrackManager,
        UploadManager uploadManager,
        TopBar topBar,
        BottomBar bottomBar,
        WindowSystem windowSystem,
        IFramework framework,
        IDataManager dataManager,
        IClientState clientState,
        IObjectTable objectTable,
        IPartyList partyList,
        IFateTable fateTable,
        IAetheryteList aetheryteList,
        ITextureProvider textureProvider,
        ITextureSubstitutionProvider textureSubstitutionProvider,
        IPluginLog log
        )
        : base("AkuTrack - Map##akutrack_map", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.mapStateManager = mapStateManager;
        this.configuration = configuration;
        this.log = log;
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.fateTable = fateTable;
        this.aetheryteList = aetheryteList;
        this.objTrackManager = objTrackManager;
        this.uploadManager = uploadManager;
        this.topBar = topBar;
        this.bottomBar = bottomBar;
        this.windowSystem = windowSystem;
        this.framework = framework;
        this.textureProvider = textureProvider;
        this.textureSubstitutionProvider = textureSubstitutionProvider;
        this.enpcShopResolver = new EnpcShopResolver(dataManager, clientState.ClientLanguage);
        this.currentMapBgPath = "";
        this.currentMapFgPath = "";
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public unsafe void FocusCurrentFlagMarkerIfNeeded()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->FlagMarkerCount == 0)
        {
            lastFocusedFlag = null;
            return;
        }

        var flag = agentMap->FlagMapMarkers[0];
        var focusedFlag = (flag.TerritoryId, flag.MapId, flag.XFloat, flag.YFloat);
        if (lastFocusedFlag == focusedFlag)
        {
            return;
        }

        lastFocusedFlag = focusedFlag;
        pendingFlagFocus = true;
        keepPlayerCenteredPaused = true;
    }

    public void FocusCurrentFlagMarkerOnNextDraw()
    {
        pendingFlagFocus = true;
        keepPlayerCenteredPaused = true;
    }

    public unsafe void CaptureSelectedMapFromAgent()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->SelectedMapId == 0)
        {
            return;
        }

        capturedAgentMapId = agentMap->SelectedMapId;
        capturedAgentMapFgPath = agentMap->SelectedMapPath.ToString();
        capturedAgentMapBgPath = agentMap->SelectedMapBgPath.ToString();
        capturedAgentRawMapOffset = new Vector2(agentMap->SelectedOffsetX * -1, agentMap->SelectedOffsetY * -1);
        capturedAgentMapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
        CapturePlacedMapMarkerFocus(agentMap);
        mapStateManager.SwitchMap(agentMap->SelectedMapId);
    }

    public override void OnOpen() {
        keepPlayerCenteredPaused = false;

        if (!configuration.CenterOnPlayerWhenOpening)
        {
            return;
        }

        CenterOnLocalPlayer();
    }

    public override void Draw()
    {
        if (configuration.KeepPlayerCentered && !keepPlayerCenteredPaused && mapStateManager.currentMap.RowId == clientState.MapId)
        {
            CenterOnLocalPlayer();
        }

        FocusPendingPlacedMapMarker();
        ProcessPendingFlagFocus();
        UpdateDrawOffset();

        HoveredFlags = HoverFlags.Nothing;

        if (IsBoundedBy(ImGui.GetMousePos(), ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionMax()))
        {
            HoveredFlags |= HoverFlags.Window;
        }

        topBar.Draw(GetCurrentMapDisplayPath(), GetTopBarCursorPositionText());

        using (var childStyle = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0.0f))
        using (var renderChild = ImRaii.Child("render_child", GetMapCanvasSize(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar))
        {
            currentMapScreenPosition = ImGui.GetWindowPos();
            DrawMapElements();
            currentMapPixelSize = ImGui.GetWindowSize();

            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                HoveredFlags |= HoverFlags.WindowInnerFrame;
            }
        }

        bottomBar.Draw(HoveredFlags.HasFlag(HoverFlags.MapTexture), currentMapPixelSize, DrawPosition, DrawOffset, Scale, GetBottomBarPlayerPositionText());
        ProcessInputs();
    }

    private string GetCurrentMapDisplayPath()
    {
        var placeName = mapStateManager.currentMap.PlaceName.ValueNullable?.Name.ToString();
        var territoryName = mapStateManager.currentMap.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString();

        if (!string.IsNullOrWhiteSpace(territoryName) && !string.Equals(territoryName, placeName, StringComparison.CurrentCultureIgnoreCase))
        {
            return $"{territoryName} / {placeName}";
        }

        return !string.IsNullOrWhiteSpace(placeName)
            ? placeName
            : $"Map {mapStateManager.currentMap.RowId}";
    }

    private string GetTopBarCursorPositionText()
    {
        if (IsMouseInsideMapCanvas())
        {
            var cursor = TexturePixelToIngameCoord(GetMouseMapCoordinate());
            currentCursorPositionText = $"X:{cursor.X:F1} Y:{cursor.Y:F1}";
        }

        return currentCursorPositionText;
    }

    private Vector2 GetMapCanvasSize()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var footerHeight = 30.0f * scale;
        var available = ImGui.GetContentRegionAvail();
        return new Vector2(available.X, MathF.Max(120.0f * scale, available.Y - footerHeight));
    }

    private string GetBottomBarPlayerPositionText()
    {
        return mapStateManager.currentMap.RowId == clientState.MapId && objectTable.LocalPlayer is { } player
            ? FormatPlayerMapPosition(player.Position)
            : string.Empty;
    }

    private void DrawMapElements() {
        DrawContextMenu();
        DrawMapBackground();
        if (ImGui.IsItemHovered())
        {
            HoveredFlags |= HoverFlags.MapTexture;
        }

        var drawPlayerMarkersInBackground = ImGui.GetIO().KeyCtrl;
        if (drawPlayerMarkersInBackground)
        {
            DrawPlayerAndCone();
        }

        DrawAkuObjects();
        DrawMapMarkers();
        DrawQuestMarkers();
        DrawTreasureMapSpots();
        DrawFateMarkers();
        DrawDynamicEventMarkers();
        DrawPlacedMapMarkers();
        DrawFlagMarker();

        if (!drawPlayerMarkersInBackground)
        {
            DrawPlayerAndCone();
        }
    }

    private unsafe void DrawMapBackground() {
        if (mapStateManager.currentMap.RowId != lastRenderedMapId)
        {
            var idSplits = mapStateManager.currentMap.Id.ToString().Split('/');
            currentMapBgPath = $"ui/map/{idSplits[0]}/{idSplits[1]}/{idSplits[0]}{idSplits[1]}m_m.tex";
            currentMapFgPath = $"ui/map/{idSplits[0]}/{idSplits[1]}/{idSplits[0]}{idSplits[1]}_m.tex";
            log.Debug($"Drawing map BG: {currentMapBgPath} || FG: {currentMapFgPath} RowId {mapStateManager.currentMap.RowId} ScaleFactor: {GetMapScaleFactor()} OffsetX: {GetRawMapOffsetVector().X} OffsetY: {GetRawMapOffsetVector().Y}");
            blendedTexture?.Dispose();
            var loadedTexture = LoadTexture(currentMapBgPath, currentMapFgPath);
            if (loadedTexture is not null)
            {
                isBlendedTexture = true;
                blendedTexture = loadedTexture;
            } else {
                isBlendedTexture = false;
            }
        }

        IDalamudTextureWrap? currentTexture = null;

        if(isBlendedTexture) {
            currentTexture = blendedTexture;
        } else {
            currentTexture = textureProvider.GetFromGame(currentMapFgPath).GetWrapOrEmpty();

        }
        if(currentTexture is null) {
            log.Debug("Trying to draw null texture... Skip!");
            return;
        }
        ImGui.SetCursorPos(DrawPosition);
        ImGui.Image(currentTexture.Handle, currentTexture.Size * Scale);
        lastRenderedMapId = mapStateManager.currentMap.RowId;
    }

    private unsafe void RefreshCapturedAgentMapTransform()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->SelectedMapId != mapStateManager.currentMap.RowId)
        {
            return;
        }

        var foregroundPath = agentMap->SelectedMapPath.ToString();
        var backgroundPath = agentMap->SelectedMapBgPath.ToString();
        if (string.IsNullOrWhiteSpace(foregroundPath) && string.IsNullOrWhiteSpace(backgroundPath))
        {
            return;
        }

        capturedAgentMapId = agentMap->SelectedMapId;
        capturedAgentMapFgPath = foregroundPath;
        capturedAgentMapBgPath = backgroundPath;
        capturedAgentRawMapOffset = new Vector2(agentMap->SelectedOffsetX * -1, agentMap->SelectedOffsetY * -1);
        capturedAgentMapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
    }



    private IDalamudTextureWrap? LoadTexture(string bgPath, string fgPath)
    {

        var bgFile = GetTexFile(bgPath);
        var fgFile = GetTexFile(fgPath);

        if (bgFile is null || fgFile is null)
        {
            return null;
        }

        var backgroundBytes = bgFile.GetRgbaImageData();
        var foregroundBytes = fgFile.GetRgbaImageData();

        if (IsEffectivelyEmptyMapLayer(backgroundBytes))
        {
            log.Debug($"Skipping empty map background layer: {bgPath}");
            return null;
        }

        // Blend textures together
        Parallel.For(0, 2048 * 2048, i =>
        {
            var index = i * 4;

            // Blend, R, G, B, skip A.
            backgroundBytes[index + 0] = (byte)(backgroundBytes[index + 0] * foregroundBytes[index + 0] / 255);
            backgroundBytes[index + 1] = (byte)(backgroundBytes[index + 1] * foregroundBytes[index + 1] / 255);
            backgroundBytes[index + 2] = (byte)(backgroundBytes[index + 2] * foregroundBytes[index + 2] / 255);
        });

        return textureProvider.CreateFromRaw(RawImageSpecification.Rgba32(2048, 2048), backgroundBytes);
    }

    private static bool IsEffectivelyEmptyMapLayer(byte[] rgbaBytes)
    {
        if (rgbaBytes.Length < 4)
        {
            return true;
        }

        var pixelCount = rgbaBytes.Length / 4;
        var nonEmptyPixelLimit = Math.Max(1, pixelCount / 100);
        var visiblePixels = 0;
        var nonBlackPixels = 0;

        for (var i = 0; i < rgbaBytes.Length; i += 4)
        {
            if (rgbaBytes[i + 3] > 8)
            {
                visiblePixels++;
            }

            if (rgbaBytes[i] > 4 || rgbaBytes[i + 1] > 4 || rgbaBytes[i + 2] > 4)
            {
                nonBlackPixels++;
            }

            if (visiblePixels > nonEmptyPixelLimit && nonBlackPixels > nonEmptyPixelLimit)
            {
                return false;
            }
        }

        return visiblePixels <= nonEmptyPixelLimit || nonBlackPixels <= nonEmptyPixelLimit;
    }

    private TexFile? GetTexFile(string rawPath)
    {
        var path = textureSubstitutionProvider.GetSubstitutedPath(rawPath);

        if (Path.IsPathRooted(path))
        {
            return dataManager.GameData.GetFileFromDisk<TexFile>(path);
        }

        return dataManager.GetFile<TexFile>(path);
    }

    private void DrawTooltip(AkuGameObject obj)
    {
        ImGui.SetTooltip($"Created: {obj.created_at}\nLastSeen: {obj.lastseen_at}\n\nName: {obj.name}\nType: {obj.t}\nBaseID: {obj.bid}");
    }

    private void DrawContextMenu()
    {
        mapContextMenu.Draw(() => PlaceFlagAtMapCoordinate(contextMenuMapCoordinate));

        if (clickedObjects.Count > 0 || clickedMarkers.Count > 0)
        {
            DrawAkuObjectContextMenu(clickedObjects, clickedMarkers);
            if (!ImGui.IsPopupOpen("AkuTrack_AkuObject_Context_Menu"))
            {
                if (clickedObjects.Count > 0)
                    clickedObjects.Clear();
                if (clickedMarkers.Count > 0)
                    clickedMarkers.Clear();
            }
        }
    }

    private void DrawAkuObjects()
    {
        var scope = GetCurrentContentScope();
        if (ShouldDrawContent("RemoteMarker", scope))
        {
            foreach (var o in objTrackManager.downloadHashList)
            {
                if (!objTrackManager.seenHashList.ContainsKey(o.Key))
                    DrawAkuGameObject(o.Value, MapObjectSource.Downloaded, scope);
            }
        }

        if (mapStateManager.currentMap.RowId == clientState.MapId)
        {
            foreach (var o in objTrackManager.liveAkuObjects)
            {
                DrawAkuGameObject(o, MapObjectSource.SelfFound, scope);
            }
        }
    }

    private void DrawPlayerAndCone()
    {
        // Only draw player and from ObjectTable if we are looking at the map we are currently in
        if (mapStateManager.currentMap.RowId == clientState.MapId)
        {
            if (objectTable.LocalPlayer is { } localPlayer)
            {
                if (configuration.DrawCameraCone)
                {
                    DrawCameraCone(localPlayer.Position);
                }

                DrawPlayerIcon(localPlayer.Position, localPlayer.Rotation);
            }

            DrawPartyMemberIcons();
        }
    }

    private void DrawPartyMemberIcons()
    {
        if (!configuration.DrawPartyMembers || partyList.Length == 0)
        {
            return;
        }

        foreach (var member in partyList)
        {
            if (objectTable.LocalPlayer is { } localPlayer && member.EntityId == localPlayer.GameObjectId)
            {
                continue;
            }

            var memberObject = objectTable.SearchById(member.EntityId);
            if (memberObject is null || memberObject.ObjectKind != ObjectKind.Pc)
            {
                continue;
            }

            if (!MatchesMapSearch("Party member", member.Name.ToString(), member.ClassJob.RowId.ToString()))
            {
                continue;
            }

            var tint = GetPlayerMarkerTint(member.ClassJob.RowId, new Vector4(0.3f, 0.85f, 1.0f, 1.0f));
            if (DrawPlayerIcon(memberObject.Position, memberObject.Rotation, tint, 0.75f))
            {
                ImGui.SetTooltip($"Party Member: {member.Name}");
            }
        }
    }

    private void DrawMapMarkers() {
        try
        {
            var scope = GetCurrentContentScope();
            var rows = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>().GetRow(mapStateManager.currentMap.MapMarkerRange);
            foreach (var row in rows)
            {
                if (row.X == 0 && row.Y == 0)
                {
                    continue;
                }
                if (!ShouldDrawMapMarker(row, scope))
                {
                    continue;
                }
                if (mapStateManager.filterEnabled && mapStateManager.filterExpression != string.Empty)
                {
                    bool doDraw = false;
                    /*
                    if (row.DataKey.TryGetValue<Lumina.Excel.Sheets.PlaceName>(out var rowPlaceName)) {
                        if (mapStateManager.filterExpression.Contains(rowPlaceName.Name.ToString()))
                            doDraw = true;
                    }
                    */
                    if (row.PlaceNameSubtext.Value.Name.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase))
                    {
                        doDraw = true;
                    }
                    if (!doDraw)
                        continue;
                }
                var pos = new Vector2(row.X, row.Y);
                //log.Debug($"Icon {row.Icon} to {pos} {row.RowOffset} |{row.PlaceNameSubtext.Value.Name}|");
                DrawMapIcon(row.Icon, pos, 3.14f, row.PlaceNameSubtext.Value.Name.ToString(), row.SubtextOrientation);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                    AddClickedMarker(row);
                    AddNearbyElementsToSelection(ImGui.GetMousePos());
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // FIXME: How to get markers from region maps?!?
            //log.Debug($"Could not find Markers for Territory {currentTerritory}");
        }
    }

    private unsafe void DrawPlacedMapMarkers()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        if (agentMap->SelectedMapId == mapStateManager.currentMap.RowId || agentMap->CurrentMapId == mapStateManager.currentMap.RowId)
        {
            for (var i = 0; i < agentMap->TempMapMarkerCount && i < agentMap->TempMapMarkers.Length; i++)
            {
                var marker = agentMap->TempMapMarkers[i];
                DrawPlacedMapMarker(marker);
            }
        }

        foreach (var marker in agentMap->EventMarkers)
        {
            DrawEventMapMarker(marker);
        }
    }

    private void DrawPlacedMapMarker(TempMapMarker tempMarker)
    {
        var marker = tempMarker.MapMarker;
        var position = GetMapPositionForMarker(marker);
        if (!IsBoundedBy(position, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return;
        }

        DrawPlacedMapMarkerRadius(position, marker.Scale);

        var iconId = marker.IconId != 0 ? marker.IconId : marker.SecondaryIconId;
        if (iconId != 0)
        {
            DrawPlacedMapMarkerIcon((int)iconId, position, tempMarker.TooltipText.ToString());
        }
    }

    private void DrawPlacedMapMarkerRadius(Vector2 mapPosition, float radius)
    {
        if (radius <= 0)
        {
            return;
        }

        var center = currentMapScreenPosition + DrawPosition + mapPosition * Scale;
        var scaledRadius = radius * Scale;
        if (scaledRadius <= 1.0f)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var fillColor = ImGui.GetColorU32(new Vector4(0.02f, 0.26f, 0.09f, 0.42f));
        var lineColor = ImGui.GetColorU32(new Vector4(0.08f, 0.46f, 0.16f, 0.9f));
        drawList.AddCircleFilled(center, scaledRadius, fillColor, 64);
        drawList.AddCircle(center, scaledRadius, lineColor, 64, MathF.Max(1.0f, ImGuiHelpers.GlobalScale));
    }

    private void DrawEventMapMarker(FFXIVClientStructs.FFXIV.Client.Game.UI.MapMarkerData marker)
    {
        if (marker.MapId != mapStateManager.currentMap.RowId ||
            marker.TerritoryTypeId != mapStateManager.currentMap.TerritoryType.RowId)
        {
            return;
        }

        var scope = GetCurrentContentScope();
        var mapPosition = GetMapPositionForEventMarker(marker);
        var hasIcon = marker.IconId != 0;
        var isQuestMarker = hasIcon && IsQuestIcon(marker.IconId);
        if ((hasIcon || marker.Radius > 0) && ShouldDrawQuestContent(scope))
        {
            if (marker.Radius > 0)
            {
                DrawPlacedMapMarkerRadius(mapPosition, marker.Radius * GetMapScaleFactor());
            }

            var iconId = hasIcon ? marker.IconId : 71221;
            DrawPlacedMapMarkerIcon((int)iconId, mapPosition, "Active quest target");
            return;
        }
    }

    private void DrawCriticalEngagementMarker(Vector2 mapPosition, float radius, int iconId)
    {
        if (!IsBoundedBy(mapPosition, Vector2.Zero, new Vector2(2048, 2048)))
        {
            var offscreenLogKey = $"{mapStateManager.currentMap.RowId}:{iconId}:offscreen:{MathF.Round(mapPosition.X)}:{MathF.Round(mapPosition.Y)}:{MathF.Round(radius)}";
            if (loggedCriticalEngagementMarkers.Add(offscreenLogKey))
            {
                log.Debug(
                    "Skipping offscreen critical engagement marker map={MapId} territory={TerritoryId} icon={IconId} mapPosition={MapPosition} radius={Radius}",
                    mapStateManager.currentMap.RowId,
                    mapStateManager.currentMap.TerritoryType.RowId,
                    iconId,
                    mapPosition,
                    radius);
            }

            return;
        }

        var logKey = $"{mapStateManager.currentMap.RowId}:{iconId}:{MathF.Round(mapPosition.X)}:{MathF.Round(mapPosition.Y)}:{MathF.Round(radius)}";
        if (loggedCriticalEngagementMarkers.Add(logKey))
        {
            log.Debug(
                "Drawing critical engagement marker map={MapId} territory={TerritoryId} icon={IconId} mapPosition={MapPosition} radius={Radius}",
                mapStateManager.currentMap.RowId,
                mapStateManager.currentMap.TerritoryType.RowId,
                iconId,
                mapPosition,
                radius);
        }

        var drawList = ImGui.GetWindowDrawList();
        var center = currentMapScreenPosition + DrawPosition + mapPosition * Scale;

        if (radius > 0)
        {
            var scaledRadius = radius * GetMapScaleFactor() * Scale;
            if (scaledRadius > 1.0f)
            {
                drawList.AddCircleFilled(center, scaledRadius, ImGui.GetColorU32(new Vector4(0.76f, 0.05f, 0.04f, 0.28f)), 72);
                drawList.AddCircle(center, scaledRadius, ImGui.GetColorU32(new Vector4(0.86f, 0.05f, 0.04f, 0.82f)), 72, MathF.Max(2.0f, 2.0f * ImGuiHelpers.GlobalScale));
            }
        }

        var iconDrawn = false;
        if (GetPlacedMapMarkerTexture(iconId) is { } texture)
        {
            var size = texture.Size / 2.0f;
            var min = center - size / 2.0f;
            drawList.AddImage(texture.Handle, min, min + size);
            iconDrawn = true;
        }

        if (!iconDrawn)
        {
            DrawCriticalEngagementFallbackGlyph(center, false);
        }

        var hitSize = 28.0f * ImGuiHelpers.GlobalScale;
        if (IsMouseInsideMapCanvas() && IsBoundedBy(ImGui.GetMousePos(), center - new Vector2(hitSize), center + new Vector2(hitSize)))
        {
            ImGui.SetTooltip("Critical engagement");
        }
    }

    private Vector2 GetMapPositionForEventMarker(FFXIVClientStructs.FFXIV.Client.Game.UI.MapMarkerData marker)
    {
        var candidates = new[]
        {
            GetMapCoordinateFor3D(marker.Position),
            new Vector2(marker.Position.X, marker.Position.Z),
            new Vector2(marker.Position.X, marker.Position.Y),
            new Vector2(marker.Position.X / 16.0f, marker.Position.Z / 16.0f),
            new Vector2(marker.Position.X / 16.0f, marker.Position.Y / 16.0f),
        };

        return candidates.FirstOrDefault(
            candidate => IsBoundedBy(candidate, Vector2.Zero, new Vector2(2048, 2048)),
            candidates[0]);
    }

    private static void DrawCriticalEngagementFallbackGlyph(Vector2 center, bool overlay)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var radius = (overlay ? 9.0f : 13.0f) * scale;
        var borderColor = ImGui.GetColorU32(new Vector4(0.02f, 0.17f, 0.42f, 0.95f));
        var fillColor = ImGui.GetColorU32(new Vector4(0.25f, 0.78f, 1.0f, overlay ? 0.55f : 0.95f));
        var highlightColor = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.88f));

        drawList.AddCircleFilled(center, radius, borderColor, 24);
        drawList.AddCircleFilled(center, radius * 0.72f, fillColor, 24);
        drawList.AddLine(center + new Vector2(-radius * 0.45f, 0), center + new Vector2(radius * 0.45f, 0), highlightColor, MathF.Max(1.5f, 1.5f * scale));
        drawList.AddLine(center + new Vector2(0, -radius * 0.45f), center + new Vector2(0, radius * 0.45f), highlightColor, MathF.Max(1.5f, 1.5f * scale));
    }

    private unsafe void DrawFlagMarker()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->FlagMarkerCount == 0)
        {
            return;
        }

        var flag = agentMap->FlagMapMarkers[0];
        if (flag.MapId != mapStateManager.currentMap.RowId || flag.TerritoryId != mapStateManager.currentMap.TerritoryType.RowId)
        {
            return;
        }

        if (!MatchesMapSearch("Flag", "Map flag", flag.MapId.ToString(), flag.TerritoryId.ToString()))
        {
            return;
        }

        var position = new Vector3(flag.XFloat, 0, flag.YFloat);
        var iconId = flag.MapMarker.IconId == 0 ? 60561 : flag.MapMarker.IconId;
        DrawFlagIcon((int)iconId, position);
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas())
        {
            ImGui.SetTooltip($"Flag: {flag.XFloat:F1}, {flag.YFloat:F1}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                AddClickedObject(CreateFlagObject(position));
                AddNearbyElementsToSelection(ImGui.GetMousePos());
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                agentMap->FlagMarkerCount = 0;
                lastFocusedFlag = null;
                suppressFlagPlacement = true;
            }
        }
    }

    private void DrawQuestMarkers()
    {
        var scope = GetCurrentContentScope();
        if (!ShouldDrawQuestContent(scope))
        {
            return;
        }

        foreach (var quest in dataManager.GetExcelSheet<Quest>(clientState.ClientLanguage))
        {
            if (quest.RowId == 0 || !quest.IssuerLocation.IsValid)
            {
                continue;
            }

            var level = quest.IssuerLocation.Value;
            if (level.Map.RowId != mapStateManager.currentMap.RowId || level.X == 0 && level.Z == 0)
            {
                continue;
            }

            var iconId = GetQuestMapIconId(quest);
            if (iconId == 0 || !configuration.IsIconCategoryEntryEnabled(scope, "Quest", iconId))
            {
                continue;
            }

            var position = new Vector3(level.X, level.Y, level.Z);
            var name = string.IsNullOrWhiteSpace(quest.Name.ToString()) ? $"Quest #{quest.RowId}" : quest.Name.ToString();
            if (!MatchesMapSearch("Quest", name, quest.RowId.ToString(), iconId.ToString()))
            {
                continue;
            }

            DrawQuestIcon(iconId, position, name);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                AddClickedObject(CreateSyntheticMapObject("Quest", quest.RowId, name, position));
                AddNearbyElementsToSelection(ImGui.GetMousePos());
            }
        }
    }

    private void DrawTreasureMapSpots()
    {
        var scope = GetCurrentContentScope();
        if (!ShouldDrawContent("TreasureMaps", scope))
        {
            return;
        }

        foreach (var spot in GetTreasureMapSpotsForCurrentMap())
        {
            if (!configuration.IsTreasureMapRankEnabled(spot.RankId) ||
                !MatchesMapSearch("TreasureMaps", "Treasure map", spot.RankName, spot.RankId.ToString()))
            {
                continue;
            }

            DrawTreasureMapSpot(spot);
        }
    }

    private void DrawFateMarkers()
    {
        var scope = GetCurrentContentScope();
        if (!ShouldDrawContent("FATE", scope) && !ShouldDrawContent("CriticalEngagements", scope))
        {
            return;
        }

        foreach (var fate in fateTable)
        {
            if (fate is null || !fateTable.IsValid(fate))
            {
                continue;
            }

            if (fate.TerritoryType.RowId != mapStateManager.currentMap.TerritoryType.RowId ||
                fate.State is not (FateState.Preparing or FateState.Running))
            {
                continue;
            }

            var iconId = GetFateIconId(fate);
            var isCriticalEngagement = IsCriticalEngagementFate(fate);
            var contentCategory = isCriticalEngagement ? "CriticalEngagements" : "FATE";
            if (loggedFateIds.Add(fate.FateId))
            {
                log.Debug(
                    "Live FATE marker fateId={FateId} name={Name} mapIcon={MapIconId} icon={IconId} selectedIcon={SelectedIconId} territory={TerritoryId} state={State}",
                    fate.FateId,
                    fate.Name.ToString(),
                    fate.MapIconId,
                    fate.IconId,
                    iconId,
                    fate.TerritoryType.RowId,
                    fate.State);
            }

            if (!ShouldDrawContent(contentCategory, scope))
            {
                continue;
            }

            if (!MatchesMapSearch(contentCategory, fate.Name.ToString(), fate.FateId.ToString(), fate.Level.ToString(), iconId.ToString()))
            {
                continue;
            }

            DrawFateMarker(fate, isCriticalEngagement);
        }
    }

    private void DrawFateMarker(IFate fate, bool isCriticalEngagement)
    {
        var center = GetMapScreenPosition(fate.Position);
        var radius = fate.Radius * GetMapScaleFactor() * Scale;
        var drawList = ImGui.GetWindowDrawList();
        var radiusFillColor = isCriticalEngagement
            ? ImGui.GetColorU32(new Vector4(0.76f, 0.05f, 0.04f, 0.28f))
            : ImGui.GetColorU32(new Vector4(0.35f, 0.2f, 0.75f, 0.12f));
        var radiusLineColor = isCriticalEngagement
            ? ImGui.GetColorU32(new Vector4(0.86f, 0.05f, 0.04f, 0.82f))
            : ImGui.GetColorU32(new Vector4(0.55f, 0.35f, 1.0f, 0.65f));

        if (radius > 1.0f)
        {
            drawList.AddCircleFilled(center, radius, radiusFillColor, 48);
            drawList.AddCircle(center, radius, radiusLineColor, 48, MathF.Max(1.0f, ImGuiHelpers.GlobalScale));
        }

        var iconId = GetFateIconId(fate);
        var iconDrawn = false;
        if (iconId != 0)
        {
            try
            {
                var texture = textureProvider.GetFromGameIcon(new GameIconLookup(iconId, false)).GetWrapOrEmpty();
                var size = texture.Size / 2.0f;
                drawList.AddImage(texture.Handle, center - size / 2.0f, center + size / 2.0f);
                iconDrawn = true;
            }
            catch (Exception ex)
            {
                if (missingPlacedMapIconIds.Add((int)iconId))
                {
                    log.Warning(ex, "Could not load FATE map icon {IconId}.", iconId);
                }
            }
        }

        if (isCriticalEngagement && !iconDrawn)
        {
            DrawCriticalEngagementFallbackGlyph(center, false);
        }

        var hoverRadius = MathF.Max(14.0f, MathF.Min(radius, 32.0f));
        if (!IsMouseInsideMapCanvas() || Vector2.Distance(ImGui.GetMousePos(), center) > hoverRadius)
        {
            return;
        }

        var label = isCriticalEngagement ? "Critical engagement" : "FATE";
        ImGui.SetTooltip($"{label}: {fate.Name}\nLevel: {fate.Level}\nProgress: {fate.Progress}%\nTime: {FormatTimeRemaining(fate.TimeRemaining)}");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            AddClickedObject(CreateSyntheticMapObject(isCriticalEngagement ? "CEs" : "FATE", fate.FateId, fate.Name.ToString(), fate.Position));
            AddNearbyElementsToSelection(ImGui.GetMousePos());
        }
    }

    private static uint GetFateIconId(IFate fate)
    {
        return fate.MapIconId != 0 ? fate.MapIconId : fate.IconId;
    }

    private static bool IsCriticalEngagementFate(IFate fate)
    {
        return IsCriticalEngagementMapIcon(fate.MapIconId) || IsCriticalEngagementMapIcon(fate.IconId);
    }

    private unsafe void DrawDynamicEventMarkers()
    {
        var scope = GetCurrentContentScope();
        if (!ShouldDrawContent("CriticalEngagements", scope))
        {
            return;
        }

        var container = DynamicEventContainer.GetInstance();
        if (container == null)
        {
            return;
        }

        var events = container->Events;
        for (var i = 0; i < events.Length; i++)
        {
            ref var dynamicEvent = ref events[i];
            var marker = dynamicEvent.MapMarker;
            var logKey = $"{dynamicEvent.DynamicEventId}:{dynamicEvent.State}:{marker.MapId}:{marker.TerritoryTypeId}:{marker.IconId}:{marker.Position}:{marker.Radius}";
            if (loggedDynamicEventIds.Add(logKey))
            {
                log.Debug(
                    "Dynamic event index={Index} id={DynamicEventId} name={Name} active={Active} state={State} type={Type} progress={Progress} participants={Participants}/{MaxParticipants} markerMap={MarkerMapId} markerTerritory={MarkerTerritoryId} markerIcon={MarkerIconId} markerPosition={MarkerPosition} markerRadius={MarkerRadius}",
                    i,
                    dynamicEvent.DynamicEventId,
                    dynamicEvent.Name.ToString(),
                    dynamicEvent.IsActive(),
                    dynamicEvent.State,
                    dynamicEvent.DynamicEventType,
                    dynamicEvent.Progress,
                    dynamicEvent.Participants,
                    dynamicEvent.MaxParticipants,
                    marker.MapId,
                    marker.TerritoryTypeId,
                    marker.IconId,
                    marker.Position,
                    marker.Radius);
            }

            if (!dynamicEvent.IsActive() ||
                dynamicEvent.State is DynamicEventState.Inactive ||
                !MatchesMapSearch("CriticalEngagements", dynamicEvent.Name.ToString(), dynamicEvent.DynamicEventId.ToString(), dynamicEvent.State.ToString()))
            {
                continue;
            }

            if (marker.MapId != 0 && marker.MapId != mapStateManager.currentMap.RowId ||
                marker.TerritoryTypeId != 0 && marker.TerritoryTypeId != mapStateManager.currentMap.TerritoryType.RowId)
            {
                continue;
            }

            var mapPosition = GetMapPositionForEventMarker(marker);
            var iconId = marker.IconId != 0 ? (int)marker.IconId : 60852;
            DrawCriticalEngagementMarker(mapPosition, marker.Radius, iconId);

            if (IsMouseInsideMapCanvas())
            {
                var center = currentMapScreenPosition + DrawPosition + mapPosition * Scale;
                var hoverRadius = MathF.Max(18.0f, MathF.Min(marker.Radius * GetMapScaleFactor() * Scale, 36.0f));
                if (Vector2.Distance(ImGui.GetMousePos(), center) <= hoverRadius)
                {
                    ImGui.SetTooltip(
                        $"Critical engagement: {dynamicEvent.Name}\n" +
                        $"State: {dynamicEvent.State}\n" +
                        $"Progress: {dynamicEvent.Progress}%\n" +
                        $"Participants: {dynamicEvent.Participants}/{dynamicEvent.MaxParticipants}\n" +
                        $"Time: {FormatTimeRemaining(dynamicEvent.SecondsLeft)}");
                }
            }
        }
    }

    private IReadOnlyList<TreasureMapSpotInfo> GetTreasureMapSpotsForCurrentMap()
    {
        if (treasureMapSpotCacheMap == mapStateManager.currentMap.RowId)
        {
            return treasureMapSpotCache;
        }

        treasureMapSpotCacheMap = mapStateManager.currentMap.RowId;
        treasureMapSpotCache = new List<TreasureMapSpotInfo>();

        var spots = dataManager.GetSubrowExcelSheet<TreasureSpot>();
        foreach (var rank in dataManager.GetExcelSheet<TreasureHuntRank>(clientState.ClientLanguage))
        {
            if (rank.RowId == 0 || rank.Icon == 0 || !rank.ItemName.IsValid || !spots.HasRow(rank.RowId))
            {
                continue;
            }

            var rankName = rank.ItemName.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(rankName))
            {
                continue;
            }

            foreach (var spot in spots.GetRow(rank.RowId))
            {
                if (!spot.Location.IsValid)
                {
                    continue;
                }

                var level = spot.Location.Value;
                if (level.Map.RowId != mapStateManager.currentMap.RowId || level.X == 0 && level.Z == 0)
                {
                    continue;
                }

                treasureMapSpotCache.Add(new TreasureMapSpotInfo(
                    rank.RowId,
                    spot.SubrowId,
                    rankName,
                    rank.TreasureHuntTexture,
                    new Vector3(level.X, level.Y, level.Z)));
            }
        }

        return treasureMapSpotCache;
    }

    private void DrawTreasureMapSpot(TreasureMapSpotInfo spot)
    {
        var center = GetMapScreenPosition(spot.Position);
        var drawList = ImGui.GetWindowDrawList();
        var texture = textureProvider.GetFromGame(GetTreasureMapTexturePath(spot.TextureId)).GetWrapOrEmpty();

        var size = new Vector2(220.0f, 200.0f) * Scale;
        var min = center - size / 2.0f;
        var max = center + size / 2.0f;

        var baseUv0 = Vector2.Zero;
        var baseUv1 = new Vector2(2.0f / 5.0f, 1.0f);
        drawList.AddImage(texture.Handle, min, max, baseUv0, baseUv1);

        var xSourceSize = texture.Size.Y / 6.0f;
        var xCenter = new Vector2(
            texture.Size.X * (14.0f / 15.0f) + xSourceSize * 0.12f,
            texture.Size.Y * (3.0f / 5.0f) - xSourceSize * 0.18f
        );
        var xSourceMin = xCenter - new Vector2(xSourceSize / 2.0f, xSourceSize / 2.0f);
        var xSourceMax = xCenter + new Vector2(xSourceSize / 2.0f, xSourceSize / 2.0f);

        var xRenderSize = new Vector2(size.X * 0.22f, size.X * 0.22f);
        var xMin = center - xRenderSize / 2.0f;
        var xMax = center + xRenderSize / 2.0f;
        drawList.AddImage(texture.Handle, xMin, xMax, xSourceMin / texture.Size, xSourceMax / texture.Size);

        if (IsMouseInsideMapCanvas() && IsBoundedBy(ImGui.GetMousePos(), min, max))
        {
            ImGui.SetTooltip($"{spot.RankName}\nTreasure map spot: {spot.Position.X:F1}, {spot.Position.Z:F1}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
                AddClickedObject(CreateTreasureMapSpotObject(spot));
                AddNearbyElementsToSelection(ImGui.GetMousePos());
            }
        }
    }

    private AkuGameObject CreateTreasureMapSpotObject(TreasureMapSpotInfo spot)
    {
        return CreateSyntheticMapObject("TreasureMaps", spot.RankId, spot.RankName, spot.Position);
    }

    private static string GetTreasureMapTexturePath(byte textureId)
    {
        return textureId switch
        {
            1 => "ui/uld/treasuremap_relic_hr1.tex",
            2 => "ui/uld/treasuremap_seasonal_hr1.tex",
            4 => "ui/uld/treasuremap_undersea_hr1.tex",
            _ => "ui/uld/treasuremap_hr1.tex",
        };
    }

    private void DrawPlacedMapMarkerIcon(int iconId, Vector2 mapPosition, string tooltip)
    {
        var texture = GetPlacedMapMarkerTexture(iconId);
        if (texture is null)
        {
            return;
        }

        var size = texture.Size / 2.0f;
        var p = mapPosition * Scale + DrawPosition - size / 2.0f;

        ImGui.SetCursorPos(p);
        ImGui.Image(texture.Handle, size);
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas() && !string.IsNullOrWhiteSpace(tooltip))
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private IDalamudTextureWrap? GetPlacedMapMarkerTexture(int iconId)
    {
        try
        {
            return textureProvider.GetFromGameIcon(new GameIconLookup((uint)iconId, false)).GetWrapOrEmpty();
        }
        catch (Exception ex)
        {
            if (missingPlacedMapIconIds.Add(iconId))
            {
                log.Warning(ex, "Could not load placed map marker icon {IconId}; falling back to generic critical engagement marker.", iconId);
            }
        }

        try
        {
            return textureProvider.GetFromGameIcon(new GameIconLookup(60852, false)).GetWrapOrEmpty();
        }
        catch (Exception ex)
        {
            if (missingPlacedMapIconIds.Add(60852))
            {
                log.Warning(ex, "Could not load fallback critical engagement map marker icon.");
            }

            return null;
        }
    }

    private void DrawFlagIcon(int iconId, Vector3 position)
    {
        var texture = textureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
        var size = texture.Size / 2.0f;
        var p = GetMapCoordinateFor3D(position) * Scale + DrawPosition - size / 2.0f;

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + size, ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }

        ImGui.SetCursorPos(p);
        ImGui.Image(texture.Handle, size);
    }

    private void DrawQuestIcon(uint iconId, Vector3 position, string tooltip)
    {
        var texture = textureProvider.GetFromGameIcon((int)iconId).GetWrapOrEmpty();
        var size = texture.Size / 2.0f;
        var p = GetMapCoordinateFor3D(position) * Scale + DrawPosition - size / 2.0f;

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + size, ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }

        ImGui.SetCursorPos(p);
        ImGui.Image(texture.Handle, size);
        if (ImGui.IsItemHovered() && IsMouseInsideMapCanvas())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private Vector2 GetMapPositionForMarker(MapMarkerBase marker)
    {
        var rawPosition = new Vector2(marker.X, marker.Y);
        if (IsBoundedBy(rawPosition, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return rawPosition;
        }

        var worldPosition = new Vector3(marker.X / 16.0f, 0, marker.Y / 16.0f);
        var mapPosition = GetMapCoordinateFor3D(worldPosition);
        if (IsBoundedBy(mapPosition, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return mapPosition;
        }

        return rawPosition / 16.0f;
    }

    private unsafe void CapturePlacedMapMarkerFocus(AgentMap* agentMap)
    {
        var mapId = agentMap->SelectedMapId != 0 ? agentMap->SelectedMapId : agentMap->CurrentMapId;
        if (mapId == 0 || !TryGetPlacedMapMarkerFocus(agentMap, out var focusPosition))
        {
            return;
        }

        pendingPlacedMarkerFocusMapId = mapId;
        pendingPlacedMarkerFocusPosition = focusPosition;
        pendingPlacedMarkerFocusFrames = 30;
    }

    private unsafe bool TryGetPlacedMapMarkerFocus(AgentMap* agentMap, out Vector2 focusPosition)
    {
        focusPosition = Vector2.Zero;
        var largestRadius = 0;

        for (var i = 0; i < agentMap->TempMapMarkerCount && i < agentMap->TempMapMarkers.Length; i++)
        {
            var marker = agentMap->TempMapMarkers[i].MapMarker;
            var position = GetMapPositionForMarker(marker);
            if (marker.Scale <= largestRadius || !IsBoundedBy(position, Vector2.Zero, new Vector2(2048, 2048)))
            {
                continue;
            }

            largestRadius = marker.Scale;
            focusPosition = position;
        }

        return largestRadius > 0;
    }

    private void FocusPendingPlacedMapMarker()
    {
        if (pendingPlacedMarkerFocusFrames <= 0)
        {
            return;
        }

        if (pendingPlacedMarkerFocusMapId != mapStateManager.currentMap.RowId)
        {
            pendingPlacedMarkerFocusFrames--;
            return;
        }

        DrawOffset = GetMapCenterOffsetVector() - pendingPlacedMarkerFocusPosition;
        keepPlayerCenteredPaused = true;
        pendingPlacedMarkerFocusFrames = 0;
    }

    private void DrawAkuGameObject(AkuGameObject obj, MapObjectSource source, MapContentScope scope) {
        if (obj.mid != mapStateManager.currentMap.RowId)
            return;
        if(!ShouldDrawObjectKind(obj.objectKind, source, scope)) {
            return;
        }
        if (IsLocalPlayerObject(obj))
        {
            return;
        }
        if(mapStateManager.filterEnabled && mapStateManager.filterExpression != string.Empty) {
            bool doDraw = false;
            if ((obj.name?.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            obj.t.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ||
            obj.bid.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase))
            {
                doDraw = true;
            }
            if(obj.nid is not null && obj.nid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase)) {
                doDraw = true;
            }
            if (obj.npiid is not null && obj.npiid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase))
            {
                doDraw = true;
            }
            if (!doDraw)
                return;
        }
        var handledClickAndHover = false;
        if (obj.t == "Quest")
        {
            if (!configuration.IsObjectSourceEnabled(scope, "Quest", source) || !ShouldDrawQuestContent(scope))
            {
                return;
            }

            var iconId = GetDownloadedQuestMapIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "Quest", iconId))
            {
                return;
            }

            DrawIcon((int)iconId, obj);
        }
        else if (IsCriticalEngagementObject(obj))
        {
            if (!ShouldDrawContent("CriticalEngagements", scope))
            {
                return;
            }

            DrawIcon(60852, obj);
        }
        else if (string.Equals(obj.t, "FATE", StringComparison.OrdinalIgnoreCase))
        {
            if (!ShouldDrawContent("FATE", scope))
            {
                return;
            }

            DrawIcon(60501, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)
        {
            if (ShouldHideDownloadedNpcWithoutUniqueIngameId(obj, source))
                return;
            DrawIcon(enpcShopResolver.GetPreferredMapIconId(obj.bid), obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
        {
            if (!configuration.IsObjectSourceEnabled(scope, "EventObj", source))
                return;
            var iconId = GetEventObjIconId(obj.bid);
            if (!configuration.IsIconCategoryEntryEnabled(scope, "EventObj", iconId))
                return;
            if (obj.bid == 2000401) // summoning bell
                DrawIcon((int)IconIds.SummoningBell, obj);
            else if (obj.bid == 2000402) // market board
                DrawIcon((int)IconIds.MarketBoard, obj);
            else if (obj.bid == 2000470) // company chest
                DrawIcon((int)IconIds.CompanyChest, obj);
            else
                DrawIcon((int)IconIds.EventObj, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
        {
            if (ShouldHideDownloadedNpcWithoutUniqueIngameId(obj, source))
                return;
            DrawIcon((int)IconIds.BattleNpc, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte)
        {
            if(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().TryGetRow(obj.bid, out var aetheryte)) {
                if (aetheryte.AethernetName.Value.Name.ToString() != string.Empty && aetheryte.PlaceName.Value.Name.ToString() == string.Empty)
                {
                    DrawIcon((int)IconIds.AethernetShard, obj);
                } else {
                    DrawIcon((int)IconIds.Aetheryte, obj);
                }
            }
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint)
        {
            if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.GatheringPoint>().TryGetRow(obj.bid, out var gatheringPointRow))
            {
                log.Debug($"GatheringPoint {obj.bid} did not have a row in GatheringPoint sheet.");
                return;
            }
            var iconId = (uint)gatheringPointRow.GatheringPointBase.Value.GatheringType.Value.IconMain;
            if (!configuration.IsIconCategoryEntryEnabled(scope, "GatheringPoint", iconId))
                return;
            DrawIcon((int)iconId, obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)
            DrawIcon((int)IconIds.Treasure, obj);
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
        {
            var isPartyMember = IsPartyMember(obj);
            if (isPartyMember || !configuration.DrawOtherPlayers)
            {
                return;
            }

            handledClickAndHover = DrawActorDot(obj);
        }
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
            handledClickAndHover = DrawActorDot(obj);
        else if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount)
            handledClickAndHover = DrawActorDot(obj);
        else
            DrawIcon((int)IconIds.Unknown, obj);
        if (handledClickAndHover)
        {
            return;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            clickedObjects.Add(obj);
        }
        if (ImGui.IsItemHovered())
        {
            DrawTooltip(obj);
            if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ||
                obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
            {
                DrawActorDot(obj, true, false);
            }
            else
            {
                DrawIcon((int)IconIds.Hover, obj);
            }
        }
    }

    private bool ShouldDrawObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind, MapObjectSource source, MapContentScope scope)
    {
        var category = GetObjectKindCategory(objectKind);
        if (category is null)
        {
            return objectKind switch
            {
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte => true,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc => configuration.DrawOtherPlayers || configuration.DrawPartyMembers,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion => configuration.DrawOtherPlayers,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount => configuration.DrawOtherPlayers,
                _ => true,
            };
        }

        return configuration.IsObjectSourceEnabled(scope, category, source) && ShouldDrawContent(category, scope);
    }

    private static string? GetObjectKindCategory(Dalamud.Game.ClientState.Objects.Enums.ObjectKind objectKind)
    {
        return objectKind switch
        {
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc => "EventNpc",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj => "EventObj",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc => "BattleNpc",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint => "GatheringPoint",
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure => "Treasure",
            _ => null,
        };
    }

    private bool ShouldHideDownloadedNpcWithoutUniqueIngameId(AkuGameObject obj, MapObjectSource source)
    {
        return source == MapObjectSource.Downloaded
            && configuration.OnlyDrawDownloadedNpcsWithUniqueIngameId
            && (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
            && obj.unique_ingame_id is null;
    }

    private static bool IsCriticalEngagementObject(AkuGameObject obj)
    {
        return obj.t.Equals("CEs", StringComparison.OrdinalIgnoreCase) ||
            obj.t.Equals("CE", StringComparison.OrdinalIgnoreCase) ||
            obj.t.Equals("CriticalEngagement", StringComparison.OrdinalIgnoreCase) ||
            obj.t.Equals("CriticalEngagements", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCriticalEngagementMapIcon(uint iconId)
    {
        return iconId is 60852 or 60958;
    }

    private MapContentScope GetCurrentContentScope()
    {
        var territoryId = mapStateManager.currentMap.TerritoryType.RowId;
        return IsContentFinderTerritory(territoryId) ? MapContentScope.ContentFinder : MapContentScope.World;
    }

    private bool IsContentFinderTerritory(uint territoryId)
    {
        if (contentFinderTerritoryIds is null)
        {
            contentFinderTerritoryIds = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>()
                .Where(row => row.RowId != 0 && row.TerritoryType.RowId != 0)
                .Select(row => row.TerritoryType.RowId)
                .ToHashSet();
        }

        return territoryId != 0 && contentFinderTerritoryIds.Contains(territoryId);
    }

    private bool ShouldDrawContent(string category, MapContentScope scope)
    {
        return scope switch
        {
            MapContentScope.World => category switch
            {
                "BattleNpc" => configuration.DrawBNpc,
                "CriticalEngagements" => configuration.DrawCriticalEngagements,
                "EventNpc" => configuration.DrawENpc,
                "EventObj" => configuration.DrawEObj,
                "FATE" => configuration.DrawFates,
                "GatheringPoint" => configuration.DrawGatheringPoint,
                "HousingMapMarkerInfo" => configuration.DrawHousingMapMarkers,
                "MapMarkerLabelsOnly" => configuration.DrawMapMarkerLabelsOnly,
                "MapMarkersWithIcons" => configuration.DrawMapMarkersWithIcons,
                "RemoteMarker" => configuration.DrawRemoteMarker,
                "Quest" => configuration.DrawQuestMarkers,
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
                "EventObj" => configuration.DrawContentFinderEObj,
                "FATE" => configuration.DrawContentFinderFates,
                "GatheringPoint" => configuration.DrawContentFinderGatheringPoint,
                "HousingMapMarkerInfo" => configuration.DrawContentFinderHousingMapMarkers,
                "MapMarkerLabelsOnly" => configuration.DrawContentFinderMapMarkerLabelsOnly,
                "MapMarkersWithIcons" => configuration.DrawContentFinderMapMarkersWithIcons,
                "RemoteMarker" => configuration.DrawContentFinderRemoteMarker,
                "Quest" => configuration.DrawContentFinderQuestMarkers,
                "SightseeingLog" => configuration.DrawContentFinderSightseeingLogEntries,
                "Treasure" => configuration.DrawContentFinderTreasure,
                "TreasureMaps" => configuration.DrawContentFinderTreasureMaps,
                _ => true,
            },
            _ => true,
        };
    }

    private bool ShouldDrawMapMarker(Lumina.Excel.Sheets.MapMarker marker, MapContentScope scope)
    {
        if (IsQuestMapMarker(marker))
        {
            return ShouldDrawQuestContent(scope);
        }

        var category = IsRegionIcon((int)marker.Icon) ? "MapMarkerLabelsOnly" : "MapMarkersWithIcons";
        return ShouldDrawContent(category, scope);
    }

    private bool IsQuestMapMarker(Lumina.Excel.Sheets.MapMarker marker)
    {
        return marker.DataKey.TryGetValue<Quest>(out _) || IsQuestIcon(marker.Icon);
    }

    private bool ShouldDrawQuestContent(MapContentScope scope)
    {
        return ShouldDrawContent("Quest", scope) ||
            configuration.DrawQuestMarkers ||
            configuration.DrawContentFinderQuestMarkers;
    }

    private bool IsQuestIcon(uint iconId)
    {
        if (questMapIconIds is null)
        {
            questMapIconIds = dataManager.GetExcelSheet<Quest>()
                .Where(row => row.RowId != 0)
                .Select(GetQuestMapIconId)
                .Where(id => id != 0)
                .ToHashSet();
        }

        return questMapIconIds.Contains(iconId) || IsKnownQuestIconRange(iconId);
    }

    private static bool IsKnownQuestIconRange(uint iconId)
    {
        return iconId is >= 71200 and <= 71399 or >= 62500 and <= 62599;
    }

    private static uint GetEventObjIconId(uint baseId)
    {
        return baseId switch
        {
            2000401 => (uint)IconIds.SummoningBell,
            2000402 => (uint)IconIds.MarketBoard,
            2000470 => (uint)IconIds.CompanyChest,
            2007457 => 60033,
            _ => (uint)IconIds.EventObj,
        };
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

    private uint GetDownloadedQuestMapIconId(uint questId)
    {
        return dataManager.GetExcelSheet<Quest>(clientState.ClientLanguage).TryGetRow(questId, out var quest)
            ? GetQuestMapIconId(quest)
            : 71221;
    }

    private bool IsLocalPlayerObject(AkuGameObject obj)
    {
        return obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc &&
            objectTable.LocalPlayer is { } localPlayer &&
            obj.unique_ingame_id == localPlayer.GameObjectId;
    }

    public void DrawAkuObjectContextMenu(List<AkuGameObject> objs, List<Lumina.Excel.Sheets.MapMarker> markers)
    {
        using var contextMenu = ImRaii.ContextPopup("AkuTrack_AkuObject_Context_Menu");
        if (!contextMenu) return;

        foreach (var obj in objs)
        {
            foreach (var aetheryte in GetTeleportOptionsForObject(obj))
            {
                if (ImGui.MenuItem($"Teleport {aetheryte.Name} ({aetheryte.GilCost} gil)##teleport_obj_{aetheryte.AetheryteId}_{aetheryte.SubIndex}"))
                {
                    TeleportToAetheryte(aetheryte);
                }
            }

            if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount)
            {
                var name = dataManager.GetExcelSheet<Mount>().First(x => x.ModelChara.RowId == obj.moid).Singular.ToString();
                if (ImGui.MenuItem($"{obj.t} {name}({obj.moid})"))
                {
                    string newName = $"akutrack_details_{obj.bid}";
                    foreach (var w in windowSystem.Windows)
                    {
                        var wName = w.WindowName.Split("##")[1];
                        if (wName == newName)
                            return;
                    }
                    var dw = ActivatorUtilities.CreateInstance<DetailsWindow>(plugin.serviceProvider, new object[] { obj });
                    windowSystem.AddWindow(dw);
                    dw.Toggle();
                }                
            }
            else
            {
                if (ImGui.MenuItem($"{obj.t} {obj.name}({obj.bid})"))
                {
                    string newName = $"akutrack_details_{obj.bid}";
                    foreach (var w in windowSystem.Windows)
                    {
                        var wName = w.WindowName.Split("##")[1];
                        if (wName == newName)
                            return;
                    }
                    var dw = ActivatorUtilities.CreateInstance<DetailsWindow>(plugin.serviceProvider, new object[] { obj });
                    windowSystem.AddWindow(dw);
                    dw.Toggle();
                }
            }
            
        }
        foreach(var mark in markers) {
            foreach (var aetheryte in GetTeleportOptionsForMapMarker(mark))
            {
                if (ImGui.MenuItem($"Teleport {aetheryte.Name} ({aetheryte.GilCost} gil)##teleport_{aetheryte.AetheryteId}_{aetheryte.SubIndex}"))
                {
                    TeleportToAetheryte(aetheryte);
                }
            }

            if(ImGui.MenuItem($"MapMarker ({mark.RowId}.{mark.SubrowId}) {mark.PlaceNameSubtext.Value.Name.ToString()}")) {
                if (mark.DataKey.TryGetValue<Lumina.Excel.Sheets.Map>(out var dataKeyMap))
                {
                    log.Debug($"Found map {dataKeyMap.PlaceName.Value.Name.ToString()}");
                    mapStateManager.SwitchMap(dataKeyMap.RowId);
                } else {
                    log.Debug("Tut nix beim klicken, sorry.");
                }
            }
        }
    }

    private IEnumerable<ClickedAetheryte> GetTeleportOptionsForObject(AkuGameObject obj)
    {
        if (obj.objectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte ||
            !dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().TryGetRow(obj.bid, out var objectAetheryte))
        {
            yield break;
        }

        foreach (var aetheryte in aetheryteList)
        {
            if (aetheryte.TerritoryId != mapStateManager.currentMap.TerritoryType.RowId)
            {
                continue;
            }

            var data = aetheryte.AetheryteData.Value;
            if (!data.IsAetheryte || data.Invisible)
            {
                continue;
            }

            if (data.RowId != objectAetheryte.RowId && data.PlaceName.RowId != objectAetheryte.PlaceName.RowId)
            {
                continue;
            }

            yield return new ClickedAetheryte(
                data.PlaceName.Value.Name.ToString(),
                aetheryte.AetheryteId,
                aetheryte.SubIndex,
                aetheryte.GilCost);
        }
    }

    private IEnumerable<ClickedAetheryte> GetTeleportOptionsForMapMarker(Lumina.Excel.Sheets.MapMarker marker)
    {
        var placeNameSubtextId = marker.PlaceNameSubtext.RowId;
        if (placeNameSubtextId == 0)
        {
            yield break;
        }

        foreach (var aetheryte in aetheryteList)
        {
            if (aetheryte.TerritoryId != mapStateManager.currentMap.TerritoryType.RowId)
            {
                continue;
            }

            var data = aetheryte.AetheryteData.Value;
            if (!data.IsAetheryte || data.Invisible || data.PlaceName.RowId != placeNameSubtextId)
            {
                continue;
            }

            yield return new ClickedAetheryte(
                data.PlaceName.Value.Name.ToString(),
                aetheryte.AetheryteId,
                aetheryte.SubIndex,
                aetheryte.GilCost);
        }
    }

    private static unsafe void TeleportToAetheryte(ClickedAetheryte aetheryte)
    {
        Telepo.Instance()->Teleport(aetheryte.AetheryteId, aetheryte.SubIndex);
    }

    private void DrawIcon(int iconid, AkuGameObject obj)
    {
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();

        var p = ((GetMapCoordinateFor3D(obj.pos)) * Scale) + DrawPosition - (texture.Size / 4.0f);

        if (configuration.DrawDebugSquares)
        {
            ImGui.SetCursorPos(p);
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size / 2.0f), ImGui.GetColorU32(configuration.TextColor), 3.0f);
        }
        ImGui.SetCursorPos(p);
        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        if (obj.isDownloaded && !IsMapMarker(iconid))
            ImGui.Image(texture.Handle, texture.Size / 2.0f, Vector2.Zero, Vector2.One, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        else
            ImGui.Image(texture.Handle, texture.Size / 2.0f, Vector2.Zero, Vector2.One, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
    }

    private bool DrawActorDot(AkuGameObject obj, bool hover = false, bool interactive = true)
    {
        var center = currentMapScreenPosition +
                     DrawPosition +
                     (GetPlayerMapPosition(obj.pos) +
                      GetMapOffsetVector() +
                      GetMapCenterOffsetVector()) * Scale;
        var isFriend = IsFriendPlayer(obj);
        var radius = hover ? 5.0f : 4.0f;
        var hitRadius = MathF.Max(radius + 3.0f, 8.0f);
        var fillColor = ImGui.GetColorU32(GetActorDotColor(obj, isFriend));
        var borderColor = ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.03f, 0.85f));
        var isHovered = false;

        if (interactive)
        {
            ImGui.SetCursorScreenPos(center - new Vector2(hitRadius));
            ImGui.InvisibleButton($"##actor_dot_{obj.objectKind}_{obj.unique_ingame_id}_{obj.uuid}", new Vector2(hitRadius * 2.0f));
            isHovered = ImGui.IsItemHovered();
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, fillColor, 16);
        drawList.AddCircle(center, radius, borderColor, 16, MathF.Max(1.0f, ImGuiHelpers.GlobalScale));

        if (!interactive)
        {
            return false;
        }

        if (isHovered)
        {
            DrawTooltip(obj);
            DrawActorDot(obj, true, false);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.OpenPopup("AkuTrack_AkuObject_Context_Menu");
            AddClickedObject(obj);
            AddNearbyElementsToSelection(ImGui.GetMousePos());
        }

        return true;
    }

    private void AddNearbyElementsToSelection(Vector2 screenPosition)
    {
        const float selectionRadius = 16.0f;
        var scope = GetCurrentContentScope();

        foreach (var obj in objTrackManager.liveAkuObjects)
        {
            if (IsObjectSelectableNear(obj, MapObjectSource.SelfFound, scope, screenPosition, selectionRadius))
            {
                AddClickedObject(obj);
            }
        }

        if (ShouldDrawContent("RemoteMarker", scope))
        {
            foreach (var obj in objTrackManager.downloadHashList.Values)
            {
                if (IsObjectSelectableNear(obj, MapObjectSource.Downloaded, scope, screenPosition, selectionRadius))
                {
                    AddClickedObject(obj);
                }
            }
        }

        AddNearbyMapMarkersToSelection(screenPosition, selectionRadius, scope);
        AddNearbyTreasureMapSpotsToSelection(screenPosition, selectionRadius, scope);
    }

    private bool IsObjectSelectableNear(AkuGameObject obj, MapObjectSource source, MapContentScope scope, Vector2 screenPosition, float selectionRadius)
    {
        return obj.mid == mapStateManager.currentMap.RowId &&
            !IsLocalPlayerObject(obj) &&
            ShouldDrawObjectKind(obj.objectKind, source, scope) &&
            MatchesMapSearch(obj) &&
            Vector2.Distance(screenPosition, GetMapScreenPosition(obj.pos)) <= selectionRadius;
    }

    private void AddNearbyMapMarkersToSelection(Vector2 screenPosition, float selectionRadius, MapContentScope scope)
    {
        try
        {
            var rows = dataManager.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>().GetRow(mapStateManager.currentMap.MapMarkerRange);
            foreach (var row in rows)
            {
                if (row.X == 0 && row.Y == 0 || !ShouldDrawMapMarker(row, scope) || !MatchesMapSearch(row))
                {
                    continue;
                }

                var markerScreenPosition = currentMapScreenPosition + DrawPosition + new Vector2(row.X, row.Y) * Scale;
                if (Vector2.Distance(screenPosition, markerScreenPosition) <= selectionRadius)
                {
                    AddClickedMarker(row);
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private void AddNearbyTreasureMapSpotsToSelection(Vector2 screenPosition, float selectionRadius, MapContentScope scope)
    {
        if (!ShouldDrawContent("TreasureMaps", scope))
        {
            return;
        }

        foreach (var spot in GetTreasureMapSpotsForCurrentMap())
        {
            if (!configuration.IsTreasureMapRankEnabled(spot.RankId) ||
                !MatchesMapSearch("TreasureMaps", "Treasure map", spot.RankName, spot.RankId.ToString()))
            {
                continue;
            }

            var size = new Vector2(220.0f, 200.0f) * Scale;
            var center = GetMapScreenPosition(spot.Position);
            var insideSpot = IsBoundedBy(screenPosition, center - size / 2.0f, center + size / 2.0f);
            if (insideSpot || Vector2.Distance(screenPosition, center) <= selectionRadius)
            {
                AddClickedObject(CreateTreasureMapSpotObject(spot));
            }
        }
    }

    private void AddClickedObject(AkuGameObject obj)
    {
        var key = GetObjectSelectionKey(obj);
        if (clickedObjects.Any(clicked => GetObjectSelectionKey(clicked) == key))
        {
            return;
        }

        clickedObjects.Add(obj);
    }

    private static string GetObjectSelectionKey(AkuGameObject obj)
    {
        return $"{obj.objectKind}:{obj.unique_ingame_id}:{obj.uuid}:{obj.bid}:{obj.pos.X:F2}:{obj.pos.Y:F2}:{obj.pos.Z:F2}";
    }

    private void AddClickedMarker(Lumina.Excel.Sheets.MapMarker marker)
    {
        if (clickedMarkers.Any(clicked => clicked.RowId == marker.RowId && clicked.SubrowId == marker.SubrowId))
        {
            return;
        }

        clickedMarkers.Add(marker);
    }

    private AkuGameObject CreateFlagObject(Vector3 position)
    {
        return CreateSyntheticMapObject("Flag", mapStateManager.currentMap.RowId, "Flag", position);
    }

    private AkuGameObject CreateSyntheticMapObject(string type, uint baseId, string name, Vector3 position)
    {
        return new AkuGameObject(new DownloadGameObject
        {
            created_at = DateTimeOffset.Now,
            last_seen_at = DateTimeOffset.Now,
            objecttype = nameof(ObjectKind.None),
            zone_id = mapStateManager.currentMap.TerritoryType.RowId,
            map_id = mapStateManager.currentMap.RowId,
            base_id = baseId,
            x = position.X,
            y = position.Y,
            z = position.Z,
            rotation = 0,
            uuid = $"{type}:{mapStateManager.currentMap.RowId}:{baseId}:{position.X:F2}:{position.Z:F2}",
        })
        {
            syntheticType = type,
            name = name,
            isDownloaded = false,
        };
    }

    private bool MatchesMapSearch(AkuGameObject obj)
    {
        if (!mapStateManager.filterEnabled || mapStateManager.filterExpression == string.Empty)
        {
            return true;
        }

        return (obj.name?.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            obj.t.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ||
            obj.bid.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase) ||
            (obj.nid is not null && obj.nid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase)) ||
            (obj.npiid is not null && obj.npiid.Value.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool MatchesMapSearch(Lumina.Excel.Sheets.MapMarker marker)
    {
        return !mapStateManager.filterEnabled ||
            mapStateManager.filterExpression == string.Empty ||
            marker.PlaceNameSubtext.Value.Name.ToString().Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool MatchesMapSearch(params string[] values)
    {
        return !mapStateManager.filterEnabled ||
            mapStateManager.filterExpression == string.Empty ||
            values.Any(value => value.Contains(mapStateManager.filterExpression, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string FormatTimeRemaining(long seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private Vector2 GetMapScreenPosition(Vector3 position)
    {
        return currentMapScreenPosition +
               DrawPosition +
               (GetPlayerMapPosition(position) +
                GetMapOffsetVector() +
                GetMapCenterOffsetVector()) * Scale;
    }

    private static Vector4 GetActorDotColor(AkuGameObject obj, bool isFriend)
    {
        if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
        {
            return new Vector4(1.0f, 0.72f, 0.18f, 0.95f);
        }

        if (obj.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount)
        {
            return new Vector4(0.78f, 0.48f, 1.0f, 0.95f);
        }

        return isFriend
            ? new Vector4(0.1f, 1.0f, 0.55f, 0.95f)
            : new Vector4(0.15f, 0.65f, 1.0f, 0.9f);
    }

    private bool IsFriendPlayer(AkuGameObject obj)
    {
        if (obj.unique_ingame_id is not { } gameObjectId)
        {
            return false;
        }

        return objectTable.SearchById(gameObjectId) is ICharacter character &&
            character.StatusFlags.HasFlag(StatusFlags.Friend);
    }

    private bool IsPartyMember(AkuGameObject obj)
    {
        if (obj.unique_ingame_id is not { } gameObjectId || partyList.Length == 0)
        {
            return false;
        }

        return partyList.Any(member => member.EntityId == gameObjectId);
    }

    private Vector4 GetPlayerMarkerTint(uint classJobId, Vector4 fallback)
    {
        if (!configuration.ColorPlayerMarkersByClass)
        {
            return fallback;
        }

        if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().TryGetRow(classJobId, out var classJob))
        {
            return fallback;
        }

        return classJob.JobType switch
        {
            1 => new Vector4(0.25f, 0.48f, 1.0f, 1.0f),
            2 or 6 => new Vector4(0.25f, 0.95f, 0.45f, 1.0f),
            3 or 4 or 5 => new Vector4(1.0f, 0.05f, 0.05f, 1.0f),
            _ when IsClassJobCategory(classJob, "Disciple of the Hand") => new Vector4(0.95f, 0.75f, 0.25f, 1.0f),
            _ when IsClassJobCategory(classJob, "Disciple of the Land") => new Vector4(0.35f, 0.9f, 0.85f, 1.0f),
            _ => fallback,
        };
    }

    private bool IsClassJobCategory(Lumina.Excel.Sheets.ClassJob classJob, string englishCategoryName)
    {
        return dataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJobCategory>(ClientLanguage.English)
            .TryGetRow(classJob.ClassJobCategory.RowId, out var category)
            && category.Name.ToString() == englishCategoryName;
    }

    private void DrawMapIcon(int iconid, Vector2 position, float rotation, string text, byte subtextOrientation)
    {
        if (IsDoubleHousingArea(iconid))
            return;
        var texture = textureProvider.GetFromGameIcon(iconid).GetWrapOrEmpty();
            //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        if (IsRegionIcon(iconid)) {
            var regionScaleFactor = 0.84f;
            // FIXME: Shading of region icons is broken (they are white)
            var p = (position * Scale) + DrawPosition - (texture.Size * regionScaleFactor / 4.0f * Scale);
            ImGui.SetCursorPos(p);
            ImGui.Image(texture.Handle, texture.Size * regionScaleFactor / 2.0f * Scale);
            if(configuration.DrawDebugSquares) {
                ImGui.SetCursorPos(p);
                var cursorPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size * regionScaleFactor / 2.0f * Scale), ImGui.GetColorU32(configuration.TextColor), 3.0f);
            }
            if (text != string.Empty)
            {
                var ap = p + (texture.Size * regionScaleFactor / 4.0f * Scale);
                ImGui.SetCursorPos(ap);
                ImGui.TextColored(configuration.TextColor, text.ToString());
            }
        } else {
            var p = (position * Scale) + DrawPosition - (texture.Size / 4.0f);
            ImGui.SetCursorPos(p);
            ImGui.Image(texture.Handle, texture.Size / 2.0f);
            if(configuration.DrawDebugSquares)
            {
                ImGui.SetCursorPos(p);
                var cursorPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(cursorPos, cursorPos + (texture.Size / 2.0f), ImGui.GetColorU32(configuration.TextColor), 3.0f);
            }
            if (text != string.Empty)
            {
                var ap = p;
                // FIXME: Map Icon Text is moved (left of marker, above marker, right of marker etc) on the ingame map whereas we render it just at the marker's position
                /*
                switch(subtextOrientation) {
                    case 1:
                        ap.Y += texture.Size.Y / 2.0f / 4.0f;
                        break;
                    case 2:
                        ap.Y += texture.Size.Y / 2.0f / 4.0f;
                        break;
                    case 3:
                        break;
                    case 4:
                        break;
                    default:
                        break;
                }
                */
                ImGui.SetCursorPos(ap);
                ImGui.TextColored(configuration.TextColor, text.ToString());
            }
        }
    }

    public static bool IsRegionIcon(int iconId) =>
       iconId switch
       {
           >= 63200 and < 63900 => true,
           >= 62620 and < 62800 => true,
           _ => false,
       };

    public static bool IsDoubleHousingArea(int iconId) {
        if (iconId == 63249 /* Goblet */ || iconId == 63210 /* Mist */ || iconId == 63228 /* Lavender Beds */ || iconId == 63383 /* Shirogane */ || iconId == 63266 /* Empyreum */)
            return true;
        return false;
    }

    private bool DrawPlayerIcon(Vector3 pos, float rotation)
    {
        return DrawPlayerIcon(pos, rotation, Vector4.One, 1.0f);
    }

    private bool DrawPlayerIcon(Vector3 pos, float rotation, Vector4 tint, float iconScale)
    {
        var texture = textureProvider.GetFromGameIcon(60443).GetWrapOrEmpty();
        var angle = -rotation + MathF.PI / 2.0f;
        var scaledSize = texture.Size / 2.0f * Scale * iconScale;
        var minimumSize = new Vector2(36.0f * ImGuiHelpers.GlobalScale * iconScale);
        var maximumSize = new Vector2(96.0f * ImGuiHelpers.GlobalScale * iconScale);
        var size = Vector2.Clamp(scaledSize, minimumSize, maximumSize);

        var p = currentMapScreenPosition +
                DrawPosition +
                (GetPlayerMapPosition(pos) +
                 GetMapOffsetVector() +
                 GetMapCenterOffsetVector()) * Scale;
        //var p = ((GetMapCoordinateFor3D(pos)) * Scale) + DrawPosition - (texture.Size / 4.0f * Scale);
        var vectors = GetRotationVectors(angle, p, size);

        //log.Debug($"@ {position} Drawing to {p} with scale {Scale} DrawPosition: {DrawPosition}");
        ImGui.GetWindowDrawList().AddImageQuad(
            texture.Handle,
            vectors[0],
            vectors[1],
            vectors[2],
            vectors[3],
            Vector2.Zero,
            new Vector2(1, 0),
            Vector2.One,
            new Vector2(0, 1),
            ImGui.GetColorU32(tint));

        return IsMouseInsideMapCanvas() && IsBoundedBy(ImGui.GetMousePos(), p - size / 2.0f, p + size / 2.0f);
    }

    private unsafe void DrawCameraCone(Vector3 pos)
    {
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null)
        {
            return;
        }

        var camera = cameraManager->GetActiveCamera();
        if (camera == null)
        {
            return;
        }

        var center = currentMapScreenPosition +
                     DrawPosition +
                     (GetPlayerMapPosition(pos) +
                      GetMapOffsetVector() +
                      GetMapCenterOffsetVector()) * Scale;

        var angle = -camera->CalculateSceneCameraYaw() + MathF.PI * 1.5f;
        const float halfConeAngle = 75.0f * MathF.PI / 360.0f;
        var coneOrigin = center;
        var coneLength = 43.0f * Scale;
        var left = coneOrigin + AngleToDirection(angle - halfConeAngle) * coneLength;
        var right = coneOrigin + AngleToDirection(angle + halfConeAngle) * coneLength;

        var fillColor = ImGui.GetColorU32(new Vector4(0.05f, 0.75f, 1.0f, 0.28f));
        var lineColor = ImGui.GetColorU32(new Vector4(0.25f, 0.9f, 1.0f, 0.85f));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddTriangleFilled(coneOrigin, left, right, fillColor);
        drawList.AddTriangle(coneOrigin, left, right, lineColor, MathF.Max(1.0f, Scale));
    }

    private static Vector2 AngleToDirection(float angle)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private void ProcessInputs() {
        if (HoveredFlags.Any())
        {
            if (ImGui.GetIO().KeyShift)
            {
                Flags &= ~ImGuiWindowFlags.NoMove;
            }
            else
            {
                ProcessMouseScroll();
                ProcessMapFlagClick();
                ProcessMapDragStart();
                Flags |= ImGuiWindowFlags.NoMove;
            }
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        ProcessMapDragDragging();
        ProcessMapDragEnd();
    }

    private void ProcessMouseScroll()
    {
        if (ImGui.GetIO().MouseWheel is 0) return;
        if (!HoveredFlags.HasFlag(HoverFlags.WindowInnerFrame)) return;

        Scale += ZoomSpeed * ImGui.GetIO().MouseWheel;
        Scale = Math.Clamp(Scale, 0.25f, 100.0f);
    }

    private unsafe void ProcessMapFlagClick()
    {
        if (suppressFlagPlacement)
        {
            suppressFlagPlacement = false;
            return;
        }

        if (!HoveredFlags.HasFlag(HoverFlags.MapTexture)) return;
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Right)) return;

        if (TryClearFlagAtMousePosition())
        {
            return;
        }

        var mapCoordinate = GetMouseMapCoordinate();
        if (ImGui.GetIO().KeyCtrl)
        {
            PlaceFlagAtMapCoordinate(mapCoordinate);
            return;
        }

        if (IsBoundedBy(mapCoordinate, Vector2.Zero, new Vector2(2048, 2048)))
        {
            contextMenuMapCoordinate = mapCoordinate;
            ImGui.OpenPopup("AkuTrack_Context_Menu");
        }
    }

    private unsafe bool TryClearFlagAtMousePosition()
    {
        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->FlagMarkerCount == 0)
        {
            return false;
        }

        var flag = agentMap->FlagMapMarkers[0];
        if (flag.MapId != mapStateManager.currentMap.RowId ||
            flag.TerritoryId != mapStateManager.currentMap.TerritoryType.RowId)
        {
            return false;
        }

        var flagPosition = new Vector3(flag.XFloat, 0, flag.YFloat);
        var flagScreenPosition = GetMapScreenPosition(flagPosition);
        var hitRadius = MathF.Max(18.0f, 18.0f * ImGuiHelpers.GlobalScale);
        if (Vector2.Distance(ImGui.GetMousePos(), flagScreenPosition) > hitRadius)
        {
            return false;
        }

        agentMap->FlagMarkerCount = 0;
        lastFocusedFlag = null;
        suppressFlagPlacement = true;
        return true;
    }

    private void ProcessMapDragStart()
    {
        // Don't allow a drag to start if the window size is changing
        if (ImGui.GetWindowSize() == lastWindowSize && HoveredFlags != HoverFlags.Nothing)
        {
            if (HoveredFlags.HasFlag(HoverFlags.MapTexture) && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !isDragStarted)
            {
                isDragStarted = true;
                //System.SystemConfig.FollowPlayer = false;
            }
        }
        else
        {
            lastWindowSize = ImGui.GetWindowSize();
            isDragStarted = false;
        }
    }

    private void ProcessMapDragDragging()
    {
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && isDragStarted)
        {
            keepPlayerCenteredPaused = true;
            DrawOffset += ImGui.GetMouseDragDelta() / Scale;
            ImGui.ResetMouseDragDelta();
        }
    }

    private void ProcessMapDragEnd()
    {
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            isDragStarted = false;
        }
    }

    private void UpdateDrawOffset()
    {
        var childCenterOffset = ImGui.GetContentRegionAvail() / 2.0f;
        var mapCenterOffset = new Vector2(1024.0f, 1024.0f) * Scale;

        DrawPosition = childCenterOffset - mapCenterOffset + (DrawOffset * Scale);
    }

    private void CenterOnLocalPlayer()
    {
        if (objectTable.LocalPlayer is not { } localPlayer)
        {
            return;
        }

        DrawOffset = -(GetPlayerMapPosition(localPlayer.Position) + GetMapOffsetVector());
    }

    private void CenterOnWorldPosition(Vector3 position)
    {
        DrawOffset = GetMapCenterOffsetVector() - GetMapCoordinateFor3D(position);
    }

    private unsafe void ProcessPendingFlagFocus()
    {
        if (!pendingFlagFocus)
        {
            return;
        }

        var agentMap = AgentMap.Instance();
        if (agentMap == null || agentMap->FlagMarkerCount == 0)
        {
            pendingFlagFocus = false;
            return;
        }

        var flag = agentMap->FlagMapMarkers[0];
        if (flag.MapId != mapStateManager.currentMap.RowId || flag.TerritoryId != mapStateManager.currentMap.TerritoryType.RowId)
        {
            return;
        }

        keepPlayerCenteredPaused = true;
        CenterOnWorldPosition(new Vector3(flag.XFloat, 0, flag.YFloat));
        pendingFlagFocus = false;
    }

    private unsafe void PlaceFlagAtMapCoordinate(Vector2 mapCoordinate)
    {
        if (!IsBoundedBy(mapCoordinate, Vector2.Zero, new Vector2(2048, 2048)))
        {
            return;
        }

        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }

        agentMap->SetFlagMapMarker(
            mapStateManager.currentMap.TerritoryType.RowId,
            mapStateManager.currentMap.RowId,
            GetWorldPositionForCurrentMapCoordinate(mapCoordinate));

        var flag = agentMap->FlagMapMarkers[0];
        lastFocusedFlag = (flag.TerritoryId, flag.MapId, flag.XFloat, flag.YFloat);
        framework.RunOnTick(() => AgentChatLog.Instance()->InsertTextCommandParam(FlagTextCommandParamId, false));
    }

    private string FormatPlayerMapPosition(Vector3 worldPosition)
    {
        var mapPosition = TexturePixelToIngameCoord(GetMapCoordinateFor3D(worldPosition));
        return $"X:{mapPosition.X:F1} Y:{mapPosition.Y:F1} Z:{worldPosition.Y:F1}";
    }

    private Vector2 GetMouseMapCoordinate()
    {
        return (ImGui.GetMousePos() - currentMapScreenPosition - DrawPosition) / Scale;
    }

    private bool IsMouseInsideMapCanvas()
    {
        return HoveredFlags.HasFlag(HoverFlags.Window) &&
            IsBoundedBy(ImGui.GetMousePos(), currentMapScreenPosition, currentMapScreenPosition + currentMapPixelSize);
    }

    private Vector2 TexturePixelToIngameCoord(Vector2 textureCoord)
    {
        var rawMapPosition = (textureCoord - GetMapCenterOffsetVector()) / GetMapScaleFactor() - GetRawMapOffsetVector();
        var mapPixelPosition = rawMapPosition * GetMapScaleFactor();
        return new Vector2(
            (float)Math.Round(((41.0f / GetMapScaleFactor() * ((mapPixelPosition.X + 1024.0f) / 2048.0f) + 1) * 100) / 100, 1),
            (float)Math.Round(((41.0f / GetMapScaleFactor() * ((mapPixelPosition.Y + 1024.0f) / 2048.0f) + 1) * 100) / 100, 1));
    }

    private static Vector2[] GetRotationVectors(float angle, Vector2 center, Vector2 size)
    {
        var cosA = MathF.Cos(angle + 0.5f * MathF.PI);
        var sinA = MathF.Sin(angle + 0.5f * MathF.PI);

        Vector2[] vectors =
        [
            center + ImRotate(new Vector2(-size.X * 0.5f, -size.Y * 0.5f), cosA, sinA),
            center + ImRotate(new Vector2(+size.X * 0.5f, -size.Y * 0.5f), cosA, sinA),
            center + ImRotate(new Vector2(+size.X * 0.5f, +size.Y * 0.5f), cosA, sinA),
            center + ImRotate(new Vector2(-size.X * 0.5f, +size.Y * 0.5f), cosA, sinA),
        ];
        return vectors;
    }

    public Vector2 GetMapCoordinateFor3D(Vector3 pos)
    {
        var twoD = new Vector2(pos.X, pos.Z);
        var mapcoord = ((twoD + GetRawMapOffsetVector()) * GetMapScaleFactor()) + GetMapCenterOffsetVector();
        return mapcoord;
    }

    public static unsafe Vector3 GetWorldPositionForMapCoordinate(Vector2 mapCoordinate)
    {
        var agentMap = AgentMap.Instance();
        var rawMapOffset = new Vector2(agentMap->SelectedOffsetX, agentMap->SelectedOffsetY);
        var mapScaleFactor = agentMap->SelectedMapSizeFactorFloat;
        var twoD = ((mapCoordinate - GetMapCenterOffsetVector()) / mapScaleFactor) - rawMapOffset;
        return new Vector3(twoD.X, 0, twoD.Y);
    }

    private Vector3 GetWorldPositionForCurrentMapCoordinate(Vector2 mapCoordinate)
    {
        var twoD = ((mapCoordinate - GetMapCenterOffsetVector()) / GetMapScaleFactor()) - GetRawMapOffsetVector();
        return new Vector3(twoD.X, 0, twoD.Y);
    }

    public Vector2 GetPlayerMapPosition(Vector3 vec) => new Vector2(vec.X, vec.Z) * GetMapScaleFactor();
    private static Vector2 ImRotate(Vector2 v, float cosA, float sinA) => new(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);

    /// <summary>
    /// Offset Vector of SelectedX, SelectedY, scaled with SelectedSizeFactor
    /// </summary>
    public Vector2 GetMapOffsetVector() => GetRawMapOffsetVector() * GetMapScaleFactor();

    /// <summary>
    /// Unscaled Vector of SelectedX, SelectedY
    /// </summary>
    public Vector2 GetRawMapOffsetVector()
    {
        if (capturedAgentMapId == mapStateManager.currentMap.RowId && capturedAgentMapScaleFactor > 0)
        {
            return capturedAgentRawMapOffset;
        }

        return new Vector2(mapStateManager.currentMap.OffsetX, mapStateManager.currentMap.OffsetY);
    }

    /// <summary>
    /// Selected Scale Factor
    /// </summary>
    public float GetMapScaleFactor()
    {
        if (capturedAgentMapId == mapStateManager.currentMap.RowId && capturedAgentMapScaleFactor > 0)
        {
            return capturedAgentMapScaleFactor;
        }

        return (float)mapStateManager.currentMap.SizeFactor / 100.0f;
    }

    /// <summary>
    /// 1024 vector, center offset vector
    /// </summary>
    public static Vector2 GetMapCenterOffsetVector() => new(1024.0f, 1024.0f);

    public static bool IsBoundedBy(Vector2 cursor, Vector2 minBounds, Vector2 maxBounds)
    {
        if (cursor.X >= minBounds.X && cursor.Y >= minBounds.Y)
        {
            if (cursor.X <= maxBounds.X && cursor.Y <= maxBounds.Y)
            {
                return true;
            }
        }

        return false;
    }
}
