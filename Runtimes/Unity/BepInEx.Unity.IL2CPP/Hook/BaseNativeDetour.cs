using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace BepInEx.Unity.IL2CPP.Hook;

internal abstract class BaseNativeDetour<T> : INativeDetour where T : BaseNativeDetour<T>
{
    protected static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(typeof(T).Name);

    protected BaseNativeDetour(nint originalMethodPtr, Delegate detourMethod)
    {
        OriginalMethodPtr = originalMethodPtr;
        DetourMethod = detourMethod;
        DetourMethodPtr = Marshal.GetFunctionPointerForDelegate(detourMethod);
    }

    public bool IsPrepared { get; protected set; }
    protected MethodInfo TrampolineMethod { get; set; }
    protected Delegate DetourMethod { get; set; }

    public nint OriginalMethodPtr { get; }
    public nint DetourMethodPtr { get; }
    public nint TrampolinePtr { get; protected set; }
    public bool IsValid { get; private set; } = true;
    public bool IsApplied { get; private set; }

    // MonoMod 25's IDetour carries a DetourConfig (ordering/priority for managed detours).
    // These are bespoke native (Dobby/Funchook) detours that don't participate in that ordering.
    public DetourConfig Config => null;

    public void Dispose()
    {
        if (!IsValid) return;
        Undo();
        Free();
    }

    public void Apply()
    {
        if (IsApplied) return;

        Prepare();
        ApplyImpl();

        Logger.Log(LogLevel.Debug,
                   $"Original: {OriginalMethodPtr:X}, Trampoline: {TrampolinePtr:X}, diff: {Math.Abs(OriginalMethodPtr - TrampolinePtr):X}");

        IsApplied = true;
    }

    public void Undo()
    {
        if (IsApplied && IsPrepared) UndoImpl();
    }

    public void Free()
    {
        FreeImpl();
        IsValid = false;
    }

    public MethodBase GenerateTrampoline(MethodBase signature = null)
    {
        if (TrampolineMethod == null)
        {
            Prepare();
            // MonoMod 25 removed DetourHelper.GenerateNativeProxy. Emit an equivalent managed proxy that
            // calli's into the trampoline pointer, using the signature's native calling convention.
            TrampolineMethod = GenerateNativeProxy(TrampolinePtr, (MethodInfo) signature);
        }

        return TrampolineMethod;
    }

    public TDelegate GenerateTrampoline<TDelegate>() where TDelegate : Delegate
    {
        if (!typeof(Delegate).IsAssignableFrom(typeof(TDelegate)))
            throw new InvalidOperationException($"Type {typeof(TDelegate)} not a delegate type.");

        // The delegate trampoline only needs the prepared pointer; no managed proxy method required.
        Prepare();

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(TrampolinePtr);
    }

    private static MethodInfo GenerateNativeProxy(nint functionPtr, MethodInfo signature)
    {
        var returnType = signature.ReturnType;
        var parameterTypes = signature.GetParameters().Select(p => p.ParameterType).ToArray();

        var callingConvention = signature.DeclaringType?
                                         .GetCustomAttribute<UnmanagedFunctionPointerAttribute>()?
                                         .CallingConvention ?? CallingConvention.Cdecl;

        using var dmd = new DynamicMethodDefinition($"NativeProxy<{signature.DeclaringType?.Name}>",
                                                    returnType, parameterTypes);
        var il = dmd.GetILGenerator();
        for (var i = 0; i < parameterTypes.Length; i++)
            il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Ldc_I8, (long) functionPtr);
        il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, callingConvention, returnType, parameterTypes);
        il.Emit(OpCodes.Ret);

        return dmd.Generate();
    }

    protected abstract void ApplyImpl();

    private void Prepare()
    {
        if (IsPrepared) return;
        Logger.LogDebug($"Preparing detour from 0x{OriginalMethodPtr:X2} to 0x{DetourMethodPtr:X2}");
        PrepareImpl();
        Logger.LogDebug($"Prepared detour; Trampoline: 0x{TrampolinePtr:X2}");
        IsPrepared = true;
    }

    protected abstract void PrepareImpl();

    protected abstract void UndoImpl();

    protected abstract void FreeImpl();
}
