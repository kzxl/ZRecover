using System.Buffers.Binary;
using ZRecover.Core.Models;

namespace ZRecover.Core.Carving;

/// <summary>
/// Deep carver for SQLite Database files (.sqlite, .db) calculating exact page counts and page sizes.
/// </summary>
public sealed class DatabaseCarver : IFileCarver
{
    public string FileExtension => ".sqlite";

    public FileCategory Category => FileCategory.Databases;

    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset)
    {
        headerOffset = -1;
        if (buffer.Length < 16) return false;

        if (buffer[..16].SequenceEqual(SqliteHeader))
        {
            headerOffset = 0;
            return true;
        }

        return false;
    }

    public CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + 32 >= buffer.Length)
            return CarveResult.Failed;

        ushort rawPageSize = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(headerOffset + 16, 2));
        int pageSize = rawPageSize == 1 ? 65536 : rawPageSize;

        // Valid SQLite page sizes are powers of 2 between 512 and 65536
        if (pageSize < 512 || pageSize > 65536 || (pageSize & (pageSize - 1)) != 0)
            return CarveResult.Failed;

        uint pageCount = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(headerOffset + 28, 4));
        if (pageCount == 0)
        {
            // In-WAL or uncheckpointed database; minimum valid is at least 1 page
            pageCount = 1;
        }

        long totalSize = (long)pageCount * pageSize;
        if (totalSize <= 0 || totalSize > buffer.Length)
            return CarveResult.Failed;

        return new CarveResult
        {
            Success = true,
            Length = totalSize,
            Extension = FileExtension,
            Category = Category,
            Health = RecoveryHealth.Excellent,
            Description = $"SQLite 3 Database ({pageCount} pages × {pageSize}B)"
        };
    }
}
