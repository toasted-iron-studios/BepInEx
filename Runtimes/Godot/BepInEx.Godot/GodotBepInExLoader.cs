using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using GodotPlugins.Game;

namespace BepInEx.Godot;

/// <summary>
/// Boots the BepInEx chainloader inside a Godot .NET game. Mirrors
/// <c>BepInEx.NET.CoreCLR.NetCorePreloader</c>, minus the Cecil assembly-preloader pass
/// (Godot games are not statically patched — plugins hook at runtime via HarmonyX).
/// </summary>
public static class GodotBepInExLoader
{
    private static ManualLogSource _log;
    private static string _gameExePath;
    private static string _managedPath;

    /// <summary>
    /// Phase 1 — runs BEFORE the game boots (GodotSharp not yet initialized). Sets up logging,
    /// discovers the game assembly, initializes BepInEx paths and the console driver. Touches no
    /// engine API.
    /// </summary>
    public static void PreInit()
    {
        if (_log != null) return;

        // Stream BepInEx log output to the game's stdout.
        Logger.Listeners.Add(new StdoutLogListener());
        _log = Logger.CreateLogSource("BepInEx.Godot");

        _gameExePath = Process.GetCurrentProcess().MainModule.FileName;
        _log.LogMessage($"BepInEx.Godot preloader starting (host: {_gameExePath})");
        _log.LogInfo($"CLR runtime version: {Environment.Version}");

        // Make the game assembly available so plugins can resolve game types when they patch.
        // Discovered generically (no hard-coded name); its directory is the Godot
        // "data_<name>_<platform>" managed folder.
        try
        {
            Assembly game = GodotEntrypointGuard.ResolveGameAssembly();
            _managedPath = System.IO.Path.GetDirectoryName(game.Location);
            _log.LogInfo($"Game assembly: {game.FullName}");
            _log.LogInfo($"Game managed path: {_managedPath}");
        }
        catch (Exception e)
        {
            _log.LogWarning($"Could not resolve game assembly: {e.Message}");
        }

        // Initialize BepInEx paths for the Godot export layout. Godot has no Unity-style
        // "<Name>_Data" folder, so we point BepInEx at the export's managed directory
        // directly (GameDataPath becomes its parent = the export root, where BepInEx/ lives).
        if (_managedPath != null)
            Paths.SetExecutablePath(_gameExePath, managedPath: _managedPath, gameDataRelativeToManaged: true);

        // Initialize the console driver so the chainloader's logger setup has a valid driver.
        ConsoleManager.Initialize(false, true);

        // Run the chainloader NOW, before the game boots — so plugin Load() applies its HarmonyX
        // patches ahead of any game code running (the "preloader phase"). Plugins defer node/tree
        // work to SceneTreeHook.WhenReady, which fires in phase 2.
        try
        {
            var chainloader = new GodotChainloader();
            chainloader.Initialize(_managedPath != null ? null : _gameExePath);
            chainloader.Execute();
            _log.LogMessage("BepInEx.Godot chainloader finished (patches applied); booting game.");
        }
        catch (Exception e)
        {
            _log.LogError($"Chainloader failed: {e}");
        }
    }

    /// <summary>
    /// Phase 2 — runs AFTER the game's entrypoint (GodotSharp live) on the main thread. Starts the
    /// SceneTree watcher so plugins' <c>SceneTreeHook.WhenReady</c> callbacks fire once the tree
    /// exists (the place to create/add nodes). No-op if the engine-facing lifecycle assembly is
    /// absent (a patch-only install).
    /// </summary>
    public static void SchedulePostBoot()
    {
        var hook = Type.GetType("BepInEx.Godot.SceneTreeHook, BepInEx.Godot.Lifecycle");
        if (hook == null)
        {
            _log.LogInfo("Lifecycle assembly not installed; scene-tree callbacks disabled (patch-only).");
            return;
        }

        hook.GetMethod("Start").Invoke(null, null);
        _log.LogInfo("Watching for SceneTree; plugin WhenReady callbacks will fire once it exists.");
    }
}

/// <summary>Routes BepInEx log events to <see cref="Console.Out"/> (the game's stdout).</summary>
internal sealed class StdoutLogListener : ILogListener
{
    public LogLevel LogLevelFilter => LogLevel.All;

    public void LogEvent(object sender, LogEventArgs eventArgs) => Console.WriteLine(eventArgs.ToString());

    public void Dispose() { }
}
