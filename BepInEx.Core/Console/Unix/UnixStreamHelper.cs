using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BepInEx.Unix;

internal static class UnixStreamHelper
{
    public delegate int dupDelegate(int fd);

    public delegate int fcloseDelegate(IntPtr stream);

    public delegate IntPtr fdopenDelegate(int fd, string mode);

    public delegate int fflushDelegate(IntPtr stream);

    public delegate IntPtr freadDelegate(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    public delegate int fwriteDelegate(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    public delegate int isattyDelegate(int fd);

    // MonoMod's DynDllImport was removed in MonoMod 25.x. On CoreCLR the runtime
    // resolves the bare "libc" name to the platform C library (libc.so.6 on Linux,
    // libSystem.dylib on macOS) via its own name mangling, so plain P/Invoke covers
    // the same targets the old DynDllMapping list did.
    private const string Libc = "libc";

    [DllImport(Libc, EntryPoint = "dup")]
    private static extern int dup_native(int fd);

    [DllImport(Libc, EntryPoint = "fdopen")]
    private static extern IntPtr fdopen_native(int fd, string mode);

    [DllImport(Libc, EntryPoint = "fread")]
    private static extern IntPtr fread_native(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    [DllImport(Libc, EntryPoint = "fwrite")]
    private static extern int fwrite_native(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    [DllImport(Libc, EntryPoint = "fclose")]
    private static extern int fclose_native(IntPtr stream);

    [DllImport(Libc, EntryPoint = "fflush")]
    private static extern int fflush_native(IntPtr stream);

    [DllImport(Libc, EntryPoint = "isatty")]
    private static extern int isatty_native(int fd);

    public static readonly dupDelegate dup = dup_native;
    public static readonly fdopenDelegate fdopen = fdopen_native;
    public static readonly freadDelegate fread = fread_native;
    public static readonly fwriteDelegate fwrite = fwrite_native;
    public static readonly fcloseDelegate fclose = fclose_native;
    public static readonly fflushDelegate fflush = fflush_native;
    public static readonly isattyDelegate isatty = isatty_native;

    public static Stream CreateDuplicateStream(int fileDescriptor)
    {
        var newFd = dup(fileDescriptor);

        return new UnixStream(newFd, FileAccess.Write);
    }
}
