using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace TessynDesktop.WPF.Views;

/// <summary>Exposes the tag area's mini/full mode through the UI Automation ExpandCollapse pattern (E2E tests and keyboard or screen-reader users).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagAreaAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider
{
	private readonly SessionTagArea _area;
	private ExpandCollapseState _lastState;

	public SessionTagAreaAutomationPeer(SessionTagArea owner) : base(owner)
	{
		_area = owner;
		_lastState = ExpandCollapseState;
	}

	public ExpandCollapseState ExpandCollapseState =>
		!_area.HasTags ? System.Windows.Automation.ExpandCollapseState.LeafNode
		: _area.IsExpanded ? System.Windows.Automation.ExpandCollapseState.Expanded
		: System.Windows.Automation.ExpandCollapseState.Collapsed;

	public void Expand() => _area.Expand();

	public void Collapse() => _area.Collapse();

	public override object? GetPattern(PatternInterface patternInterface) =>
		patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

	protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

	protected override string GetClassNameCore() => nameof(SessionTagArea);

	protected override string GetNameCore() => Constants.TagArea.AutomationName;

	protected override string GetAutomationIdCore() => Constants.TagArea.AutomationId;

	protected override bool IsControlElementCore() => _area.HasTags;

	protected override bool IsContentElementCore() => _area.HasTags;

	internal void RaiseStateChanged()
	{
		var state = ExpandCollapseState;
		if (state == _lastState)
			return;

		RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, _lastState, state);
		_lastState = state;
	}
}
