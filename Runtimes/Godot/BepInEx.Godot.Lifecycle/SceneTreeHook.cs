using System;
using System.Collections.Generic;
using Godot;

namespace BepInEx.Godot;

/// <summary>
/// Lets plugins schedule work for the moment the Godot <see cref="SceneTree"/> exists.
///
/// Plugins call <see cref="WhenReady"/> from their <c>Load()</c> (which runs early, during
/// preload — the right place for HarmonyX patches). The registered callbacks fire later, on the
/// main thread, once the scene tree is up — the right place to create and add nodes. This mirrors
/// BepInEx-for-Unity's split: patches in the preloader phase, GameObject/scene work triggered
/// from a main-thread engine entrypoint.
/// </summary>
public static class SceneTreeHook
{
    private static readonly List<Action> Pending = new();
    private static bool _fired;

    /// <summary>
    /// Registers <paramref name="onReady"/> to run on the main thread once the SceneTree exists.
    /// Safe to call during preload (before GodotSharp is initialized) — it only queues; nothing
    /// touches the engine until <see cref="Start"/> fires. If the tree is already up, runs now.
    /// </summary>
    public static void WhenReady(Action onReady)
    {
        if (_fired)
        {
            onReady();
            return;
        }
        Pending.Add(onReady);
    }

    /// <summary>
    /// Begins watching for the SceneTree. Called by the loader AFTER the game's runtime has
    /// initialized (GodotSharp live), on the main thread. Re-defers each idle frame until the
    /// tree exists, then flushes all pending callbacks — no background thread touches the engine.
    /// </summary>
    public static void Start()
    {
        void Attempt()
        {
            if (Engine.GetMainLoop() is SceneTree)
            {
                _fired = true;
                foreach (var cb in Pending)
                {
                    try { cb(); }
                    catch (Exception e) { GD.PushError($"[BepInEx.Godot] SceneTree-ready callback threw: {e}"); }
                }
                Pending.Clear();
            }
            else
            {
                Callable.From(Attempt).CallDeferred();
            }
        }

        Callable.From(Attempt).CallDeferred();
    }
}
