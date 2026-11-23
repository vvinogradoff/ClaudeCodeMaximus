namespace TessynDesktop.WPF;

/// <summary>Texts and wire values of the questionnaire on a Focus card (FR.23.35, FR.23.36).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Labels and values of the questionnaire controls.</summary>
	public static class Questionnaire
	{
		/// <summary>The label of the reply box of a questionnaire card (FR.23.35).</summary>
		public const string NoteLabel = "Note (optional)";

		/// <summary>The marker on the recommended button, or under the text box while its text equals the recommendation.</summary>
		public const string RecommendedMarker = "recommended";

		/// <summary>The link that restores a question's recommendation.</summary>
		public const string Reset = "Reset";

		public const string YesLabel = "Yes";

		public const string NoLabel = "No";

		/// <summary>The wire values of a yes/no answer (lower case, FR.23.36).</summary>
		public const string YesValue = "yes";

		public const string NoValue = "no";

		/// <summary>The letters shown before the choices of a Single or Multi question, in order (FR.23.33).</summary>
		public const string ChoiceLetters = "ABCD";

		/// <summary>The label of a lettered choice button: letter, two spaces, choice.</summary>
		public const string ChoiceFormat = "{0}  {1}";

		/// <summary>The label of a question: <c>Q1</c>.</summary>
		public const string NumberFormat = "Q{0}";

		/// <summary>The automation name of a choice button: question number, then its label.</summary>
		public const string ChoiceAutomationFormat = "Q{0} {1}";

		/// <summary>The automation name of a question's text box and of its Reset link.</summary>
		public const string TextAutomationFormat = "Q{0} answer";

		public const string ResetAutomationFormat = "Q{0} Reset";
	}
}
