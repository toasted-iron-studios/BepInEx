using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace GodotPlugins.Game;

/// <summary>
/// GodotDoorstop injection target.
///
/// GodotDoorstop hooks CoreCLR's <c>load_assembly_and_get_function_pointer</c> and
/// redirects Godot's bootstrap lookup — <c>GodotPlugins.Game.Main.InitializeFromGameProject</c>
/// (native symbol <c>godotsharp_game_main_init</c>) — from the game assembly to THIS
/// assembly. We therefore expose the exact same type/method the Godot source generator
/// emits into the game, run BepInEx, then chainload the game's real entrypoint so the
/// game still boots.
///
/// This type is deliberately tiny and references nothing from BepInEx.* so that its JIT
/// does not force those assemblies to load before <see cref="GodotEntrypointGuard"/> has
/// registered the assembly resolver that finds them under <c>BepInEx/core</c>. It also
/// references no GodotSharp type: the return is plain <c>byte</c> (Godot's <c>godot_bool</c>
/// is an <c>enum : byte</c>, so this is ABI-identical), which keeps this assembly free of
/// any engine-version-specific reference and lets it target a low framework floor.
/// </summary>
internal static unsafe class Main
{
    [UnmanagedCallersOnly(EntryPoint = "godotsharp_game_main_init")]
    private static byte InitializeFromGameProject(IntPtr godotDllHandle, IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        // GodotDoorstop loads THIS assembly into an isolated component load context, but the
        // BepInEx chainloader loads plugins via Assembly.LoadFrom (which targets the Default
        // ALC). To keep a single instance of every BepInEx assembly — and therefore a single
        // set of statics like BepInEx.Paths — we run the whole loader in the Default ALC.
        GodotEntrypointGuard.RegisterResolver();

        // Pre-init (Paths, logging, resolver) BEFORE the game boots — cheap, GodotSharp-free.
        try { GodotEntrypointGuard.InvokeInDefaultAlc("PreInit"); }
        catch (Exception e) { GodotEntrypointGuard.LogFatal(e); }

        // Chainload the game's real entrypoint. This initializes GodotSharp's runtime interop.
        byte result = Chainload(godotDllHandle, outManagedCallbacks, unmanagedCallbacks, unmanagedCallbacksSize);

        // Now GodotSharp is live and we are still on the main thread — schedule the chainloader
        // to run once the SceneTree exists (so plugins can create nodes in Load()).
        try { GodotEntrypointGuard.InvokeInDefaultAlc("SchedulePostBoot"); }
        catch (Exception e) { GodotEntrypointGuard.LogFatal(e); }

        return result;
    }

    private static byte Chainload(IntPtr godotDllHandle, IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        // Discover the game's main assembly generically (no hard-coded name) and chainload
        // its real entrypoint.
        Assembly game = GodotEntrypointGuard.ResolveGameAssembly();
        Type mainType = game.GetType("GodotPlugins.Game.Main", throwOnError: true);
        MethodInfo mi = mainType.GetMethod("InitializeFromGameProject",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        // InitializeFromGameProject is [UnmanagedCallersOnly]; it can't be Invoke()d, but its
        // native entry can be called through its function pointer. The game returns a
        // godot_bool (enum : byte), which we read/return as a byte.
        var entry = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, byte>)mi.MethodHandle.GetFunctionPointer();
        return entry(godotDllHandle, outManagedCallbacks, unmanagedCallbacks, unmanagedCallbacksSize);
    }
}

/// <summary>
/// BCL-only helper (references no BepInEx.* type) that resolves BepInEx assemblies from the
/// <c>BepInEx/core</c> and <c>BepInEx/plugins</c> directories next to this preloader.
/// </summary>
internal static class GodotEntrypointGuard
{
    private static string[] _probeDirs;
    private static bool _registered;

    /// <summary>
    /// Locates the Godot game's main managed assembly generically — no hard-coded game
    /// name. In a Godot .NET export the app assembly is the one that has a
    /// <c>*.runtimeconfig.json</c> beside it and into which the Godot source generator
    /// emitted the entry type <c>GodotPlugins.Game.Main</c>. Works for any game.
    /// </summary>
    public static Assembly ResolveGameAssembly()
    {
        string managedDir = GameManagedDirectory();

        foreach (string cfg in Directory.GetFiles(managedDir, "*.runtimeconfig.json"))
        {
            string fileName = Path.GetFileName(cfg);
            string name = fileName.Substring(0, fileName.Length - ".runtimeconfig.json".Length);

            Assembly asm;
            try { asm = Assembly.Load(new AssemblyName(name)); }
            catch { continue; }

            if (asm.GetType("GodotPlugins.Game.Main") != null)
                return asm;
        }

        throw new InvalidOperationException(
            $"Could not locate the Godot game assembly in '{managedDir}': no assembly with a " +
            "*.runtimeconfig.json defines GodotPlugins.Game.Main.");
    }

    /// <summary>
    /// The export's managed directory (<c>data_&lt;name&gt;_&lt;platform&gt;</c>), located via
    /// GodotSharp.dll on the CoreCLR trusted-platform-assemblies list — present in every
    /// Godot .NET export.
    /// </summary>
    public static string GameManagedDirectory()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string p in tpa.Split(Path.PathSeparator))
            {
                if (Path.GetFileName(p).Equals("GodotSharp.dll", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(p);
            }
        }
        return AppContext.BaseDirectory;
    }

    public static void RegisterResolver()
    {
        if (_registered) return;
        _registered = true;

        string coreDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string bepinRoot = Path.GetDirectoryName(coreDir);
        string pluginDir = Path.Combine(bepinRoot, "plugins");
        string patcherDir = Path.Combine(bepinRoot, "patchers");
        _probeDirs = new[] { coreDir, pluginDir, patcherDir };

        // Resolve into the Default ALC so name-based binding shares a single instance of
        // every BepInEx assembly (using Assembly.LoadFrom here would create a separate
        // load-context identity, splitting static state such as BepInEx.Paths).
        AssemblyLoadContext.Default.Resolving += ResolveFromBepInEx;
    }

    private static Assembly _defaultCopy;

    /// <summary>
    /// Invokes a static, parameterless method on the Default-ALC copy of this preloader assembly,
    /// so BepInEx and all Assembly.LoadFrom'd plugins live in the same load context. The copy is
    /// loaded once and reused across calls (so its statics are shared).
    /// </summary>
    public static void InvokeInDefaultAlc(string method)
    {
        _defaultCopy ??= AssemblyLoadContext.Default.LoadFromAssemblyPath(Assembly.GetExecutingAssembly().Location);
        Type loader = _defaultCopy.GetType("BepInEx.Godot.GodotBepInExLoader", throwOnError: true);
        loader.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
    }

    private static Assembly ResolveFromBepInEx(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        string name = assemblyName.Name;
        foreach (string dir in _probeDirs)
        {
            if (dir == null || !Directory.Exists(dir)) continue;
            string direct = Path.Combine(dir, name + ".dll");
            if (File.Exists(direct)) return context.LoadFromAssemblyPath(direct);
            foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
            {
                string p = Path.Combine(sub, name + ".dll");
                if (File.Exists(p)) return context.LoadFromAssemblyPath(p);
            }
        }
        return null;
    }

    public static void LogFatal(Exception e)
    {
        try
        {
            string coreDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            File.WriteAllText(Path.Combine(coreDir, "bepinex_fatal.log"), e.ToString());
        }
        catch { }
        Console.WriteLine("[BepInEx.Godot] FATAL during preload: " + e);
    }
}
