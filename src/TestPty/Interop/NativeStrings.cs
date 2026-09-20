using System.Runtime.InteropServices;

namespace TestPty.Interop;

/// <summary>
/// Holds UTF-8 copies of the strings a child needs, in native memory, so that everything the child
/// reads after a fork was allocated before it.
/// </summary>
internal sealed unsafe class NativeStrings : IDisposable
{
    private readonly List<nint> allocations = [];

    public byte* Allocate(string value)
    {
        nint pointer = Marshal.StringToCoTaskMemUTF8(value);
        allocations.Add(pointer);
        return (byte*)pointer;
    }

    /// <summary>A NULL-terminated <c>char**</c>, as <c>execve</c> expects for argv and envp.</summary>
    public byte** AllocateArray(IReadOnlyList<string> values)
    {
        nint block = Marshal.AllocCoTaskMem((values.Count + 1) * sizeof(nint));
        allocations.Add(block);

        byte** array = (byte**)block;

        for (int index = 0; index < values.Count; index++)
        {
            array[index] = Allocate(values[index]);
        }

        array[values.Count] = null;
        return array;
    }

    public void Dispose()
    {
        foreach (nint pointer in allocations)
        {
            Marshal.FreeCoTaskMem(pointer);
        }

        allocations.Clear();
    }
}
