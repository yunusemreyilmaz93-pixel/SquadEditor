using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SquadEditor.Core.Memory;

/// <summary>
/// macOS (Apple Silicon ARM64 &amp; x64) process memory reader/writer using Mach VM APIs
/// (task_for_pid, mach_vm_read_overwrite, mach_vm_write, mach_vm_region, TASK_DYLD_INFO).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacProcessMemory : IMemory, IDisposable
{
	private const int KERN_SUCCESS = 0;
	private const int VM_REGION_BASIC_INFO_64 = 9;
	private const int VM_REGION_BASIC_INFO_COUNT_64 = 9;
	private const int VM_PROT_READ = 1;
	private const int VM_PROT_WRITE = 2;
	private const int TASK_DYLD_INFO = 17;
	private const int TASK_DYLD_INFO_COUNT = 5;
	private const uint MH_MAGIC_64 = 0xFEEDFACFu;
	private const uint LC_SEGMENT_64 = 0x19u;

	private readonly int _pid;
	private uint _task;
	private bool _disposed;

	public int ProcessId => _pid;

	private MacProcessMemory(int pid, uint task)
	{
		_pid = pid;
		_task = task;
	}

	public static MacProcessMemory Attach(string processName)
	{
		string target = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
			? processName[..^4]
			: processName;

		Process[] all = Process.GetProcesses();
		Process? proc = all.FirstOrDefault(p =>
			string.Equals(p.ProcessName, target, StringComparison.OrdinalIgnoreCase) ||
			p.ProcessName.Contains("Football Manager", StringComparison.OrdinalIgnoreCase));

		foreach (Process p in all)
		{
			if (p != proc) p.Dispose();
		}

		if (proc == null)
		{
			throw new InvalidOperationException($"FM27 process ('{target}') not found. Make sure Football Manager 27 is running with a save loaded.");
		}

		int pid = proc.Id;
		proc.Dispose();

		int kr = task_for_pid(mach_task_self(), pid, out uint task);
		if (kr != KERN_SUCCESS || task == 0)
		{
			throw new UnauthorizedAccessException(
				$"task_for_pid(pid={pid}) failed with kern_return={kr}. On macOS, run with sudo (and note that SIP / hardened runtime on signed Steam binaries may restrict task_for_pid).");
		}

		return new MacProcessMemory(pid, task);
	}

	public unsafe bool TryRead(ulong address, Span<byte> buffer)
	{
		if (_disposed || buffer.IsEmpty) return false;
		fixed (byte* ptr = buffer)
		{
			int kr = mach_vm_read_overwrite(_task, address, (ulong)buffer.Length, (ulong)ptr, out ulong outSize);
			return kr == KERN_SUCCESS && outSize == (ulong)buffer.Length;
		}
	}

	public unsafe bool TryWrite(ulong address, ReadOnlySpan<byte> data)
	{
		if (_disposed || data.IsEmpty) return false;
		fixed (byte* ptr = data)
		{
			int kr = mach_vm_write(_task, address, (ulong)ptr, (uint)data.Length);
			return kr == KERN_SUCCESS;
		}
	}

	public IReadOnlyList<MemoryRegion> GetRegions()
	{
		var regions = new List<MemoryRegion>();
		ulong address = 0;
		while (true)
		{
			uint count = VM_REGION_BASIC_INFO_COUNT_64;
			int kr = mach_vm_region(_task, ref address, out ulong size, VM_REGION_BASIC_INFO_64, out VmRegionBasicInfo64 info, ref count, out uint _);
			if (kr != KERN_SUCCESS || size == 0)
			{
				break;
			}
			if ((info.Protection & VM_PROT_READ) != 0)
			{
				regions.Add(new MemoryRegion(address, size, (info.Protection & VM_PROT_WRITE) != 0));
			}
			ulong next = address + size;
			if (next <= address) break;
			address = next;
		}
		return regions;
	}

	public bool TryGetModule(string name, out MemoryRegion module)
	{
		module = default;
		string needle = Path.GetFileNameWithoutExtension(name);
		if (string.IsNullOrEmpty(needle)) needle = name;

		foreach (var (imagePath, loadAddr) in EnumerateDyldImages())
		{
			string file = Path.GetFileName(imagePath);
			if (file.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
				imagePath.Contains(needle, StringComparison.OrdinalIgnoreCase))
			{
				ulong span = MeasureMachOImageSpan(loadAddr);
				if (span > 0)
				{
					module = new MemoryRegion(loadAddr, span, false);
					return true;
				}
			}
		}
		return false;
	}

	public List<(string Path, ulong LoadAddress)> EnumerateDyldImages()
	{
		var list = new List<(string, ulong)>();
		uint count = TASK_DYLD_INFO_COUNT;
		int kr = task_info(_task, TASK_DYLD_INFO, out TaskDyldInfo dyldInfo, ref count);
		if (kr != KERN_SUCCESS || dyldInfo.AllImageInfoAddr == 0)
		{
			return list;
		}

		// dyld_all_image_infos (64-bit): version (u32 @0), infoArrayCount (u32 @4), infoArray (u64 @8)
		Span<byte> hdr = stackalloc byte[16];
		if (!TryRead(dyldInfo.AllImageInfoAddr, hdr)) return list;
		uint infoCount = BitConverter.ToUInt32(hdr[4..8]);
		ulong infoArray = BitConverter.ToUInt64(hdr[8..16]);
		if (infoCount == 0 || infoCount > 4096 || infoArray == 0) return list;

		// Each dyld_image_info is 24 bytes: imageLoadAddress (u64), imageFilePath (u64), imageFileModDate (u64)
		byte[] entries = new byte[infoCount * 24];
		if (!TryRead(infoArray, entries)) return list;

		Span<byte> pathBuf = stackalloc byte[512];
		for (int i = 0; i < infoCount; i++)
		{
			ulong loadAddr = BitConverter.ToUInt64(entries, i * 24);
			ulong pathPtr = BitConverter.ToUInt64(entries, i * 24 + 8);
			if (loadAddr == 0 || pathPtr == 0) continue;
			string path = ReadCString(pathPtr, pathBuf);
			if (path.Length > 0) list.Add((path, loadAddr));
		}
		return list;
	}

	private string ReadCString(ulong addr, Span<byte> buf)
	{
		if (!TryRead(addr, buf))
		{
			// Fallback shorter read near page boundary
			Span<byte> small = buf[..64];
			if (!TryRead(addr, small)) return "";
			int z = small.IndexOf((byte)0);
			return Encoding.UTF8.GetString(z >= 0 ? small[..z] : small);
		}
		int zero = buf.IndexOf((byte)0);
		return Encoding.UTF8.GetString(zero >= 0 ? buf[..zero] : buf);
	}

	private ulong MeasureMachOImageSpan(ulong loadAddr)
	{
		// mach_header_64: magic(4), cputype(4), cpusubtype(4), filetype(4), ncmds(4), sizeofcmds(4), flags(4), reserved(4) = 32 bytes
		Span<byte> hdr = stackalloc byte[32];
		if (!TryRead(loadAddr, hdr)) return 0;
		if (BitConverter.ToUInt32(hdr[0..4]) != MH_MAGIC_64) return 0;

		uint ncmds = BitConverter.ToUInt32(hdr[16..20]);
		uint sizeofcmds = BitConverter.ToUInt32(hdr[20..24]);
		if (sizeofcmds == 0 || sizeofcmds > 1024 * 1024) return 0;

		byte[] cmds = new byte[sizeofcmds];
		if (!TryRead(loadAddr + 32, cmds)) return 0;

		ulong minVm = ulong.MaxValue, maxVm = 0;
		int off = 0;
		for (uint i = 0; i < ncmds && off + 8 <= cmds.Length; i++)
		{
			uint cmd = BitConverter.ToUInt32(cmds, off);
			uint cmdsize = BitConverter.ToUInt32(cmds, off + 4);
			if (cmdsize < 8 || off + cmdsize > cmds.Length) break;

			if (cmd == LC_SEGMENT_64 && cmdsize >= 56)
			{
				string segName = Encoding.ASCII.GetString(cmds, off + 8, 16).TrimEnd('\0');
				ulong vmaddr = BitConverter.ToUInt64(cmds, off + 24);
				ulong vmsize = BitConverter.ToUInt64(cmds, off + 32);
				if (segName != "__PAGEZERO" && vmsize > 0)
				{
					if (vmaddr < minVm) minVm = vmaddr;
					if (vmaddr + vmsize > maxVm) maxVm = vmaddr + vmsize;
				}
			}
			off += (int)cmdsize;
		}
		return maxVm > minVm ? maxVm - minVm : 0;
	}

	public void Dispose()
	{
		if (!_disposed && _task != 0)
		{
			mach_port_deallocate(mach_task_self(), _task);
			_task = 0;
		}
		_disposed = true;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct VmRegionBasicInfo64
	{
		public int Protection;
		public int MaxProtection;
		public uint Inheritance;
		public int Shared;
		public int Reserved;
		public ulong Offset;
		public int Behavior;
		public ushort UserWiredCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TaskDyldInfo
	{
		public ulong AllImageInfoAddr;
		public ulong AllImageInfoSize;
		public int AllImageInfoFormat;
	}

	[DllImport("libSystem.dylib")]
	private static extern uint mach_task_self();

	[DllImport("libSystem.dylib")]
	private static extern int task_for_pid(uint targetTask, int pid, out uint task);

	[DllImport("libSystem.dylib")]
	private static extern int mach_port_deallocate(uint task, uint name);

	[DllImport("libSystem.dylib")]
	private static extern int mach_vm_read_overwrite(uint targetTask, ulong address, ulong size, ulong data, out ulong outSize);

	[DllImport("libSystem.dylib")]
	private static extern int mach_vm_write(uint targetTask, ulong address, ulong data, uint dataCnt);

	[DllImport("libSystem.dylib")]
	private static extern int mach_vm_region(uint targetTask, ref ulong address, out ulong size, int flavor, out VmRegionBasicInfo64 info, ref uint infoCnt, out uint objectName);

	[DllImport("libSystem.dylib")]
	private static extern int task_info(uint targetTask, int flavor, out TaskDyldInfo info, ref uint infoCnt);
}
