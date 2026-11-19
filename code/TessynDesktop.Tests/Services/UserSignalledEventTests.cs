using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.HS.29 (FR.32.14): the host's exit event can be set by every process of the user, a normal one included when the host runs elevated. The event is
/// created with a descriptor that grants the user wait and set, and a Medium no-write-up mandatory label; the descriptor is read back from the real event.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UserSignalledEventTests
{
	private const int KernelObject = 6;               // SE_KERNEL_OBJECT
	private const uint DaclInformation = 0x00000004;  // DACL_SECURITY_INFORMATION
	private const uint LabelInformation = 0x00000010; // LABEL_SECURITY_INFORMATION

	private static string NewName() => @"Local\TessynTest.UserSignalledEvent." + Guid.NewGuid().ToString("N");

	private static string CurrentUserSid() => WindowsIdentity.GetCurrent().User!.Value;

	[Fact]
	public void TheDescriptor_GrantsTheUserWaitAndSet_AndLabelsTheEventMedium()
	{
		var sddl = new UserSignalledEvent().SecurityDescriptorFor("S-1-5-21-1-2-3-1001");

		Assert.Contains("(A;;0x00100002;;;S-1-5-21-1-2-3-1001)", sddl);
		Assert.Contains("(A;;GA;;;SY)", sddl);
		Assert.EndsWith("S:(ML;;NW;;;ME)", sddl);
	}

	[Fact]
	public void TheRealEvent_CarriesTheUserAceAndTheMediumLabel()
	{
		using var handle = new UserSignalledEvent().Create(NewName());

		var sddl = ReadDescriptor(handle);

		Assert.Contains($"(A;;0x100002;;;{CurrentUserSid()})", sddl, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("(ML;;NW;;;ME)", sddl);
	}

	[Fact]
	public void AClientThatOpensTheEventByName_CanSetIt_AndTheWaitWakes()
	{
		var name = NewName();
		using var handle = new UserSignalledEvent().Create(name);

		using (var client = EventWaitHandle.OpenExisting(name))
			Assert.True(client.Set());

		Assert.True(handle.WaitOne(TimeSpan.FromSeconds(5)));
		Assert.False(handle.WaitOne(0));   // auto-reset: one signal, one wake
	}

	[Fact]
	public void TheHostExitListener_UsesIt_SoASignalByNameRunsTheRequest()
	{
		var name = NewName();
		using var listener = new DaemonHostExitListener(name);
		using var requested = new ManualResetEventSlim();
		listener.Start(requested.Set);

		Assert.True(DaemonHostExitListener.IsListening(name));
		Assert.True(DaemonHostExitListener.Signal(name));
		Assert.True(requested.Wait(TimeSpan.FromSeconds(5)));
	}

	private static string ReadDescriptor(WaitHandle handle)
	{
		var error = GetSecurityInfo(handle.SafeWaitHandle.DangerousGetHandle(), KernelObject, DaclInformation | LabelInformation,
			out _, out _, out _, out _, out var descriptor);
		Assert.Equal(0, error);
		try
		{
			Assert.True(ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor, 1, DaclInformation | LabelInformation, out var text, out _));
			try
			{
				return Marshal.PtrToStringUni(text)!;
			}
			finally
			{
				LocalFree(text);
			}
		}
		finally
		{
			LocalFree(descriptor);
		}
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern int GetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr securityDescriptor);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(IntPtr securityDescriptor, uint revision, uint securityInfo, out IntPtr text, out uint length);

	[DllImport("kernel32.dll")]
	private static extern IntPtr LocalFree(IntPtr memory);
}
