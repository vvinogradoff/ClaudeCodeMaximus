using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Creates a named auto-reset event that every process of the current user may wait on and set, whatever its integrity level (FR.32.14). The host's exit
/// event needs it: a normal WPF client must be able to ask a host that runs elevated to exit. Created with the default security, an elevated process
/// labels its objects High, and a normal (Medium) process may then not set them. This event gets an explicit descriptor instead: the user may wait on
/// it and set it, SYSTEM and the Administrators group have full access, and the mandatory label is Medium with no-write-up, so a Low or AppContainer
/// process still cannot set it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class UserSignalledEvent
{
	private static readonly ILogger _log = Log.ForContext<UserSignalledEvent>();

	// D: the user gets SYNCHRONIZE | EVENT_MODIFY_STATE (0x00100002, what EventWaitHandle.OpenExisting asks for); SYSTEM and Administrators get GENERIC_ALL.
	// S: mandatory label Medium, no write up.
	private const string SddlFormat = "D:(A;;0x00100002;;;{0})(A;;GA;;;SY)(A;;GA;;;BA)S:(ML;;NW;;;ME)";

	/// <summary>The security descriptor (SDDL) the event is created with for the user <paramref name="userSid"/>.</summary>
	public string SecurityDescriptorFor(string userSid) => string.Format(CultureInfo.InvariantCulture, SddlFormat, userSid);

	/// <summary>
	/// Creates the named event, or opens it when it exists. Off Windows, or when the descriptor cannot be applied, it is a plain named event, which
	/// processes of the same integrity level can still set.
	/// </summary>
	public WaitHandle Create(string name)
	{
		if (!OperatingSystem.IsWindows())
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);

		var sid = WindowsIdentity.GetCurrent().User?.Value;
		if (sid == null || !Native.ConvertStringSecurityDescriptorToSecurityDescriptorW(SecurityDescriptorFor(sid), Native.SddlRevision1, out var descriptor, out _))
		{
			_log.Warning("Could not build the security descriptor of the event {Name} (error {Error}); a normal client cannot signal an elevated host", name, Marshal.GetLastPInvokeError());
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
		}

		try
		{
			var attributes = new Native.SecurityAttributes
			{
				Length             = Marshal.SizeOf<Native.SecurityAttributes>(),
				SecurityDescriptor = descriptor,
				InheritHandle      = 0,
			};
			var handle = Native.CreateEventW(ref attributes, manualReset: false, initialState: false, name);
			if (!handle.IsInvalid)
				return new NativeEvent(handle);

			var error = Marshal.GetLastPInvokeError();
			handle.Dispose();
			_log.Warning("Could not create the event {Name} with its security descriptor (error {Error}); a normal client cannot signal an elevated host", name, error);
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
		}
		finally
		{
			Native.LocalFree(descriptor);
		}
	}

	/// <summary>A wait handle over an event created natively.</summary>
	private sealed class NativeEvent : WaitHandle
	{
		public NativeEvent(SafeWaitHandle handle)
		{
			SafeWaitHandle = handle;
		}
	}
}
