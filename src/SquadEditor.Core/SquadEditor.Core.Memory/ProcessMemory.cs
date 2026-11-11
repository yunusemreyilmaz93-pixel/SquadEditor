using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace SquadEditor.Core.Memory;

[SupportedOSPlatform("windows")]
public sealed class ProcessMemory : IMemory, IDisposable
{
	private readonly Process _process;

	private readonly SafeProcessHandle _handle;

	public int ProcessId => _process.Id;

	public string ProcessName => _process.ProcessName;

	public bool HasExited => _process.HasExited;

	private ProcessMemory(Process process, SafeProcessHandle handle)
	{
		_process = process;
		_handle = handle;
	}

	public static ProcessMemory Attach(string processName)
	{
		if (!Environment.Is64BitProcess)
		{
			throw new PlatformNotSupportedException(CoreText.Get("Need64Bit"));
		}
		string text;
		if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			text = processName;
		}
		else
		{
			text = processName.Substring(0, processName.Length - 4);
		}
		string text2 = text;
		Process[] processesByName = Process.GetProcessesByName(text2);
		if (processesByName.Length == 0)
		{
			throw new InvalidOperationException(CoreText.Get("ProcessNotFound", text2));
		}
		Process process = processesByName.FirstOrDefault((Process p) => p.MainWindowHandle != 0) ?? processesByName[0];
		Process[] array = processesByName;
		foreach (Process process2 in array)
		{
			if (process2 != process)
			{
				process2.Dispose();
			}
		}
		return Open(process);
	}

	public static ProcessMemory AttachToCurrentProcess()
	{
		return Open(Process.GetCurrentProcess());
	}

	private static ProcessMemory Open(Process process)
	{
		SafeProcessHandle safeProcessHandle = Native.OpenProcess(1080u, inheritHandle: false, process.Id);
		if (safeProcessHandle.IsInvalid)
		{
			Win32Exception ex = new Win32Exception(Marshal.GetLastWin32Error());
			string processName = process.ProcessName;
			process.Dispose();
			throw new InvalidOperationException(CoreText.Get("AttachFailed", processName, ex.Message));
		}
		return new ProcessMemory(process, safeProcessHandle);
	}

	public bool TryRead(ulong address, Span<byte> buffer)
	{
		if (buffer.IsEmpty)
		{
			return true;
		}
		if (Native.ReadProcessMemory(_handle, (nint)address, ref MemoryMarshal.GetReference(buffer), (nuint)buffer.Length, out var bytesRead))
		{
			return bytesRead == (nuint)buffer.Length;
		}
		return false;
	}

	public bool TryWrite(ulong address, ReadOnlySpan<byte> data)
	{
		if (data.IsEmpty)
		{
			return true;
		}
		if (Native.WriteProcessMemory(_handle, (nint)address, ref MemoryMarshal.GetReference(data), (nuint)data.Length, out var bytesWritten))
		{
			return bytesWritten == (nuint)data.Length;
		}
		return false;
	}

	public IReadOnlyList<MemoryRegion> GetRegions()
	{
		List<MemoryRegion> list = new List<MemoryRegion>();
		nuint length = (nuint)Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION64>();
		ulong num = 0uL;
		Native.MEMORY_BASIC_INFORMATION64 buffer;
		while (Native.VirtualQueryEx(_handle, (nint)num, out buffer, length) != 0)
		{
			if (buffer.State == 4096 && (buffer.Protect & 0x100) == 0 && (buffer.Protect & 1) == 0 && buffer.Protect != 0)
			{
				bool isWritable = (buffer.Protect & 0xCC) != 0;
				list.Add(new MemoryRegion(buffer.BaseAddress, buffer.RegionSize, isWritable));
			}
			ulong num2 = buffer.BaseAddress + buffer.RegionSize;
			if (num2 <= num)
			{
				break;
			}
			num = num2;
		}
		return list;
	}

	public bool TryGetModule(string name, out MemoryRegion module)
	{
		_process.Refresh();
		foreach (ProcessModule module2 in _process.Modules)
		{
			if (string.Equals(module2.ModuleName, name, StringComparison.OrdinalIgnoreCase))
			{
				module = new MemoryRegion((ulong)module2.BaseAddress, (ulong)module2.ModuleMemorySize, IsWritable: false);
				return true;
			}
		}
		module = default;
		return false;
	}

	public void Dispose()
	{
		_handle.Dispose();
		_process.Dispose();
	}
}
