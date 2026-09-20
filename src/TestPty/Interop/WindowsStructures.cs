using System.Runtime.InteropServices;

namespace TestPty.Interop;

/// <summary>A console screen size, in character cells.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Coord
{
    public short X;
    public short Y;

    public static Coord From(int columns, int rows) => new()
    {
        X = (short)columns,
        Y = (short)rows,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfo
{
    public int Size;
    public nint Reserved;
    public nint Desktop;
    public nint Title;
    public int X;
    public int Y;
    public int XSize;
    public int YSize;
    public int XCountChars;
    public int YCountChars;
    public int FillAttribute;
    public int Flags;
    public short ShowWindow;
    public short ReservedLength;
    public nint Reserved2;
    public nint StandardInput;
    public nint StandardOutput;
    public nint StandardError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoEx
{
    public StartupInfo StartupInfo;
    public nint AttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public nint Process;
    public nint Thread;
    public int ProcessId;
    public int ThreadId;
}
