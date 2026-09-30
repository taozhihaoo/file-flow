using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileFlow.Infrastructure.FileSystem;

/// <summary>
/// Sends files to the Windows Recycle Bin through the shell API (SHFileOperation).
/// No permission bypass is implemented: standard Windows ACLs apply as-is.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_SILENT = 0x04;
    private const ushort FOF_NOERRORUI = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOperation);

    /// <summary>Moves a single file to the Recycle Bin. Throws IOException on failure.</summary>
    public static void SendFileToRecycleBin(string path)
    {
        // pFrom must be double-null terminated.
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        var result = SHFileOperation(ref operation);
        if (result != 0)
        {
            throw new IOException($"Recycle Bin operation failed for '{path}' (shell error {result}).");
        }

        if (operation.fAnyOperationsAborted != 0)
        {
            throw new Win32Exception("Recycle Bin operation was aborted.");
        }
    }
}
