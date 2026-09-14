using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Carving;

/// <summary>
/// Result of a file carving extraction attempt.
/// </summary>
public sealed class CarveResult
{
    public bool Success { get; set; }

    public long Length { get; set; }

    public string Extension { get; set; } = string.Empty;

    public FileCategory Category { get; set; } = FileCategory.Other;

    public byte[]? PreviewBytes { get; set; }

    public RecoveryHealth Health { get; set; } = RecoveryHealth.Good;

    public string Description { get; set; } = string.Empty;

    public static CarveResult Failed => new() { Success = false };
}

/// <summary>
/// Interface implemented by format-specific carving parsers.
/// </summary>
public interface IFileCarver
{
    string FileExtension { get; }

    FileCategory Category { get; }

    /// <summary>
    /// Checks if the stream at the given offset matches the magic header of this carver.
    /// </summary>
    bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset);

    /// <summary>
    /// Parses internal structures from the buffer starting at headerOffset to determine exact file length.
    /// </summary>
    CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset);
}
