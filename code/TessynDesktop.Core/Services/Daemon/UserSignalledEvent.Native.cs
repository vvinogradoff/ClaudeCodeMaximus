using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TessynDesktop.Services;

public sealed partial class UserSignalledEvent
{
	/// <summary>The Win32 calls that create an event with an explicit security descriptor.</summary>
	private static class Native
	{
		public const uint SddlRevision1 = 1;

		[StructLayout(LayoutKind.Sequential)]
		public struct SecurityAttributes
		{
			public int    Length;
			public IntPtr SecurityDescriptor;
			public int    InheritHandle;
		}

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string stringSecurityDescriptor, uint revision, out IntPtr securityDescriptor, out uint size);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern SafeWaitHandle CreateEventW(ref SecurityAttributes attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string name);

		[DllImport("kernel32.dll")]
		public static extern IntPtr LocalFree(IntPtr memory);
	}
}
