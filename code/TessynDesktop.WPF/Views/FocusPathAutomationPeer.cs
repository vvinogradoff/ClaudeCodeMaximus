using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// A path marked in a Focus card as a UI Automation element of its own (FR.23.30): a link-like child of the card's text that Invoke activates, so a keyboard or
/// accessibility client opens it as Ctrl+click does.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FocusPathAutomationPeer : AutomationPeer, IInvokeProvider
{
	private readonly FocusCardText _owner;
	private readonly FocusPathSpan _span;

	public FocusPathAutomationPeer(FocusCardText owner, FocusPathSpan span)
	{
		_owner = owner;
		_span = span;
	}

	public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : null;

	public void Invoke() => _owner.ActivatePath(_span.Path);

	protected override string GetNameCore() => _span.Path;

	protected override string GetClassNameCore() => nameof(FocusPathSpan);

	protected override string GetAutomationIdCore() => "FocusPath:" + _span.Path;

	protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;

	protected override List<AutomationPeer>? GetChildrenCore() => null;

	protected override Rect GetBoundingRectangleCore()
	{
		var local = _owner.RectOf(_span);
		if (local.IsEmpty)
			return Rect.Empty;

		var topLeft = _owner.PointToScreen(local.TopLeft);
		var bottomRight = _owner.PointToScreen(local.BottomRight);
		return new Rect(topLeft, bottomRight);
	}

	protected override Point GetClickablePointCore()
	{
		var bounds = GetBoundingRectangleCore();
		return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
	}

	protected override bool IsEnabledCore() => true;

	protected override bool IsOffscreenCore() => false;

	protected override bool IsKeyboardFocusableCore() => false;

	protected override bool HasKeyboardFocusCore() => false;

	protected override bool IsContentElementCore() => true;

	protected override bool IsControlElementCore() => true;

	protected override bool IsPasswordCore() => false;

	protected override bool IsRequiredForFormCore() => false;

	protected override string GetItemStatusCore() => string.Empty;

	protected override string GetItemTypeCore() => string.Empty;

	protected override string GetHelpTextCore() => "Ctrl+click opens the file";

	protected override string GetAcceleratorKeyCore() => string.Empty;

	protected override string GetAccessKeyCore() => string.Empty;

	protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

	protected override AutomationPeer? GetLabeledByCore() => null;

	protected override void SetFocusCore()
	{
	}
}
