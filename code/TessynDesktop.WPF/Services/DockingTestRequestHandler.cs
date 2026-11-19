using System.Text.Json;
using System.Text.Json.Serialization;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Turns one request line of the docking test hook into one reply line (FR.6.22): parses the JSON, calls the matching <see cref="IDockingTestOperations"/> member and reports the
/// state of every panel afterwards. Never throws: an unreadable line, an unknown operation or a failing call becomes an error reply. Runs on the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingTestRequestHandler
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	private readonly IDockingTestOperations _operations;

	public DockingTestRequestHandler(IDockingTestOperations operations)
	{
		_operations = operations;
	}

	/// <summary>One request line in, one reply line (compact JSON, no line break) out.</summary>
	public string Handle(string line) => JsonSerializer.Serialize(Reply(line), Json);

	private DockingTestReply Reply(string line)
	{
		DockingTestRequest? request;
		try
		{
			request = JsonSerializer.Deserialize<DockingTestRequest>(line, Json);
		}
		catch (JsonException ex)
		{
			return Error(string.Format(Constants.DockingTestHook.UnreadableRequestError, ex.Message));
		}

		if (request?.Op is not { Length: > 0 } op)
			return Error(string.Format(Constants.DockingTestHook.UnreadableRequestError, line));

		try
		{
			var result = Run(op, request);
			return new DockingTestReply { Ok = result.Ok, Error = result.Error, Panels = _operations.State() };
		}
		catch (Exception ex)
		{
			return Error(string.Format(Constants.DockingTestHook.OperationFailedError, op, ex.Message));
		}
	}

	private DockingTestResult Run(string op, DockingTestRequest request)
	{
		if (op == Constants.DockingTestHook.OpState)
			return DockingTestResult.Success;

		if (op == Constants.DockingTestHook.OpReset)
			return _operations.Reset();

		if (op is not (Constants.DockingTestHook.OpDock or Constants.DockingTestHook.OpTab or Constants.DockingTestHook.OpFloat or Constants.DockingTestHook.OpAutoHide
			or Constants.DockingTestHook.OpSlideOut or Constants.DockingTestHook.OpClose or Constants.DockingTestHook.OpShow or Constants.DockingTestHook.OpBringForward))
			return new DockingTestResult(false, string.Format(Constants.DockingTestHook.UnknownOperationError, op));

		if (request.Panel is not { Length: > 0 } panel)
			return Missing(op, nameof(DockingTestRequest.Panel));

		switch (op)
		{
			case Constants.DockingTestHook.OpDock:
				return request.Arg is { Length: > 0 } side ? _operations.Dock(panel, side) : Missing(op, nameof(DockingTestRequest.Arg));
			case Constants.DockingTestHook.OpTab:
				return request.Arg is { Length: > 0 } with ? _operations.Tab(panel, with) : Missing(op, nameof(DockingTestRequest.Arg));
			case Constants.DockingTestHook.OpFloat:
				return _operations.Float(panel, RectOf(request));
			case Constants.DockingTestHook.OpAutoHide:
				return request.Arg switch
				{
					Constants.DockingTestHook.ArgOn => _operations.AutoHide(panel, true),
					Constants.DockingTestHook.ArgOff => _operations.AutoHide(panel, false),
					null or "" => Missing(op, nameof(DockingTestRequest.Arg)),
					var other => new DockingTestResult(false, string.Format(Constants.DockingTestHook.UnknownModeError, other)),
				};
			case Constants.DockingTestHook.OpSlideOut:
				return _operations.SlideOut(panel);
			case Constants.DockingTestHook.OpClose:
				return _operations.Close(panel);
			case Constants.DockingTestHook.OpShow:
				return _operations.Show(panel);
			default:
				return _operations.BringForward(panel);
		}
	}

	/// <summary>A rectangle only when all four numbers are given.</summary>
	private DockingFloatRect? RectOf(DockingTestRequest request) =>
		request is { X: { } x, Y: { } y, W: { } w, H: { } h } ? new DockingFloatRect(x, y, w, h) : null;

	private DockingTestResult Missing(string op, string name) => new DockingTestResult(false, string.Format(Constants.DockingTestHook.MissingArgumentError, op, name.ToLowerInvariant()));

	private DockingTestReply Error(string message) => new() { Ok = false, Error = message, Panels = SafeState() };

	private IReadOnlyList<DockingPanelState> SafeState()
	{
		try
		{
			return _operations.State();
		}
		catch (Exception)
		{
			return [];
		}
	}
}
