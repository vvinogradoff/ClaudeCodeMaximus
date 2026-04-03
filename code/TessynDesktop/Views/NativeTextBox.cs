using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace TessynDesktop.Views;

/// <summary>
/// TextBox with macOS-native keyboard shortcuts that Avalonia doesn't implement:
/// Cmd+Delete (delete to start of line), Cmd+Left/Right (line start/end),
/// Option+Left/Right (word jump), Option+Delete/Backspace (delete word),
/// Cmd+Backspace (delete to start of line).
/// On non-macOS platforms this behaves identically to a standard TextBox.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class NativeTextBox : TextBox
{
    private static readonly bool _isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    // Use TextBox's style template so the control renders correctly
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_isMacOS && HandleMacShortcut(e))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private bool HandleMacShortcut(KeyEventArgs e)
    {
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var text = Text ?? string.Empty;
        var caret = CaretIndex;

        // For delete operations: if there's a selection, delete it first (standard behavior)
        if (!shift && HasSelection() && (e.Key is Key.Back or Key.Delete) && (meta || alt))
        {
            DeleteSelection();
            return true;
        }

        // Cmd+Left → move to start of line
        if (meta && e.Key == Key.Left && !shift)
        {
            CaretIndex = GetLineStart(text, caret);
            return true;
        }

        // Cmd+Right → move to end of line
        if (meta && e.Key == Key.Right && !shift)
        {
            CaretIndex = GetLineEnd(text, caret);
            return true;
        }

        // Cmd+Shift+Left → select to start of line
        if (meta && shift && e.Key == Key.Left)
        {
            SelectionStart = caret;
            SelectionEnd = GetLineStart(text, caret);
            CaretIndex = SelectionEnd;
            return true;
        }

        // Cmd+Shift+Right → select to end of line
        if (meta && shift && e.Key == Key.Right)
        {
            SelectionStart = caret;
            SelectionEnd = GetLineEnd(text, caret);
            CaretIndex = SelectionEnd;
            return true;
        }

        // Option+Left → move to previous word boundary
        if (alt && e.Key == Key.Left && !shift)
        {
            CaretIndex = GetPreviousWordBoundary(text, caret);
            return true;
        }

        // Option+Right → move to next word boundary
        if (alt && e.Key == Key.Right && !shift)
        {
            CaretIndex = GetNextWordBoundary(text, caret);
            return true;
        }

        // Option+Shift+Left → select to previous word boundary
        if (alt && shift && e.Key == Key.Left)
        {
            SelectionStart = caret;
            SelectionEnd = GetPreviousWordBoundary(text, caret);
            CaretIndex = SelectionEnd;
            return true;
        }

        // Option+Shift+Right → select to next word boundary
        if (alt && shift && e.Key == Key.Right)
        {
            SelectionStart = caret;
            SelectionEnd = GetNextWordBoundary(text, caret);
            CaretIndex = SelectionEnd;
            return true;
        }

        // Cmd+Backspace → delete to start of line
        if (meta && e.Key == Key.Back)
        {
            var lineStart = GetLineStart(text, caret);
            if (lineStart < caret)
            {
                Text = text.Remove(lineStart, caret - lineStart);
                CaretIndex = lineStart;
            }
            return true;
        }

        // Cmd+Delete → delete to end of line
        if (meta && e.Key == Key.Delete)
        {
            var lineEnd = GetLineEnd(text, caret);
            if (caret < lineEnd)
                Text = text.Remove(caret, lineEnd - caret);
            return true;
        }

        // Option+Backspace → delete previous word
        if (alt && e.Key == Key.Back)
        {
            var wordStart = GetPreviousWordBoundary(text, caret);
            if (wordStart < caret)
            {
                Text = text.Remove(wordStart, caret - wordStart);
                CaretIndex = wordStart;
            }
            return true;
        }

        // Option+Delete → delete next word
        if (alt && e.Key == Key.Delete)
        {
            var wordEnd = GetNextWordBoundary(text, caret);
            if (caret < wordEnd)
                Text = text.Remove(caret, wordEnd - caret);
            return true;
        }

        return false;
    }

    private static int GetLineStart(string text, int position)
    {
        if (position <= 0) return 0;
        var idx = text.LastIndexOf('\n', position - 1);
        return idx < 0 ? 0 : idx + 1;
    }

    private static int GetLineEnd(string text, int position)
    {
        var idx = text.IndexOf('\n', position);
        return idx < 0 ? text.Length : idx;
    }

    private static int GetPreviousWordBoundary(string text, int position)
    {
        if (position <= 0) return 0;
        var i = position - 1;
        // Skip whitespace
        while (i > 0 && char.IsWhiteSpace(text[i])) i--;
        // Skip punctuation
        while (i > 0 && IsPunctuation(text[i])) i--;
        // Skip word characters (letters/digits)
        while (i > 0 && char.IsLetterOrDigit(text[i - 1])) i--;
        return i;
    }

    private static int GetNextWordBoundary(string text, int position)
    {
        if (position >= text.Length) return text.Length;
        var i = position;
        // Skip current word characters (letters/digits)
        while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;
        // Skip punctuation
        while (i < text.Length && IsPunctuation(text[i])) i++;
        // Skip whitespace
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    private static bool IsPunctuation(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);

    private bool HasSelection() => SelectionStart != SelectionEnd;

    private void DeleteSelection()
    {
        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        var text = Text ?? string.Empty;
        if (start < end && end <= text.Length)
        {
            Text = text.Remove(start, end - start);
            CaretIndex = start;
        }
    }
}
