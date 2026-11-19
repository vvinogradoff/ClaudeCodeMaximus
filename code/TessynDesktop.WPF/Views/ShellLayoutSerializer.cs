using System.IO;
using System.Windows;
using System.Xml.Linq;
using AvalonDock;
using AvalonDock.Core;

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
	public string Serialize(DockingManager manager, IReadOnlySet<string> keepIds)
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
		return document.ToString(SaveOptions.DisableFormatting);
	}

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
