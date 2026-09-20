using System.Runtime.InteropServices;

namespace TestPty.Interop;

/// <summary>The <c>struct winsize</c> that <c>forkpty</c> and <c>TIOCSWINSZ</c> take.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WinSize
{
    public ushort Rows;
    public ushort Columns;
    public ushort PixelWidth;
    public ushort PixelHeight;

    public static WinSize From(int columns, int rows) => new()
    {
        Rows = (ushort)rows,
        Columns = (ushort)columns,
    };
}
