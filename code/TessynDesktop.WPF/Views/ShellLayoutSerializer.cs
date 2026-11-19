using System.IO;
using System.Windows;
using System.Xml.Linq;
using AvalonDock;
using AvalonDock.Core;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Turns the docking manager's layout into text and back (FR.6.18) with AvalonDock's <see cref="DockXmlLayoutSerializer"/>. Saving keeps only the shell panels: every other panel (a
/// preview panel, FR.29.21 keeps its own places) is cut out of the text, and a pane, panel or floating window it leaves empty with it. Restoring gives each panel its content by
/// id and skips a panel whose id is unknown.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutSerializer
{
	/// <summary>The layout of <paramref name="manager"/> as text, with only the panels whose id is in <paramref name="keepIds"/>.</summary>
	/// <param name="floating">The places of the floating panels (pixels, monitor, scale; FR.29.16): written over the library's rectangle and as extra attributes; null writes the library's own.</param>
	public string Serialize(DockingManager manager, IReadOnlySet<string> keepIds, IReadOnlyDictionary<string, ShellFloatingEntry>? floating = null)
	{
		var writer = new StringWriter();
		new DockXmlLayoutSerializer(manager).Serialize(writer);
		var document = XDocument.Parse(writer.ToString());
		foreach (var panel in document.Descendants().Where(e => Constants.ShellLayout.PanelElementNames.Contains(e.Name.LocalName)).ToList())
		{
			var id = (string?)panel.Attribute("ContentId");
			if (id == null || !keepIds.Contains(id))
				panel.Remove();
		}

		PruneEmptyContainers(document);
		if (floating != null)
			WriteFloating(document, floating);

		return document.ToString(SaveOptions.DisableFormatting);
	}

	/// <summary>The floating places stored in <paramref name="xml"/> by <see cref="Serialize"/>, by panel id; a panel without the attributes (an older layout) is not in it.</summary>
	public IReadOnlyDictionary<string, PreviewPlacementModel> ReadFloating(string xml)
	{
		var places = new Dictionary<string, PreviewPlacementModel>();
		XDocument document;
		try
		{
			document = XDocument.Parse(xml);
		}
		catch (Exception)
		{
			return places;   // the restore itself reports an unreadable layout
		}

		foreach (var panel in document.Descendants().Where(e => Constants.ShellLayout.PanelElementNames.Contains(e.Name.LocalName)))
		{
			var id = (string?)panel.Attribute("ContentId");
			if (id == null || panel.Attribute(Constants.ShellLayout.DpiAttribute) == null)
				continue;

			var place = new PreviewPlacementModel
			{
				Left = IntOf(panel, Constants.ShellLayout.PixelLeftAttribute),
				Top = IntOf(panel, Constants.ShellLayout.PixelTopAttribute),
				Width = IntOf(panel, Constants.ShellLayout.PixelWidthAttribute),
				Height = IntOf(panel, Constants.ShellLayout.PixelHeightAttribute),
				MonitorDeviceName = (string?)panel.Attribute(Constants.ShellLayout.MonitorAttribute),
				MonitorLeft = IntOf(panel, Constants.ShellLayout.MonitorLeftAttribute),
				MonitorTop = IntOf(panel, Constants.ShellLayout.MonitorTopAttribute),
				Dpi = IntOf(panel, Constants.ShellLayout.DpiAttribute),
			};
			if (place.Width > 0 && place.Height > 0)
				places[id] = place;
		}

		return places;
	}

	private void WriteFloating(XDocument document, IReadOnlyDictionary<string, ShellFloatingEntry> floating)
	{
		foreach (var panel in document.Descendants().Where(e => Constants.ShellLayout.PanelElementNames.Contains(e.Name.LocalName)))
		{
			var id = (string?)panel.Attribute("ContentId");
			if (id == null || !floating.TryGetValue(id, out var entry))
				continue;

			panel.SetAttributeValue("FloatingLeft", entry.LibraryUnits.Left);
			panel.SetAttributeValue("FloatingTop", entry.LibraryUnits.Top);
			panel.SetAttributeValue("FloatingWidth", entry.LibraryUnits.Width);
			panel.SetAttributeValue("FloatingHeight", entry.LibraryUnits.Height);
			panel.SetAttributeValue(Constants.ShellLayout.PixelLeftAttribute, entry.Pixels.Left);
			panel.SetAttributeValue(Constants.ShellLayout.PixelTopAttribute, entry.Pixels.Top);
			panel.SetAttributeValue(Constants.ShellLayout.PixelWidthAttribute, entry.Pixels.Width);
			panel.SetAttributeValue(Constants.ShellLayout.PixelHeightAttribute, entry.Pixels.Height);
			panel.SetAttributeValue(Constants.ShellLayout.MonitorAttribute, entry.Pixels.MonitorDeviceName);
			panel.SetAttributeValue(Constants.ShellLayout.MonitorLeftAttribute, entry.Pixels.MonitorLeft);
			panel.SetAttributeValue(Constants.ShellLayout.MonitorTopAttribute, entry.Pixels.MonitorTop);
			panel.SetAttributeValue(Constants.ShellLayout.DpiAttribute, entry.Pixels.Dpi);
		}
	}

	private static int IntOf(XElement element, string attribute) => int.TryParse((string?)element.Attribute(attribute), out var value) ? value : 0;

	/// <summary>
	/// Replaces the layout of <paramref name="manager"/> with <paramref name="xml"/>. <paramref name="contentOf"/> gives a panel's content by id (null = unknown id, the panel is
	/// skipped). Throws when the text cannot be read; the manager keeps its layout then.
	/// </summary>
	public void Deserialize(DockingManager manager, string xml, Func<string, FrameworkElement?> contentOf)
	{
		var serializer = new DockXmlLayoutSerializer(manager);
		serializer.LayoutSerializationCallback += (_, e) =>
		{
			var content = string.IsNullOrEmpty(e.Model.ContentId) ? null : contentOf(e.Model.ContentId);
			if (content == null)
			{
				e.Cancel = true;
				return;
			}

			e.Content = content;
		};
		serializer.Deserialize(new StringReader(xml));
	}

	private void PruneEmptyContainers(XDocument document)
	{
		bool removed;
		do
		{
			removed = false;
			foreach (var container in document.Descendants().Where(e => Constants.ShellLayout.PrunableContainerNames.Contains(e.Name.LocalName) && !e.HasElements).ToList())
			{
				container.Remove();
				removed = true;
			}
		}
		while (removed);
	}
}
