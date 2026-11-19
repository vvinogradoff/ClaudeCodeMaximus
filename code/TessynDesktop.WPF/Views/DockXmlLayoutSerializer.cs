using System.IO;
using System.Xml.Serialization;
using AvalonDock.Core;
using AvalonDock.Core.Serialization.Dto;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// AvalonDock 5 ships the layout serializer as a format-independent base (<see cref="LayoutSerializerBase"/>) and no concrete one; this writes and reads its layout DTOs as XML
/// (the DTOs carry the <c>XmlSerializer</c> attributes). The text is what is stored in the <c>shellLayout</c> section (FR.6.18).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockXmlLayoutSerializer : LayoutSerializerBase
{
	private static readonly XmlSerializer Xml = new(typeof(LayoutRootDto));

	public DockXmlLayoutSerializer(IDockingManager manager) : base(manager)
	{
	}

	protected override void SerializeCore(Stream stream, LayoutRootDto dto) => Xml.Serialize(stream, dto);

	protected override LayoutRootDto DeserializeCore(Stream stream) => (LayoutRootDto)(Xml.Deserialize(stream) ?? throw new InvalidDataException("The layout text is empty."));
}
