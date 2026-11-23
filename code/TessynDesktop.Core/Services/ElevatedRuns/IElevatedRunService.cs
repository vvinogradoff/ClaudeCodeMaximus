using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Owns the elevated run requests (FR.15.16 to FR.15.19): the request records, the status transitions (compare-and-set under one lock), the limits, the expiry,
/// the watching of the run folder, the Focus items and the one outcome turn. A Singleton: it owns the record cache, the lock and the polling timer.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IElevatedRunService
{
	/// <summary>Raised after every status change (tdcp-spec 10.A13 <c>elevated.changed</c>). May fire on any thread.</summary>
	event EventHandler<ElevatedRunChangedEventArgs>? Changed;

	/// <summary>True when the daemon runs elevated: a request is then refused (FR.15.16).</summary>
	bool IsDaemonElevated { get; }

	/// <summary>Reads the run folders (restart reconciliation, FR.15.18), sweeps the expired requests and starts the polling timer.</summary>
	void Start();

	void Stop();

	/// <summary>
	/// <c>request_elevated_run</c>: checks the arguments and the limits, stores the request, pins the Focus item (also in the top-level supervisor's ledger for a
	/// worker) and notifies the user. Throws <see cref="McpToolException"/> with the tool's text when the request is refused; nothing is created then.
	/// </summary>
	Task<ElevatedRunRequestOutcomeModel> RequestAsync(SessionNodeModel caller, ElevatedRunRequestModel request);

	/// <summary><c>cancel_elevated_run</c>: withdraws the caller's own pending request; returns the tool's text. Throws <see cref="McpToolException"/> for an unknown or too short id.</summary>
	string Cancel(SessionNodeModel caller, string requestId);

	/// <summary><c>list_elevated_runs</c>: the caller's own requests, newest first, as the tool's text.</summary>
	string ListText(SessionNodeModel caller, string? status);

	/// <summary>The runs, newest first: the open ones or all, optionally only those requested by <paramref name="nodeId"/> (copies).</summary>
	IReadOnlyList<ElevatedRunModel> List(string? nodeId, bool all, int max);

	/// <summary>A copy of the run, or null.</summary>
	ElevatedRunModel? Find(string requestId);

	/// <summary>The run folder of a request (<c>%AppData%\TessynDesktop\elevated-runs\&lt;id&gt;</c>).</summary>
	string GetRunDir(string requestId);

	/// <summary>The absolute path of the run's <c>output.log</c>.</summary>
	string GetLogPath(string requestId);

	/// <summary>The Short Node Id of a session as <c>list_sessions</c> shows it (tdcp-spec 10.A13: the requester's <c>shortId</c>).</summary>
	string ShortIdOf(string nodeId);

	/// <summary>The last <paramref name="lines"/> lines of the run's <c>output.log</c> (each cut to 500 characters), joined with line feeds; empty when there is none.</summary>
	string GetOutputTail(string requestId, int lines);

	/// <summary><c>elevated.approve</c>: <c>pending</c> to <c>launching</c>, only the first call wins; returns the launch description. Throws <see cref="ElevatedRunException"/>.</summary>
	ElevatedRunLaunchModel Approve(string requestId);

	/// <summary><c>elevated.launched</c>: the client's launch call returned; starts the wait for <c>started.json</c>. Only for a <c>launching</c> request.</summary>
	void ReportLaunched(string requestId);

	/// <summary><c>elevated.launchFailed</c>: <paramref name="declined"/> (the Windows prompt was cancelled) gives <c>declinedAtPrompt</c>, else <c>failedToStart</c> with <paramref name="reason"/>.</summary>
	void ReportLaunchFailed(string requestId, bool declined, string? reason);

	/// <summary><c>elevated.decline</c>: <c>pending</c> to <c>declined</c> with the user's note.</summary>
	void Decline(string requestId, string? note);

	/// <summary><c>elevated.stop</c>: creates the <c>stop</c> file of a <c>running</c> request.</summary>
	void Stop(string requestId);
}
