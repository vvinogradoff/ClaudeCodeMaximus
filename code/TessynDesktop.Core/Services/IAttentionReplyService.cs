using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The web client's "Send answers": exactly what <c>FocusViewModel.SendCards</c> does on the desktop, resolved on the
/// daemon (titles come from the ledger, so a client cannot misstate them).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAttentionReplyService
{
	/// <summary>
	/// Marks the open items among <paramref name="answers"/> answered, then sends the answers prompt as a web turn with
	/// <paramref name="options"/>. Unknown, closed, repeated and blank-answer entries are skipped; with nothing left no turn starts.
	/// </summary>
	AttentionReplyResult Reply(SessionNodeModel node, IReadOnlyList<AttentionAnswerParams> answers, TurnOptionsModel? options);
}
