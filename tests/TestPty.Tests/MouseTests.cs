using Xunit;

namespace TestPty.Tests;

/// <summary>
/// The exact bytes a click puts on the wire. A terminal that disagrees with one character of this
/// drops the report silently, so the sequences are asserted rather than trusted.
/// </summary>
public sealed class MouseTests
{
    private const string Introducer = "\u001b[<";

    [Fact]
    public void A_press_names_the_button_and_the_one_based_position()
    {
        Assert.Equal($"{Introducer}0;1;1M", Mouse.Press(0, 0));
        Assert.Equal($"{Introducer}0;13;6M", Mouse.Press(12, 5));
    }

    [Theory]
    [InlineData(MouseButton.Left, 0)]
    [InlineData(MouseButton.Middle, 1)]
    [InlineData(MouseButton.Right, 2)]
    public void Each_button_has_its_own_code(MouseButton button, int expectedCode)
    {
        Assert.Equal($"{Introducer}{expectedCode};4;3M", Mouse.Press(3, 2, button));
    }

    [Fact]
    public void A_release_ends_with_the_lower_case_terminator()
    {
        Assert.Equal($"{Introducer}0;4;3m", Mouse.Release(3, 2));
    }

    [Fact]
    public void A_click_is_the_press_and_the_release_that_follows_it()
    {
        Assert.Equal(Mouse.Press(3, 2) + Mouse.Release(3, 2), Mouse.Click(3, 2));
    }

    [Theory]
    [InlineData(WheelDirection.Up, 64)]
    [InlineData(WheelDirection.Down, 65)]
    public void A_wheel_notch_is_reported_as_a_press(WheelDirection direction, int expectedCode)
    {
        Assert.Equal($"{Introducer}{expectedCode};4;3M", Mouse.Wheel(3, 2, direction));
    }

    [Fact]
    public void A_move_carries_no_button()
    {
        Assert.Equal($"{Introducer}35;4;3M", Mouse.Move(3, 2));
    }

    [Fact]
    public void A_position_off_the_screen_is_rejected_rather_than_encoded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Mouse.Press(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mouse.Press(0, -1));
    }
}
