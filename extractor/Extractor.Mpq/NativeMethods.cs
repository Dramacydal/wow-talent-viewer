using System.Runtime.InteropServices;

namespace Extractor.Mpq;

/// <summary>
/// Raw P/Invoke bindings for StormLib. Both vendored binaries (win-x64, linux-x64 —
/// see extractor/native/) are Ansi (char*) builds, so CharSet.Ansi is correct on both platforms.
/// </summary>
internal static class NativeMethods
{
    private const string Lib = "StormLib";

    public const uint MPQ_OPEN_READ_ONLY = 0x00000100;
    public const uint SFILE_OPEN_FROM_MPQ = 0x00000000;

    [DllImport(Lib, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileOpenArchive(string szMpqName, uint dwPriority, uint dwFlags, out nint phMpq);

    [DllImport(Lib, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileOpenPatchArchive(nint hMpq, string szPatchMpqName, string? szPatchPathPrefix, uint dwFlags);

    [DllImport(Lib, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileCloseArchive(nint hMpq);

    [DllImport(Lib, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileHasFile(nint hMpq, string szFileName);

    [DllImport(Lib, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileOpenFileEx(nint hMpq, string szFileName, uint dwSearchScope, out nint phFile);

    [DllImport(Lib, SetLastError = true)]
    public static extern uint SFileGetFileSize(nint hFile, nint pdwFileSizeHigh);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileReadFile(nint hFile, byte[] lpBuffer, uint dwToRead, out uint pdwRead, nint lpOverlapped);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SFileCloseFile(nint hFile);
}
