namespace Extractor.Mpq;

/// <summary>
/// One open MPQ archive, optionally with patch MPQs layered on top via StormLib's
/// own patch-chain mechanism (SFileOpenPatchArchive) — reads then transparently
/// return the most-patched version of a file.
/// </summary>
public sealed class MpqArchive : IDisposable
{
    private nint _handle;
    private bool _disposed;

    private MpqArchive(nint handle) => _handle = handle;

    public static MpqArchive Open(string mpqPath)
    {
        if (!NativeMethods.SFileOpenArchive(mpqPath, 0, NativeMethods.MPQ_OPEN_READ_ONLY, out var handle))
            throw new MpqException($"SFileOpenArchive failed for '{mpqPath}'", Marshal_GetLastWin32Error());

        return new MpqArchive(handle);
    }

    /// <summary>Attach a patch MPQ on top of this archive. Call in chronological order (oldest patch first).</summary>
    public void ApplyPatch(string patchMpqPath)
    {
        if (!NativeMethods.SFileOpenPatchArchive(_handle, patchMpqPath, null, 0))
            throw new MpqException($"SFileOpenPatchArchive failed for '{patchMpqPath}'", Marshal_GetLastWin32Error());
    }

    public bool HasFile(string internalPath) => NativeMethods.SFileHasFile(_handle, internalPath);

    /// <summary>Read a file's full contents (post-patch) into memory. Internal path uses backslashes, e.g. DBFilesClient\Talent.dbc.</summary>
    public byte[] ReadFile(string internalPath)
    {
        if (!NativeMethods.SFileOpenFileEx(_handle, internalPath, NativeMethods.SFILE_OPEN_FROM_MPQ, out var fileHandle))
            throw new MpqException($"SFileOpenFileEx failed for '{internalPath}'", Marshal_GetLastWin32Error());

        try
        {
            var size = NativeMethods.SFileGetFileSize(fileHandle, 0);
            if (size == 0xFFFFFFFF)
                throw new MpqException($"SFileGetFileSize failed for '{internalPath}'", Marshal_GetLastWin32Error());

            var buffer = new byte[size];
            if (size > 0 && !NativeMethods.SFileReadFile(fileHandle, buffer, size, out var read, 0))
                throw new MpqException($"SFileReadFile failed for '{internalPath}'", Marshal_GetLastWin32Error());

            return buffer;
        }
        finally
        {
            NativeMethods.SFileCloseFile(fileHandle);
        }
    }

    private static int Marshal_GetLastWin32Error() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();

    public void Dispose()
    {
        if (_disposed) return;
        NativeMethods.SFileCloseArchive(_handle);
        _handle = 0;
        _disposed = true;
    }
}

public sealed class MpqException(string message, int errorCode) : Exception($"{message} (StormLib error code {errorCode})")
{
    public int ErrorCode { get; } = errorCode;
}
