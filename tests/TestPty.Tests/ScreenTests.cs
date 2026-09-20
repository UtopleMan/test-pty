using System.Text;
using Xunit;

namespace TestPty.Tests;

/// <summary>
/// Byte sequences in, expected grid out. No process is involved, so a failure here is the parser's
/// and nothing else's.
/// </summary>
public sealed class ScreenTests
{
    private static readonly TimeSpan RealProgramTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("ab\rX", "Xb")]
    [InlineData("abc\bX", "abX")]
    [InlineData("a\tb", "a       b")]
    [InlineData("a\u0007b", "ab")]
    public void Printable_text_and_control_characters_land_where_they_should(string input, string expectedFirstRow)
    {
        Screen screen = Feed(input, columns: 20, rows: 4);

        Assert.Equal(expectedFirstRow, screen.Row(0));
    }

    [Fact]
    public void A_line_feed_moves_down_without_returning_to_the_left()
    {
        Screen screen = Feed("ab\ncd", columns: 20, rows: 4);

        Assert.Equal("ab", screen.Row(0));
        Assert.Equal("  cd", screen.Row(1));
    }

    [Theory]
    [InlineData("\u001b[3;5H", 2, 4)]
    [InlineData("\u001b[3;5f", 2, 4)]
    [InlineData("\u001b[H", 0, 0)]
    [InlineData("\u001b[4d", 3, 0)]
    [InlineData("\u001b[7G", 0, 6)]
    public void Cursor_addressing_puts_the_cursor_where_it_was_told(string input, int expectedRow, int expectedColumn)
    {
        Screen screen = Feed(input, columns: 20, rows: 8);

        Assert.Equal(expectedRow, screen.CursorRow);
        Assert.Equal(expectedColumn, screen.CursorColumn);
    }

    [Theory]
    [InlineData("\u001b[5;5H\u001b[2A", 2, 4)]
    [InlineData("\u001b[5;5H\u001b[2B", 6, 4)]
    [InlineData("\u001b[5;5H\u001b[2C", 4, 6)]
    [InlineData("\u001b[5;5H\u001b[2D", 4, 2)]
    [InlineData("\u001b[5;5H\u001b[A", 3, 4)]
    public void Relative_cursor_movement_is_relative(string input, int expectedRow, int expectedColumn)
    {
        Screen screen = Feed(input, columns: 20, rows: 8);

        Assert.Equal(expectedRow, screen.CursorRow);
        Assert.Equal(expectedColumn, screen.CursorColumn);
    }

    [Fact]
    public void Cursor_movement_stops_at_the_edges()
    {
        Screen screen = Feed("\u001b[99A\u001b[99D", columns: 20, rows: 8);

        Assert.Equal(0, screen.CursorRow);
        Assert.Equal(0, screen.CursorColumn);
    }

    [Theory]
    [InlineData("\u001b[0K", "ab")]
    [InlineData("\u001b[1K", "   de")]
    [InlineData("\u001b[2K", "")]
    [InlineData("\u001b[K", "ab")]
    public void Erase_in_line_clears_the_part_it_names(string eraseSequence, string expected)
    {
        Screen screen = Feed("abcde\u001b[1;3H" + eraseSequence, columns: 20, rows: 4);

        Assert.Equal(expected, screen.Row(0));
    }

    [Fact]
    public void Erase_in_display_to_the_end_clears_everything_after_the_cursor()
    {
        Screen screen = Feed(
            "\u001b[1;1Hone\u001b[2;1Htwo\u001b[3;1Hthree\u001b[2;2H\u001b[0J",
            columns: 20,
            rows: 4);

        Assert.Equal("one", screen.Row(0));
        Assert.Equal("t", screen.Row(1));
        Assert.Equal("", screen.Row(2));
    }

    [Fact]
    public void Erase_in_display_to_the_start_clears_everything_before_the_cursor()
    {
        Screen screen = Feed(
            "\u001b[1;1Hone\u001b[2;1Htwo\u001b[2;2H\u001b[1J",
            columns: 20,
            rows: 4);

        Assert.Equal("", screen.Row(0));
        Assert.Equal("  o", screen.Row(1));
    }

    [Fact]
    public void Erase_in_display_of_everything_clears_the_grid()
    {
        Screen screen = Feed("one\ntwo\u001b[2J", columns: 20, rows: 4);

        Assert.Equal(string.Empty, screen.ToText().Trim());
    }

    [Fact]
    public void Graphic_rendition_is_tracked_per_cell()
    {
        Screen screen = Feed("\u001b[1;31mA\u001b[0mB\u001b[7;42mC\u001b[0m", columns: 20, rows: 4);

        Assert.True(screen.Cell(0, 0).Attributes.IsBold);
        Assert.Equal(TerminalColor.Red, screen.Cell(0, 0).Attributes.Foreground);

        Assert.Equal(CellAttributes.Default, screen.Cell(0, 1).Attributes);

        Assert.True(screen.Cell(0, 2).Attributes.IsReverse);
        Assert.Equal(TerminalColor.Green, screen.Cell(0, 2).Attributes.Background);
    }

    [Fact]
    public void Bright_colours_are_the_upper_eight()
    {
        Screen screen = Feed("\u001b[94mA\u001b[105mB", columns: 20, rows: 4);

        Assert.Equal(TerminalColor.BrightBlue, screen.Cell(0, 0).Attributes.Foreground);
        Assert.Equal(TerminalColor.BrightMagenta, screen.Cell(0, 1).Attributes.Background);
    }

    [Fact]
    public void An_extended_colour_is_skipped_whole_rather_than_read_as_ordinary_codes()
    {
        Screen screen = Feed("\u001b[38;5;196mA", columns: 20, rows: 4);

        Assert.Equal(TerminalColor.Default, screen.Cell(0, 0).Attributes.Foreground);
        Assert.False(screen.Cell(0, 0).Attributes.IsBold);
        Assert.Contains(screen.UnhandledSequences, sequence => sequence.Contains("38", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_wraps_at_the_right_margin()
    {
        Screen screen = Feed("abcdef", columns: 4, rows: 4);

        Assert.Equal("abcd", screen.Row(0));
        Assert.Equal("ef", screen.Row(1));
    }

    [Fact]
    public void The_last_column_does_not_wrap_until_another_character_arrives()
    {
        Screen screen = Feed("abcd", columns: 4, rows: 4);

        Assert.Equal(0, screen.CursorRow);
        Assert.Equal(3, screen.CursorColumn);
    }

    [Fact]
    public void Auto_wrap_can_be_turned_off()
    {
        Screen screen = Feed("\u001b[?7labcdef", columns: 4, rows: 4);

        Assert.False(screen.IsAutoWrapEnabled);
        Assert.Equal("abcf", screen.Row(0));
        Assert.Equal("", screen.Row(1));
    }

    [Fact]
    public void Output_past_the_bottom_row_scrolls_the_grid()
    {
        Screen screen = Feed("one\r\ntwo\r\nthree\r\nfour", columns: 20, rows: 3);

        Assert.Equal("two", screen.Row(0));
        Assert.Equal("three", screen.Row(1));
        Assert.Equal("four", screen.Row(2));
    }

    [Fact]
    public void A_scroll_region_confines_scrolling_to_itself()
    {
        Screen screen = Feed(
            "\u001b[1;1Htop\u001b[2;1Ha\u001b[3;1Hb\u001b[4;1Hc\u001b[5;1Hbottom\u001b[2;4r\u001b[4;1H\nx",
            columns: 20,
            rows: 5);

        Assert.Equal("top", screen.Row(0));
        Assert.Equal("b", screen.Row(1));
        Assert.Equal("c", screen.Row(2));
        Assert.Equal("x", screen.Row(3));
        Assert.Equal("bottom", screen.Row(4));
    }

    [Fact]
    public void Reverse_index_scrolls_the_other_way_at_the_top()
    {
        Screen screen = Feed("one\ntwo\u001b[1;1H\u001bM", columns: 20, rows: 3);

        Assert.Equal("", screen.Row(0));
        Assert.Equal("one", screen.Row(1));
        Assert.Equal("   two", screen.Row(2));
    }

    [Fact]
    public void Index_moves_down_a_line_without_a_carriage_return()
    {
        Screen screen = Feed("ab\u001bD", columns: 20, rows: 4);

        Assert.Equal(1, screen.CursorRow);
        Assert.Equal(2, screen.CursorColumn);
    }

    [Fact]
    public void The_alternate_screen_hides_what_was_there_and_gives_it_back()
    {
        Screen screen = Feed("original\u001b[?1049h", columns: 20, rows: 4);

        Assert.True(screen.IsAlternateScreenActive);
        Assert.Equal("", screen.Row(0));

        screen.Feed("drawn on top\u001b[?1049l");

        Assert.False(screen.IsAlternateScreenActive);
        Assert.Equal("original", screen.Row(0));
    }

    [Fact]
    public void Cursor_visibility_is_tracked()
    {
        Screen screen = Feed("\u001b[?25l", columns: 20, rows: 4);

        Assert.False(screen.IsCursorVisible);

        screen.Feed("\u001b[?25h");

        Assert.True(screen.IsCursorVisible);
    }

    [Fact]
    public void An_unknown_sequence_is_consumed_and_counted_rather_than_printed()
    {
        Screen screen = Feed("a\u001b[999Zb\u001b]0;a title\u0007c", columns: 20, rows: 4);

        Assert.Equal("abc", screen.Row(0));
        Assert.Equal(2, screen.UnhandledSequenceCount);
        Assert.Contains(screen.UnhandledSequences, sequence => sequence.Contains("999Z", StringComparison.Ordinal));
        Assert.Contains(screen.UnhandledSequences, sequence => sequence.Contains("a title", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sequence_split_across_chunks_is_still_understood()
    {
        var screen = new Screen(20, 4);

        screen.Feed("abc\u001b[");
        screen.Feed("1;");
        screen.Feed("1HX");

        Assert.Equal("Xbc", screen.Row(0));
    }

    [Fact]
    public void ToText_renders_every_row()
    {
        Screen screen = Feed("one\ntwo", columns: 20, rows: 3);

        Assert.Equal("one\n   two\n", screen.ToText());
    }

    [Fact]
    public void Utf8_text_reaches_the_grid_intact()
    {
        var screen = new Screen(20, 2);

        screen.Feed(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("héllo")));

        Assert.Equal("héllo", screen.Row(0));
    }

    [UnixFact]
    public async Task Escape_sequences_from_a_real_program_land_on_the_grid()
    {
        PtyOptions options = new(
            "sh",
            ["-c", @"printf 'abc\rX'; printf '\033[2;3Hhere'; printf '\033[5;1Hdone'"])
        {
            Size = (20, 6),
            DefaultTimeout = RealProgramTimeout,
        };

        using PtySession session = PtySession.Start(options);
        await session.WaitFor("done", RealProgramTimeout, TestContext.Current.CancellationToken);

        Assert.Equal("Xbc", session.Screen.Row(0));
        Assert.Equal("  here", session.Screen.Row(1));
        Assert.Equal("done", session.Screen.Row(4));

        string capture = Encoding.UTF8.GetString(session.RawCapture);
        Assert.Contains("[2;3H", capture, StringComparison.Ordinal);
    }

    [UnixFact]
    public async Task Sequences_the_terminfo_database_produces_are_understood()
    {
        PtyOptions options = new(
            "sh",
            ["-c", @"tput clear; tput cup 3 5; printf 'at'; printf '\033[7;1Hdone'"])
        {
            Size = (20, 8),
            DefaultTimeout = RealProgramTimeout,
        };

        using PtySession session = PtySession.Start(options);
        await session.WaitFor("done", RealProgramTimeout, TestContext.Current.CancellationToken);

        Assert.Equal("     at", session.Screen.Row(3));
        Assert.Equal("done", session.Screen.Row(6));
    }

    private static Screen Feed(string input, int columns, int rows)
    {
        var screen = new Screen(columns, rows);
        screen.Feed(input);
        return screen;
    }
}
