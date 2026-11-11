using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace SquadEditor.Core.Memory;

[SupportedOSPlatform("windows")]
internal static class Native
{
	public struct MEMORY_BASIC_INFORMATION64
	{
		public ulong BaseAddress;

		public ulong AllocationBase;

		public uint AllocationProtect;

		public uint Alignment1;

		public ulong RegionSize;

		public uint State;

		public uint Protect;

		public uint Type;

		public uint Alignment2;
	}

	public const uint PROCESS_VM_OPERATION = 8u;

	public const uint PROCESS_VM_READ = 16u;

	public const uint PROCESS_VM_WRITE = 32u;

	public const uint PROCESS_QUERY_INFORMATION = 1024u;

	public const uint MEM_COMMIT = 4096u;

	public const uint PAGE_NOACCESS = 1u;

	public const uint PAGE_READWRITE = 4u;

	public const uint PAGE_WRITECOPY = 8u;

	public const uint PAGE_EXECUTE_READWRITE = 64u;

	public const uint PAGE_EXECUTE_WRITECOPY = 128u;

	public const uint PAGE_GUARD = 256u;

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress, ref byte buffer, nuint size, out nuint bytesRead);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool WriteProcessMemory(SafeProcessHandle process, nint baseAddress, ref byte buffer, nuint size, out nuint bytesWritten);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address, out MEMORY_BASIC_INFORMATION64 buffer, nuint length);
}
