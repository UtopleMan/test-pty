namespace TestPty;

/// <summary>
/// The program could not be started. Thrown instead of leaving a test waiting on a pseudo-terminal
/// that will never speak.
/// </summary>
public sealed class PtyStartException(string message) : Exception(message);
