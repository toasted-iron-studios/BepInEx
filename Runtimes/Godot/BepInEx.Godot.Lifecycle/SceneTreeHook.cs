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
/// BepInEx-for-Unity's split: patches in the preloader phase, scene/node work triggered from a
/// main-thread engine entrypoint.
///
/// The trigger uses Godot's own deferred-call mechanism rather than any engine-internal patching:
/// a <see cref="Callable"/> enqueued via <see cref="GodotObject.CallDeferred(StringName, Variant[])"/>-style
/// <c>CallDeferred</c> lands on the engine's native message queue, which is flushed each idle
/// iteration of the main loop — by which point the SceneTree is live. No Harmony, no reflection,
/// no polling loop; just the same deferred-call primitive game code uses.
/// </summary>
public static class SceneTreeHook
{
    private static readonly List<Action> Pending = new();
    private static bool _fired;

    static SceneTreeHook()
    {
        // Registered the moment a plugin first touches this type (during its Load()). The preloader,
        // which cannot reference this engine-facing assembly, invokes Start through this delegate.
        GodotLifecycleBridge.StartSceneTreePump = Start;
    }

    /// <summary>
    /// Registers <paramref name="onReady"/> to run on the main thread once the SceneTree exists.
    /// Safe to call during preload (before GodotSharp is initialized) — it only queues; nothing
    /// touches the engine until the hook fires. If the tree is already up, runs now.
    /// </summary>
    public static void WhenReady(Action onReady)
    {
        if (_fired) { onReady(); return; }
        Pending.Add(onReady);
    }

    /// <summary>
    /// Arms the SceneTree watcher. Called by the loader AFTER the game's runtime has initialized
    /// (GodotSharp live, on the main thread) via <see cref="GodotLifecycleBridge"/>.
    /// </summary>
    public static void Start()
    {
        if (_fired) return;
        DeferToNextIdle();
    }

    // Enqueue OnFrame onto the engine's native message queue; it is flushed on the next idle
    // iteration of the main loop, on the main thread — the same primitive as GodotObject.CallDeferred.
    private static void DeferToNextIdle()
    {
        Callable.From(OnFrame).CallDeferred();
    }

    // Runs on the main thread on the next idle flush. If the tree isn't up yet, re-defers one flush.
    private static void OnFrame()
    {
        if (_fired) return;
        if (Engine.GetMainLoop() is not SceneTree)
        {
            DeferToNextIdle();
            return;
        }
        _fired = true;
        Flush();
    }

    private static void Flush()
    {
        foreach (var cb in Pending)
        {
            try { cb(); }
            catch (Exception e) { GD.PushError($"[BepInEx.Godot] SceneTree-ready callback threw: {e}"); }
        }
        Pending.Clear();
    }
}
