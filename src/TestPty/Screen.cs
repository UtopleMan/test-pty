using System.Globalization;
using System.Text;

namespace TestPty;

/// <summary>
/// Enough of a terminal to assert on. A grid of cells with a cursor, fed decoded output through an
/// incremental parser so a chunk may split a sequence anywhere. Every sequence it acts on is one a
/// consumer needs; everything else is consumed and counted rather than guessed at, and shows up in
/// <see cref="UnhandledSequences"/>.
/// </summary>
public sealed class Screen
{
    private const int TabStop = 8;
    private const int RememberedUnhandledSequences = 16;

    private readonly List<string> unhandledSequences = [];
    private readonly StringBuilder sequenceBody = new();

    private ScreenCell[] cells;
    private ScreenCell[]? savedCells;
    private (int Row, int Column) savedCursor;
    private CellAttributes attributes = CellAttributes.Default;
    private ParserState state = ParserState.Ground;
    private int scrollTop;
    private int scrollBottom;
    private bool pendingWrap;

    public Screen(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        Columns = columns;
        Rows = rows;
        cells = new ScreenCell[columns * rows];
        scrollBottom = rows - 1;
        Clear();
    }

    public int Columns { get; }

    public int Rows { get; }

    public int CursorRow { get; private set; }

    public int CursorColumn { get; private set; }

    /// <summary>Whether the program has asked for the cursor to be shown.</summary>
    public bool IsCursorVisible { get; private set; } = true;

    /// <summary>Whether the program is drawing on the alternate screen, as a full-screen app does.</summary>
    public bool IsAlternateScreenActive => savedCells is not null;

    /// <summary>Whether printing past the right margin moves to the next line.</summary>
    public bool IsAutoWrapEnabled { get; private set; } = true;

    /// <summary>How many sequences the parser consumed without acting on them.</summary>
    public int UnhandledSequenceCount { get; private set; }

    /// <summary>
    /// The most recent sequences the parser did not act on, so a consumer can find out what it needs
    /// that is missing.
    /// </summary>
    public IReadOnlyList<string> UnhandledSequences => unhandledSequences;

    public ScreenCell Cell(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);

        return cells[(row * Columns) + column];
    }

    /// <summary>One row, with trailing blanks removed.</summary>
    public string Row(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);

        var builder = new StringBuilder(Columns);

        for (int column = 0; column < Columns; column++)
        {
            builder.Append(cells[(row * Columns) + column].Character);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>The whole grid, for a failure message.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();

        for (int row = 0; row < Rows; row++)
        {
            builder.Append(Row(row));

            if (row < Rows - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    public void Clear()
    {
        Array.Fill(cells, ScreenCell.Blank);
        CursorRow = 0;
        CursorColumn = 0;
        pendingWrap = false;
    }

    /// <summary>Feeds decoded output through the parser, continuing wherever the last chunk stopped.</summary>
    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (char character in text)
        {
            Consume(character);
        }
    }

    private void Consume(char character)
    {
        switch (state)
        {
            case ParserState.Ground:
                ConsumeInGround(character);
                break;

            case ParserState.Escape:
                ConsumeInEscape(character);
                break;

            case ParserState.ControlSequence:
                ConsumeInControlSequence(character);
                break;

            case ParserState.StringSequence:
                ConsumeInStringSequence(character);
                break;

            case ParserState.CharacterSet:
                RecordUnhandled($"ESC ( {character}");
                state = ParserState.Ground;
                break;

            default:
                throw new InvalidOperationException($"Unknown parser state {state}.");
        }
    }

    private void ConsumeInGround(char character)
    {
        switch (character)
        {
            case Ansi.Escape:
                sequenceBody.Clear();
                state = ParserState.Escape;
                break;

            case '\r':
                CursorColumn = 0;
                pendingWrap = false;
                break;

            case '\n':
                LineFeed();
                break;

            case '\b':
                CursorColumn = Math.Max(0, CursorColumn - 1);
                pendingWrap = false;
                break;

            case '\t':
                CursorColumn = Math.Min(Columns - 1, ((CursorColumn / TabStop) + 1) * TabStop);
                pendingWrap = false;
                break;

            default:
                if (!char.IsControl(character))
                {
                    Print(character);
                }

                break;
        }
    }

    private void ConsumeInEscape(char character)
    {
        switch (character)
        {
            case '[':
                state = ParserState.ControlSequence;
                break;

            case ']' or 'P' or 'X' or '^' or '_':
                state = ParserState.StringSequence;
                break;

            case '(' or ')' or '*' or '+':
                state = ParserState.CharacterSet;
                break;

            case 'D':
                LineFeed();
                state = ParserState.Ground;
                break;

            case 'M':
                ReverseLineFeed();
                state = ParserState.Ground;
                break;

            case 'E':
                CursorColumn = 0;
                pendingWrap = false;
                LineFeed();
                state = ParserState.Ground;
                break;

            default:
                RecordUnhandled($"ESC {character}");
                state = ParserState.Ground;
                break;
        }
    }

    private void ConsumeInControlSequence(char character)
    {
        if (Ansi.IsFinalByte(character))
        {
            DispatchControlSequence(character);
            sequenceBody.Clear();
            state = ParserState.Ground;
            return;
        }

        sequenceBody.Append(character);
    }

    private void ConsumeInStringSequence(char character)
    {
        bool isTerminated = character is Ansi.Bell
            || (character == '\\' && sequenceBody.Length > 0 && sequenceBody[^1] == Ansi.Escape);

        if (isTerminated)
        {
            RecordUnhandled($"OSC {sequenceBody}");
            sequenceBody.Clear();
            state = ParserState.Ground;
            return;
        }

        sequenceBody.Append(character);
    }

    private void DispatchControlSequence(char final)
    {
        string body = sequenceBody.ToString();

        if (body.StartsWith('?'))
        {
            DispatchPrivateMode(body[1..], final);
            return;
        }

        switch (final)
        {
            case 'A':
                MoveCursor(-Parameter(body, 0, 1), 0);
                break;

            case 'B':
                MoveCursor(Parameter(body, 0, 1), 0);
                break;

            case 'C':
                MoveCursor(0, Parameter(body, 0, 1));
                break;

            case 'D':
                MoveCursor(0, -Parameter(body, 0, 1));
                break;

            case 'E':
                MoveCursor(Parameter(body, 0, 1), 0);
                CursorColumn = 0;
                break;

            case 'F':
                MoveCursor(-Parameter(body, 0, 1), 0);
                CursorColumn = 0;
                break;

            case 'G':
                PlaceCursor(CursorRow, Parameter(body, 0, 1) - 1);
                break;

            case 'd':
                PlaceCursor(Parameter(body, 0, 1) - 1, CursorColumn);
                break;

            case 'H' or 'f':
                PlaceCursor(Parameter(body, 0, 1) - 1, Parameter(body, 1, 1) - 1);
                break;

            case 'J':
                EraseInDisplay(Parameter(body, 0, 0));
                break;

            case 'K':
                EraseInLine(Parameter(body, 0, 0));
                break;

            case 'm':
                ApplyGraphicRendition(body);
                break;

            case 'r':
                SetScrollRegion(Parameter(body, 0, 1) - 1, Parameter(body, 1, Rows) - 1);
                break;

            default:
                RecordUnhandled($"CSI {body}{final}");
                break;
        }
    }

    private void DispatchPrivateMode(string body, char final)
    {
        if (final is not ('h' or 'l'))
        {
            RecordUnhandled($"CSI ?{body}{final}");
            return;
        }

        bool isEnabled = final == 'h';

        switch (Parameter(body, 0, 0))
        {
            case 7:
                IsAutoWrapEnabled = isEnabled;
                break;

            case 25:
                IsCursorVisible = isEnabled;
                break;

            case 1049:
                SetAlternateScreen(isEnabled);
                break;

            default:
                RecordUnhandled($"CSI ?{body}{final}");
                break;
        }
    }

    private void SetAlternateScreen(bool isEnabled)
    {
        if (isEnabled)
        {
            if (savedCells is not null)
            {
                return;
            }

            savedCells = cells;
            savedCursor = (CursorRow, CursorColumn);
            cells = new ScreenCell[Columns * Rows];
            Clear();
            return;
        }

        if (savedCells is null)
        {
            return;
        }

        cells = savedCells;
        savedCells = null;
        (CursorRow, CursorColumn) = savedCursor;
        pendingWrap = false;
    }

    private void ApplyGraphicRendition(string body)
    {
        if (body.Length == 0)
        {
            attributes = CellAttributes.Default;
            return;
        }

        string[] parts = body.Split(';');

        for (int index = 0; index < parts.Length; index++)
        {
            int code = ParseNumber(parts[index], 0);

            switch (code)
            {
                case 0:
                    attributes = CellAttributes.Default;
                    break;

                case 1:
                    attributes = attributes with { IsBold = true };
                    break;

                case 7:
                    attributes = attributes with { IsReverse = true };
                    break;

                case 22:
                    attributes = attributes with { IsBold = false };
                    break;

                case 27:
                    attributes = attributes with { IsReverse = false };
                    break;

                case 39:
                    attributes = attributes with { Foreground = TerminalColor.Default };
                    break;

                case 49:
                    attributes = attributes with { Background = TerminalColor.Default };
                    break;

                case >= 30 and <= 37:
                    attributes = attributes with { Foreground = (TerminalColor)(code - 30 + 1) };
                    break;

                case >= 40 and <= 47:
                    attributes = attributes with { Background = (TerminalColor)(code - 40 + 1) };
                    break;

                case >= 90 and <= 97:
                    attributes = attributes with { Foreground = (TerminalColor)(code - 90 + 9) };
                    break;

                case >= 100 and <= 107:
                    attributes = attributes with { Background = (TerminalColor)(code - 100 + 9) };
                    break;

                case 38 or 48:
                    index += ExtendedColourLength(parts, index);
                    RecordUnhandled($"SGR {code} (extended colour)");
                    break;

                default:
                    RecordUnhandled($"SGR {parts[index]}");
                    break;
            }
        }
    }

    /// <summary>
    /// How many parameters after <c>38</c> or <c>48</c> belong to it. Skipping them matters: read as
    /// ordinary codes, <c>38;5;196</c> would set colours nobody asked for.
    /// </summary>
    private static int ExtendedColourLength(string[] parts, int index)
    {
        int selector = index + 1 < parts.Length ? ParseNumber(parts[index + 1], -1) : -1;

        return selector switch
        {
            5 => 2,
            2 => 4,
            _ => 0,
        };
    }

    private void SetScrollRegion(int top, int bottom)
    {
        if (top < 0 || bottom >= Rows || top >= bottom)
        {
            scrollTop = 0;
            scrollBottom = Rows - 1;
        }
        else
        {
            scrollTop = top;
            scrollBottom = bottom;
        }

        PlaceCursor(scrollTop, 0);
    }

    private void EraseInDisplay(int mode)
    {
        int cursor = (CursorRow * Columns) + CursorColumn;

        switch (mode)
        {
            case 0:
                Array.Fill(cells, Blank(), cursor, cells.Length - cursor);
                break;

            case 1:
                Array.Fill(cells, Blank(), 0, cursor + 1);
                break;

            case 2 or 3:
                Array.Fill(cells, Blank());
                break;

            default:
                RecordUnhandled($"CSI {mode}J");
                break;
        }
    }

    private void EraseInLine(int mode)
    {
        int start = CursorRow * Columns;

        switch (mode)
        {
            case 0:
                Array.Fill(cells, Blank(), start + CursorColumn, Columns - CursorColumn);
                break;

            case 1:
                Array.Fill(cells, Blank(), start, CursorColumn + 1);
                break;

            case 2:
                Array.Fill(cells, Blank(), start, Columns);
                break;

            default:
                RecordUnhandled($"CSI {mode}K");
                break;
        }
    }

    /// <summary>Erasing leaves the current background behind, as a terminal does.</summary>
    private ScreenCell Blank() => new(' ', CellAttributes.Default with { Background = attributes.Background });

    private void MoveCursor(int rowDelta, int columnDelta) =>
        PlaceCursor(CursorRow + rowDelta, CursorColumn + columnDelta);

    private void PlaceCursor(int row, int column)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        pendingWrap = false;
    }

    private void Print(char character)
    {
        if (pendingWrap)
        {
            if (IsAutoWrapEnabled)
            {
                CursorColumn = 0;
                LineFeed();
            }

            pendingWrap = false;
        }

        cells[(CursorRow * Columns) + CursorColumn] = new ScreenCell(character, attributes);

        if (CursorColumn == Columns - 1)
        {
            pendingWrap = true;
        }
        else
        {
            CursorColumn++;
        }
    }

    private void LineFeed()
    {
        pendingWrap = false;

        if (CursorRow == scrollBottom)
        {
            ScrollUp();
            return;
        }

        CursorRow = Math.Min(Rows - 1, CursorRow + 1);
    }

    private void ReverseLineFeed()
    {
        pendingWrap = false;

        if (CursorRow == scrollTop)
        {
            ScrollDown();
            return;
        }

        CursorRow = Math.Max(0, CursorRow - 1);
    }

    private void ScrollUp()
    {
        int top = scrollTop * Columns;
        int moved = (scrollBottom - scrollTop) * Columns;

        Array.Copy(cells, top + Columns, cells, top, moved);
        Array.Fill(cells, Blank(), scrollBottom * Columns, Columns);
    }

    private void ScrollDown()
    {
        int top = scrollTop * Columns;
        int moved = (scrollBottom - scrollTop) * Columns;

        Array.Copy(cells, top, cells, top + Columns, moved);
        Array.Fill(cells, Blank(), top, Columns);
    }

    private static int Parameter(string body, int index, int fallback)
    {
        string[] parts = body.Split(';');

        if (index >= parts.Length)
        {
            return fallback;
        }

        return ParseNumber(parts[index], fallback);
    }

    private static int ParseNumber(string text, int fallback) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private void RecordUnhandled(string description)
    {
        UnhandledSequenceCount++;
        unhandledSequences.Add(description);

        if (unhandledSequences.Count > RememberedUnhandledSequences)
        {
            unhandledSequences.RemoveAt(0);
        }
    }

    private enum ParserState
    {
        Ground,
        Escape,
        ControlSequence,
        StringSequence,
        CharacterSet,
    }
}
