using AkuTrack.Managers;
using AkuTrack.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace AkuTrack;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    public ServiceProvider serviceProvider { get; private set; }

    public Configuration Configuration { get; init; }

    public readonly WindowSystem windowSystem = new("AkuTrack");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MapWindow MapWindow { get; init; }
    private SearchWindow SearchWindow { get; init; }
    private bool wasGameMapVisible;
    private bool allowedGameMapVisible;
    private int hideGameMapFrames;
    private bool handledReplacementMapOpen;
    private readonly Hook<OpenMapDelegate> openMapHook;

    private unsafe delegate void OpenMapDelegate(AgentMap* thisPtr, OpenMapInfo* data);

    public Plugin(
        IFramework framework,
        IClientState clientState,
        IDalamudPluginInterface pluginInterface,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IChatGui chatGui,
        IGameGui gameGui,
        IPluginLog pluginLog,
        IObjectTable objectTable,
        IPartyList partyList,
        IFateTable fateTable,
        IAetheryteList aetheryteList,
        IGameInteropProvider gameInteropProvider,
        ITextureSubstitutionProvider textureSubstitutionProvider)
    {
        // You might normally want to embed resources and load them from the manifest stream
        //var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (configuration.Version < 1)
        {
            configuration.DrawCriticalEngagements = true;
            configuration.DrawContentFinderCriticalEngagements = true;
            configuration.Version = 1;
        }
        pluginInterface.SavePluginConfig(configuration);

        serviceProvider = new ServiceCollection()
            .AddSingleton(this)
            .AddSingleton(framework)
            .AddSingleton(clientState)
            .AddSingleton(pluginInterface)
            .AddSingleton(dataManager)
            .AddSingleton(textureProvider)
            .AddSingleton(chatGui)
            .AddSingleton(gameGui)
            .AddSingleton(pluginLog)
            .AddSingleton(objectTable)
            .AddSingleton(partyList)
            .AddSingleton(fateTable)
            .AddSingleton(aetheryteList)
            .AddSingleton(textureSubstitutionProvider)
            .AddSingleton(configuration)
            .AddSingleton<MainWindow>()
            .AddSingleton<ConfigWindow>()
            .AddSingleton<MapWindow>()
            .AddSingleton<SearchWindow>()
            .AddSingleton<UploadManager>()
            .AddSingleton<AllaganToolsIpc>()
            .AddSingleton<ObjTrackManager>()
            .AddSingleton<MapStateManager>()
            .AddSingleton<TopBar>()
            .AddSingleton<BottomBar>()
            .AddSingleton(windowSystem)
            .AddTransient<DetailsWindow>()
            .AddTransient<ItemExtraDataWindow>()
            .AddTransient<SightseeingLogEntryWindow>()
            .BuildServiceProvider();

        MainWindow = serviceProvider.GetRequiredService<MainWindow>();
        ConfigWindow = serviceProvider.GetRequiredService<ConfigWindow>();
        MapWindow = serviceProvider.GetRequiredService<MapWindow>();
        SearchWindow = serviceProvider.GetRequiredService<SearchWindow>();
        Configuration = serviceProvider.GetRequiredService<Configuration>();
        unsafe
        {
            openMapHook = gameInteropProvider.HookFromAddress<OpenMapDelegate>((nint)AgentMap.MemberFunctionPointers.OpenMap, OpenMapDetour);
        }


        windowSystem.AddWindow(ConfigWindow);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(MapWindow);
        windowSystem.AddWindow(SearchWindow);

        CommandManager.AddHandler("/akut", new CommandInfo((string command, string args) => { ToggleMainUi(); })
        {
            HelpMessage = "Opens the main menu with debug information."
        });

        CommandManager.AddHandler("/akum", new CommandInfo((string command, string args) => { MapWindow.Toggle(); }) {
            HelpMessage = "Opens the map window."
        });
        CommandManager.AddHandler("/akuc", new CommandInfo((string command, string args) => { ToggleConfigUi(); }) {
            HelpMessage = "Opens the configuration window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        clientState.Login += OnLogin;
        framework.Update += OnFrameworkUpdate;
        openMapHook.Enable();
        _ = serviceProvider.GetRequiredService<UploadManager>().ReloadChestDropsAsync();

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ClientState.Login -= OnLogin;
        serviceProvider.GetRequiredService<IFramework>().Update -= OnFrameworkUpdate;
        serviceProvider.GetRequiredService<ObjTrackManager>().Dispose();
        openMapHook.Dispose();
        
        windowSystem.RemoveAllWindows();

        MapWindow.Dispose();
        SearchWindow.Dispose();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler("/akut");
        CommandManager.RemoveHandler("/akum");
        CommandManager.RemoveHandler("/akuc");
        serviceProvider.Dispose();
    }
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    private void OnLogin()
    {
        _ = serviceProvider.GetRequiredService<UploadManager>().ReloadChestDropsAsync();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var isGameMapVisible = IsGameMapVisible();
        MapWindow.FocusCurrentFlagMarkerIfNeeded();

        if (Configuration.ReplaceGameMap)
        {
            if (allowedGameMapVisible)
            {
                if (!isGameMapVisible)
                {
                    allowedGameMapVisible = false;
                    wasGameMapVisible = false;
                }

                return;
            }

            if (hideGameMapFrames > 0)
            {
                hideGameMapFrames--;
                HideGameMapAddons();
                wasGameMapVisible = false;
                return;
            }

            if (isGameMapVisible)
            {
                if (IsGameMapModifierPressed())
                {
                    allowedGameMapVisible = true;
                    handledReplacementMapOpen = false;
                    wasGameMapVisible = true;
                    return;
                }

                if (!handledReplacementMapOpen)
                {
                    MapWindow.CaptureSelectedMapFromAgent();
                    MapWindow.IsOpen = !MapWindow.IsOpen;
                    handledReplacementMapOpen = true;
                }

                CloseGameMap();
                wasGameMapVisible = false;
                return;
            }

            handledReplacementMapOpen = false;
            wasGameMapVisible = false;
            return;
        }

        if (!Configuration.ToggleMapWithGameMap)
        {
            wasGameMapVisible = isGameMapVisible;
            return;
        }

        if (isGameMapVisible == wasGameMapVisible)
        {
            return;
        }

        wasGameMapVisible = isGameMapVisible;
        if (isGameMapVisible)
        {
            MapWindow.CaptureSelectedMapFromAgent();
        }

        MapWindow.IsOpen = isGameMapVisible;
    }

    private unsafe void OpenMapDetour(AgentMap* thisPtr, OpenMapInfo* data)
    {
        var isFlagMapOpen = data != null && data->Type == MapType.FlagMarker;
        var isMapKeyOpen = data == null;
        var replaceGameMap = Configuration.ReplaceGameMap;
        var allowGameMap = replaceGameMap && (allowedGameMapVisible || IsGameMapModifierPressed());
        openMapHook.Original(thisPtr, data);

        if (!replaceGameMap)
        {
            if (Configuration.ToggleMapWithGameMap)
            {
                MapWindow.CaptureSelectedMapFromAgent();
            }

            if (isFlagMapOpen)
            {
                MapWindow.FocusCurrentFlagMarkerOnNextDraw();
            }

            return;
        }

        allowedGameMapVisible = allowGameMap;

        if (!allowGameMap)
        {
            MapWindow.CaptureSelectedMapFromAgent();
            MapWindow.IsOpen = isMapKeyOpen ? !MapWindow.IsOpen : true;
            handledReplacementMapOpen = isMapKeyOpen;
            hideGameMapFrames = 3;
            CloseGameMap();
            wasGameMapVisible = false;
        }
        else
        {
            handledReplacementMapOpen = false;
        }

        if (isFlagMapOpen)
        {
            MapWindow.FocusCurrentFlagMarkerOnNextDraw();
        }
    }

    private bool IsGameMapModifierPressed()
    {
        var io = ImGui.GetIO();
        return Configuration.ReplaceGameMapModifier switch
        {
            GameMapOpenModifier.Ctrl => io.KeyCtrl || IsKeyDown(0x11),
            GameMapOpenModifier.Shift => io.KeyShift || IsKeyDown(0x10),
            GameMapOpenModifier.Alt => io.KeyAlt || IsKeyDown(0x12),
            _ => io.KeyCtrl || IsKeyDown(0x11),
        };
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static unsafe void CloseGameMap()
    {
        AgentMap.Instance()->Hide();
        HideGameMapAddons();
    }

    private static unsafe void HideGameMapAddons()
    {
        HideAddon("AreaMap");
        HideAddon("NaviMap");
    }

    private static unsafe void HideAddon(string addonName)
    {
        var addon = GameGui.GetAddonByName(addonName, 1);
        if (addon.IsNull || !addon.IsVisible)
        {
            return;
        }

        ((AtkUnitBase*)addon.Address)->Hide(false, false, 0);
    }

    private static bool IsGameMapVisible()
    {
        var areaMap = GameGui.GetAddonByName("AreaMap", 1);
        if (!areaMap.IsNull && areaMap.IsVisible)
        {
            return true;
        }

        var naviMap = GameGui.GetAddonByName("NaviMap", 1);
        return !naviMap.IsNull && naviMap.IsVisible;
    }
}
