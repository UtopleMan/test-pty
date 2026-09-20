using System.Runtime.CompilerServices;
using Xunit;

namespace TestPty.Tests;

/// <summary>A case that needs forkpty, and that says so when it is skipped.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = TestConditions.UnixOnly;
        SkipUnless = nameof(TestConditions.IsUnix);
        SkipType = typeof(TestConditions);
    }
}
