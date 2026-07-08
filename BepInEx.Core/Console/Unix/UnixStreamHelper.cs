using System;
using System.IO;
using System.Runtime.InteropServices;
using MonoMod.Utils;

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

    public static dupDelegate dup;
    public static fdopenDelegate fdopen;
    public static freadDelegate fread;
    public static fwriteDelegate fwrite;
    public static fcloseDelegate fclose;
    public static fflushDelegate fflush;
    public static isattyDelegate isatty;

    static UnixStreamHelper()
    {
        // Resolve libc via MonoMod's DynDll, trying each candidate name in turn.
        var libc = OpenFirst("libc.so.6",               // Ubuntu glibc
                             "libc",                    // Linux glibc
                             "/usr/lib/libSystem.dylib" // OSX POSIX
                             );

        dup    = Bind<dupDelegate>(libc, "dup");
        fdopen = Bind<fdopenDelegate>(libc, "fdopen");
        fread  = Bind<freadDelegate>(libc, "fread");
        fwrite = Bind<fwriteDelegate>(libc, "fwrite");
        fclose = Bind<fcloseDelegate>(libc, "fclose");
        fflush = Bind<fflushDelegate>(libc, "fflush");
        isatty = Bind<isattyDelegate>(libc, "isatty");
    }

    private static IntPtr OpenFirst(params string[] names)
    {
        foreach (var name in names)
            if (DynDll.TryOpenLibrary(name, out var handle))
                return handle;
        throw new DllNotFoundException($"Could not load libc (tried: {string.Join(", ", names)})");
    }

    private static T Bind<T>(IntPtr library, string symbol) where T : Delegate =>
        (T) Marshal.GetDelegateForFunctionPointer(DynDll.GetExport(library, symbol), typeof(T));

    public static Stream CreateDuplicateStream(int fileDescriptor)
    {
        var newFd = dup(fileDescriptor);

        return new UnixStream(newFd, FileAccess.Write);
    }
}
