using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RoninDiskManager.Engine;

internal static class NativeMethods
{
    // ── Access / share / creation flags ──────────────────────────────────────
    internal const uint GENERIC_READ               = 0x80000000;
    internal const uint FILE_SHARE_READ            = 0x00000001;
    internal const uint FILE_SHARE_WRITE           = 0x00000002;
    internal const uint OPEN_EXISTING              = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    // ── File attribute flags ──────────────────────────────────────────────────
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    // ── IOCTL control codes ───────────────────────────────────────────────────
    internal const uint FSCTL_ENUM_USN_DATA = 0x900B3;

    // ── Win32 error codes ─────────────────────────────────────────────────────
    internal const int ERROR_HANDLE_EOF = 38;

    // ── MFT_ENUM_DATA_V0 ──────────────────────────────────────────────────────
    // Input buffer for FSCTL_ENUM_USN_DATA — tells the kernel where to start
    // and the USN range to include.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_ENUM_DATA_V0
    {
        public ulong StartFileReferenceNumber;
        public long  LowUsn;
        public long  HighUsn;
    }

    // ── USN_RECORD_V2 ─────────────────────────────────────────────────────────
    // Fixed-size header for each record returned by FSCTL_ENUM_USN_DATA.
    // The variable-length FileName field sits immediately after this struct
    // in memory at offset FileNameOffset.
    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_RECORD_V2
    {
        public uint   RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong  FileReferenceNumber;
        public ulong  ParentFileReferenceNumber;
        public long   Usn;
        public long   TimeStamp;
        public uint   Reason;
        public uint   SourceInfo;
        public uint   SecurityId;
        public uint   FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
    }

    // ── Kernel32 imports ──────────────────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint   dwDesiredAccess,
        uint   dwShareMode,
        IntPtr lpSecurityAttributes,
        uint   dwCreationDisposition,
        uint   dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle       hDevice,
        uint                 dwIoControlCode,
        ref MFT_ENUM_DATA_V0 lpInBuffer,
        int                  nInBufferSize,
        IntPtr               lpOutBuffer,
        int                  nOutBufferSize,
        out uint             lpBytesReturned,
        IntPtr               lpOverlapped);
}
