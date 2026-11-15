using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Gives XAML-created controls (the slot hosts, the command menu) their shell services: they are instantiated by the XAML loader, so
/// they cannot take constructor arguments. Returns null outside the running app (design time, view tests), where controls then stay empty.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellServiceProvider
{
	public T? Get<T>() where T : class => (Application.Current as App)?.Services.GetService<T>();
}
