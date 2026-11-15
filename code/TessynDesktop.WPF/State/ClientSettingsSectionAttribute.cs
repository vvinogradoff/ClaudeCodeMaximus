namespace TessynDesktop.WPF.State;

/// <summary>Names a client settings section and its schema version; put it on the section class.</summary>
/// <remarks>Created by Claude</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ClientSettingsSectionAttribute : Attribute
{
	public ClientSettingsSectionAttribute(string name, int version = 1)
	{
		Name = name;
		Version = version;
	}

	/// <summary>The key in <c>wpf.json</c>; never rename it once shipped.</summary>
	public string Name { get; }

	/// <summary>Raise it when a change is not backward compatible; a section written by a newer version is read as defaults and kept until it is next written.</summary>
	public int Version { get; }
}
