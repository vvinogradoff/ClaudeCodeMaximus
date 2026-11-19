using System.Runtime.InteropServices;
using System.Text;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// The full path of a process's executable, read with the limited query right that a normal process also has on an elevated one (FR.32.14).
/// <see cref="System.Diagnostics.Process.MainModule"/> reads the process's memory, which an elevated process refuses to a normal one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessImagePath
{
	private const uint QueryLimitedInformation = 0x1000;   // PROCESS_QUERY_LIMITED_INFORMATION
	private const int MaxPath = 32767;

	/// <summary>The executable's full path; null when the process is gone or cannot be queried.</summary>
	public string? Get(int pid)
	{
		var process = OpenProcess(QueryLimitedInformation, inheritHandle: false, pid);
		if (process == IntPtr.Zero)
			return null;

		try
		{
			var buffer = new StringBuilder(MaxPath);
			var size = buffer.Capacity;
			return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
		}
		finally
		{
			CloseHandle(process);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref int size);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);
}
