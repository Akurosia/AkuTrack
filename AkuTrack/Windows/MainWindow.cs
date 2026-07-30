using AkuTrack.ApiTypes;
using AkuTrack.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace AkuTrack.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly ObjTrackManager objTrackManager;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly ITextureProvider textureProvider;

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(ObjTrackManager objTrackManager, IDataManager dataManager, IClientState clientState, ITextureProvider textureProvider)
        : base("AkuTrack2##akutrack_main", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objTrackManager = objTrackManager;
        this.textureProvider = textureProvider;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

    }

    public void Dispose() { }

    public override void Draw()
    {

        if (ImGui.Button("Clear Seen List"))
        {
            objTrackManager.CleanSeen();
        }

        ImGui.Spacing();

        // Normally a BeginChild() would have to be followed by an unconditional EndChild(),
        // ImRaii takes care of this after the scope ends.
        // This works for all ImGui functions that require specific handling, examples are BeginTable() or Indent().
        using (var child = ImRaii.Child("SomeChildWithAScrollbar", Vector2.Zero, true))
        {
            // Check if this child is drawing
            if (child.Success)
            {
                ImGui.Text($"I have seen {objTrackManager.seenHashList.Count} objects since last reset.");

                ImGuiHelpers.ScaledDummy(20.0f);

                // Example for other services that Dalamud provides.
                // PlayerState provides a wrapper filled with information about the player character.

                var playerState = Plugin.PlayerState;
                if (!playerState.IsLoaded)
                {
                    ImGui.Text("Our local player is currently not logged in.");
                    return;
                }
                
                if (!playerState.ClassJob.IsValid)
                {
                    ImGui.Text("Our current job is currently not valid.");
                    return;
                }

                /*
                var x = dataManager.GetExcelSheet<Lumina.Excel.Sheets.TripleTriadCardResident>().ToList();
                ImGui.Text("TripleTriadCardResident");
                foreach (var y in x)
                {
                    ImGui.Text($"{y.RowId.ToString()}: {y.AcquisitionType.Value.Text.Value.Text}");
                }
                */
                ImGui.Text($"Objects still to upload [{objTrackManager.toUpload.Count}]:");
                foreach (var o in objTrackManager.toUpload)
                {
                    DrawAkuGameObject(o);
                }
                ImGui.Text($"Seen objects [{objTrackManager.seenHashList.Count}]:");
                foreach (var o in objTrackManager.seenHashList)
                {
                    DrawAkuGameObject(o.Value);
                }
                
                /*
                // If you want to see the Macro representation of this SeString use `.ToMacroString()`
                // More info about SeStrings: https://dalamud.dev/plugin-development/sestring/
                ImGui.Text($"Our current job is ({playerState.ClassJob.RowId}) '{playerState.ClassJob.Value.Abbreviation}' with level {playerState.Level}");

                // Example for querying Lumina, getting the name of our current area.
                var territoryId = Plugin.ClientState.TerritoryType;
                if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
                {
                    ImGui.Text($"We are currently in ({territoryId}) '{territoryRow.PlaceName.Value.Name}'");
                }
                else
                {
                    ImGui.Text("Invalid territory.");
                }
                */
            }
        }
    }

    private void DrawAkuGameObject(AkuGameObject o) {
        string headerText = string.Empty;
        var uniqueId = o.GetUniqueId();
        if(uniqueId is not null && objTrackManager.downloadHashList.ContainsKey(uniqueId)) {
            headerText = $"[D] [{o.bid}] {o.name}";
        } else {
            headerText = $"[{o.bid}] {o.name}";
        }
        if (ImGui.CollapsingHeader(headerText))
        {
            ImGui.Text($"BaseId: {o.bid}");
            //var map = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().FirstOrDefault(m => m.TerritoryType.RowId == clientState.TerritoryType);
            //ImGui.Text($"Map: {map.PlaceName.Value.Name}");
            ImGui.Text($"Position: {o.pos.ToString()}");
            if (o is ICharacter c)
            {
                ImGui.Text("This is an ICharacter!");
                ImGui.Text($"NameId: {c.NameId}");
            }
        }
    }
}
