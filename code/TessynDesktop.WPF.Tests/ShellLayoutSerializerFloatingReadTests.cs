using System.Globalization;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.18, FR.29.16: reading the floating places back from the saved shell layout: every number with the invariant culture, and a scale that cannot be read drops the place (never pixels read as device-independent units).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutSerializerFloatingReadTests
{
	private static string Layout(string dpi, string left = "-3000") =>
		$"<LayoutRoot><LayoutAnchorable ContentId=\"focus\" TessynPixelLeft=\"{left}\" TessynPixelTop=\"120\" TessynPixelWidth=\"900\" TessynPixelHeight=\"700\" TessynMonitor=\"DISPLAY2\" TessynMonitorLeft=\"-1920\" TessynMonitorTop=\"0\" TessynDpi=\"{dpi}\" /></LayoutRoot>";

	[Fact]
	public void AWellFormedPlace_IsRead()
	{
		var place = new ShellLayoutSerializer().ReadFloating(Layout("144"))["focus"];

		Assert.Equal((-3000, 120, 900, 700, 144, -1920), (place.Left, place.Top, place.Width, place.Height, place.Dpi, place.MonitorLeft));
	}

	[Fact]
	public void TheNumbers_AreReadWithTheInvariantCulture_WhateverTheCurrentOne()
	{
		var before = CultureInfo.CurrentCulture;
		var odd = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
		odd.NumberFormat.NegativeSign = "~";   // a culture that does not write a minus as the file does
		try
		{
			CultureInfo.CurrentCulture = odd;

			var place = new ShellLayoutSerializer().ReadFloating(Layout("144"))["focus"];

			Assert.Equal(-3000, place.Left);
			Assert.Equal(-1920, place.MonitorLeft);
		}
		finally
		{
			CultureInfo.CurrentCulture = before;
		}
	}

	[Theory]
	[InlineData("abc")]
	[InlineData("")]
	[InlineData("0")]
	[InlineData("-96")]
	[InlineData("14.4")]
	public void AScaleThatCannotBeRead_DropsThePlace_ItIsNeverReadAsDeviceIndependentUnits(string dpi)
	{
		var places = new ShellLayoutSerializer().ReadFloating(Layout(dpi));

		Assert.Empty(places);
	}

	[Fact]
	public void APanelWithoutTheAttributes_IsNotInIt()
	{
		Assert.Empty(new ShellLayoutSerializer().ReadFloating("<LayoutRoot><LayoutAnchorable ContentId=\"focus\" FloatingLeft=\"10\" /></LayoutRoot>"));
	}
}
