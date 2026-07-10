using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace BepInEx.Unity.IL2CPP.Hook;

/// <summary>
/// Builds a managed method that calls straight into a native function pointer, matching a given
/// delegate signature and its unmanaged calling convention.
///
/// This replaces MonoMod 22's removed <c>DetourHelper.GenerateNativeProxy</c>: it emits a tiny
/// method that pushes its arguments, loads the pointer, and <c>calli</c>s through it. Used to hand
/// out a <see cref="MethodBase"/> view of a native detour trampoline. Kept separate from
/// <see cref="BaseNativeDetour{T}"/> (it does not depend on the detour type parameter) so it can be
/// exercised in isolation.
///
/// A raw <see cref="DynamicMethod"/> is used rather than MonoMod's DynamicMethodDefinition because
/// the latter's IL generator does not support <c>EmitCalli</c> (it throws NotSupportedException);
/// <see cref="DynamicMethod"/>'s CoreCLR IL generator emits the unmanaged <c>calli</c> correctly.
/// </summary>
internal static class NativeProxyGenerator
{
    internal static MethodInfo GenerateNativeProxy(nint functionPtr, MethodInfo signature)
    {
        var returnType = signature.ReturnType;
        var parameterTypes = signature.GetParameters().Select(p => p.ParameterType).ToArray();

        var callingConvention = signature.DeclaringType?
                                         .GetCustomAttribute<UnmanagedFunctionPointerAttribute>()?
                                         .CallingConvention ?? CallingConvention.Cdecl;

        var proxy = new DynamicMethod($"NativeProxy<{signature.DeclaringType?.Name}>",
                                      returnType, parameterTypes,
                                      typeof(NativeProxyGenerator).Module, skipVisibility: true);
        var il = proxy.GetILGenerator();
        for (var i = 0; i < parameterTypes.Length; i++)
            il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Ldc_I8, (long) functionPtr);
        il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, callingConvention, returnType, parameterTypes);
        il.Emit(OpCodes.Ret);

        return proxy;
    }
}
