using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace AkuTrack.Windows;

public class SightseeingLogEntryWindow : Window, IDisposable
{
    private readonly WindowSystem windowSystem;
    private readonly SightseeingLogEntryInfo entry;

    public SightseeingLogEntryWindow(WindowSystem windowSystem, IPluginLog log, SightseeingLogEntryInfo entry)
        : base($"AkuTrack2 - Vista #{entry.RowId}##akutrack_sightseeing_{entry.RowId}")
    {
        this.windowSystem = windowSystem;
        this.entry = entry;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 180),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public override void OnClose()
    {
        windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        ImGui.TextUnformatted($"Name: {entry.Name}");
        ImGui.TextUnformatted($"Id: {entry.RowId}");

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            ImGui.Separator();
            ImGui.TextWrapped(entry.Description);
        }

        if (!string.IsNullOrWhiteSpace(entry.Emote))
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Emote: {entry.Emote}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Time))
        {
            ImGui.TextUnformatted($"Time: {entry.Time}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Weather))
        {
            ImGui.TextUnformatted($"Weather: {entry.Weather}");
        }
    }
}
