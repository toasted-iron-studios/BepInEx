using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Preloader.Core.Logging;

namespace BepInEx.Godot;

/// <summary>
/// Discovers and loads <see cref="BasePlugin" /> plugins from <c>BepInEx/plugins</c> for a
/// Godot .NET game. Mirrors the other backends' chainloaders (e.g. NetChainloader) on top of
/// the runtime-agnostic <see cref="BaseChainloader{TPlugin}" /> in BepInEx.Core.
/// </summary>
public class GodotChainloader : BaseChainloader<BasePlugin>
{
    public static GodotChainloader Instance { get; private set; }

    public override void Initialize(string gameExePath = null)
    {
        Instance = this;
        base.Initialize(gameExePath);
    }

    public override BasePlugin LoadPlugin(PluginInfo pluginInfo, Assembly pluginAssembly)
    {
        var type = pluginAssembly.GetType(pluginInfo.TypeName);
        var pluginInstance = (BasePlugin) Activator.CreateInstance(type);
        pluginInstance.Load();
        return pluginInstance;
    }

    protected override void InitializeLoggers()
    {
        base.InitializeLoggers();
        ChainloaderLogHelper.RewritePreloaderLogs();
    }
}
