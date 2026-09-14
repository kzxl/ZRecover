using System.Buffers;
using System.Diagnostics;
using ZeroRecover.Core.Disk;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Carving;

/// <summary>
/// High-throughput streaming sector carving engine utilizing ArrayPool buffers and sliding overlaps.
/// </summary>
public sealed class DeepCarverEngine
{
    private const int BufferSize = 4 * 1024 * 1024; // 4MB sliding buffer
    private const int OverlapSize = 64 * 1024;       // 64KB boundary overlap

    public async Task<List<RecoverableFile>> CarveAsync(
        RawDiskReader reader,
        long startByteOffset,
        long totalBytesToScan,
        ScanOptions options,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<RecoverableFile>();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                long currentOffset = startByteOffset;
                long endOffset = startByteOffset + totalBytesToScan;
                int fileCounter = 0;
                int stride = reader.SectorSize > 0 ? reader.SectorSize : 512;

                while (currentOffset < endOffset)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int bytesToRead = (int)Math.Min(BufferSize, endOffset - currentOffset);
                    int bytesRead = reader.ReadAligned(currentOffset, buffer, 0, bytesToRead);
                    if (bytesRead <= 0)
                        break;

                    ScanBufferWindow(buffer, bytesRead, currentOffset, options, stride, ref fileCounter, results);

                    currentOffset += (bytesRead - OverlapSize);
                    if (currentOffset < 0) currentOffset = 0;

                    // Telemetry report
                    if (progress != null && stopwatch.ElapsedMilliseconds > 200)
                    {
                        double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                        long scanned = currentOffset - startByteOffset;
                        double mbPerSec = elapsedSec > 0 ? (scanned / (1024.0 * 1024.0)) / elapsedSec : 0.0;

                        progress.Report(new ScanProgressReport
                        {
                            ScannedBytes = scanned,
                            TotalBytes = totalBytesToScan,
                            FilesFound = results.Count,
                            CurrentOperation = $"Carving raw sectors @ 0x{currentOffset:X8}...",
                            MegaBytesPerSecond = mbPerSec
                        });
                        stopwatch.Restart();
                    }
                }

                return results;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }, cancellationToken);
    }

    private static void ScanBufferWindow(
        byte[] buffer,
        int bytesRead,
        long currentOffset,
        ScanOptions options,
        int stride,
        ref int fileCounter,
        List<RecoverableFile> results)
    {
        ReadOnlySpan<byte> window = buffer.AsSpan(0, bytesRead);

        for (int i = 0; i <= bytesRead - 16; i += stride)
        {
            var slice = window.Slice(i);

            foreach (var carver in SignatureDatabase.Carvers)
            {
                if (options.FilterCategory != FileCategory.All && carver.Category != options.FilterCategory)
                    continue;

                if (carver.CanCarve(slice, out int headerOffset))
                {
                    var carveResult = carver.Carve(slice, headerOffset);
                    if (carveResult.Success && carveResult.Length > 0)
                    {
                        fileCounter++;
                        long absoluteFileOffset = currentOffset + i + headerOffset;
                        string fileName = $"Carved_{fileCounter:D5}_{absoluteFileOffset:X8}{carveResult.Extension}";

                        var rec = new RecoverableFile
                        {
                            FileName = fileName,
                            OriginalPath = $@"RAW:\{options.TargetDrive}\{fileName}",
                            Size = carveResult.Length,
                            Extension = carveResult.Extension,
                            Category = carveResult.Category,
                            Health = carveResult.Health,
                            RecoveryMethod = "DEEP_CARVE",
                            SourceDrive = options.TargetDrive,
                            SourceOffset = absoluteFileOffset,
                            PreviewBytes = carveResult.PreviewBytes,
                            CreatedTime = DateTime.Now
                        };

                        results.Add(rec);

                        // Advance past this carved file to prevent redundant hits, keeping alignment
                        long skipBytes = ((carveResult.Length + stride - 1) / stride) * stride;
                        i += (int)Math.Min(skipBytes, bytesRead - i - stride);
                        break;
                    }
                }
            }
        }
    }
}
