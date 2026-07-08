using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BepInEx.Godot;

/// <summary>
/// Base class for a BepInEx plugin targeting a Godot .NET game — the Godot backend's
/// equivalent of <c>BepInEx.Unity.Mono</c> / <c>BepInEx.Unity.IL2CPP</c>'s BasePlugin.
///
/// Like every BepInEx backend it derives its Harmony instance, log source and config from
/// the plugin's <see cref="BepInPlugin" /> metadata, exposes an abstract <see cref="Load" />
/// (called during preload — the place to apply HarmonyX patches) and a virtual
/// <see cref="Unload" />.
///
/// This type references no GodotSharp assembly, so a plugin that only patches code needs
/// nothing engine-version-specific and inherits this backend's low framework floor. A plugin
/// that wants to spawn <c>Node</c>s references GodotSharp itself and does so once the scene
/// tree exists (the scene tree does not exist yet while <see cref="Load" /> runs during
/// preload — that engine-lifecycle layer is intentionally kept out of the generic base).
/// </summary>
public abstract class BasePlugin
{
    protected BasePlugin()
    {
        var metadata = MetadataHelper.GetMetadata(this);

        HarmonyInstance = new Harmony("BepInEx.Plugin." + metadata.GUID);
        Log = Logger.CreateLogSource(metadata.Name);
        Config = new ConfigFile(Utility.CombinePaths(Paths.ConfigPath, metadata.GUID + ".cfg"), false, metadata);
    }

    /// <summary>Log source named after the plugin, routed to BepInEx's log listeners.</summary>
    public ManualLogSource Log { get; }

    /// <summary>The plugin's own config file under <c>BepInEx/config</c>.</summary>
    public ConfigFile Config { get; }

    /// <summary>A Harmony instance scoped to this plugin; apply/undo patches through it.</summary>
    public Harmony HarmonyInstance { get; set; }

    /// <summary>Called once during preload. Apply HarmonyX patches here.</summary>
    public abstract void Load();

    /// <summary>Optionally undo the plugin's effects. Returns true if unload is supported.</summary>
    public virtual bool Unload() => false;
}
