namespace NexusLabs.Eve;

/// <summary>
/// Identifies a fact or progress record within a version-27 source line.
/// </summary>
/// <param name="Line">The zero-based source-line position.</param>
/// <param name="Index">The zero-based record position within that line.</param>
public readonly record struct EveStreamPosition(int Line, int Index);
