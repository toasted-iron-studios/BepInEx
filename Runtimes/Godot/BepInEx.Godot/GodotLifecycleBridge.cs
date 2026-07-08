using System;

namespace BepInEx.Godot;

/// <summary>
/// Late-bound, typed hand-off from the (GodotSharp-free, low-floor) preloader to the optional,
/// engine-facing <c>BepInEx.Godot.Lifecycle</c> assembly.
///
/// The preloader deliberately references no GodotSharp type, so it cannot reference Lifecycle
/// directly. Instead Lifecycle registers its scene-tree pump starter here the first time a plugin
/// touches the scene-tree API (via <c>SceneTreeHook</c>'s static constructor). The preloader then
/// invokes it through this typed delegate — no reflection, no assembly-name strings.
///
/// If no plugin uses the scene-tree API, Lifecycle never loads, <see cref="StartSceneTreePump" />
/// stays null, and post-boot scheduling is a no-op ("patch-only" install).
/// </summary>
public static class GodotLifecycleBridge
{
    /// <summary>Set by <c>BepInEx.Godot.SceneTreeHook</c> (in the Lifecycle assembly) when it loads.</summary>
    public static Action StartSceneTreePump;
}
