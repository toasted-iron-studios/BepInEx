using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;

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
/// The trigger is found in Godot's own source (modules/mono): the engine calls
/// <c>ScriptManagerBridge.FrameCallback()</c> every idle frame from the main-loop iteration
/// (<c>CSharpLanguage::frame()</c>), on the main thread — its first call is the earliest managed
/// code that runs with a live SceneTree. <c>FrameCallback</c> itself is <c>[UnmanagedCallersOnly]</c>
/// and therefore cannot be patched (Harmony's trampoline would call it from managed code, which the
/// CLR forbids). But every frame it calls one ordinary managed method — <c>GodotTaskScheduler.Activate()</c>
/// — so we Harmony-postfix THAT, fire once, then unpatch. (There is no dedicated "main loop
/// initialized" managed callback in the engine, so this per-frame method is the correct hook point.)
/// </summary>
public static class SceneTreeHook
{
    private static readonly List<Action> Pending = new();
    private static bool _fired;

    private static Harmony _harmony;
    private static MethodBase _target;

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
    /// Installs the engine hook. Called by the loader AFTER the game's runtime has initialized.
    /// A self-cleaning Harmony postfix on the engine's per-frame managed callback.
    /// </summary>
    public static void Start()
    {
        if (_fired || _harmony != null) return;

        _target = AccessTools.Method(typeof(GodotTaskScheduler), "Activate");
        if (_target == null)
        {
            GD.PushWarning("[BepInEx.Godot] GodotTaskScheduler.Activate not found; " +
                           "scene-tree callbacks are unavailable on this Godot build.");
            return;
        }

        _harmony = new Harmony("bepinex.godot.scenetreehook");
        _harmony.Patch(_target, postfix: new HarmonyMethod(
            typeof(SceneTreeHook).GetMethod(nameof(OnFrame), BindingFlags.Static | BindingFlags.NonPublic)));
    }

    // Harmony postfix on GodotTaskScheduler.Activate — runs on the main thread every frame.
    private static void OnFrame()
    {
        if (_fired)
        {
            // Second visit: safe to remove ourselves now (never unpatch from the first invocation,
            // which is still on the stack). This is the "hook that gets undone later".
            if (_harmony != null)
            {
                var h = _harmony;
                _harmony = null;
                try { h.Unpatch(_target, HarmonyPatchType.Postfix, h.Id); } catch { /* best effort */ }
            }
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree) return; // defensive; FrameCallback runs post-tree
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
