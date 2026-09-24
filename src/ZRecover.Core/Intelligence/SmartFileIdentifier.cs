using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ZRecover.Core.Models;

namespace ZRecover.Core.Intelligence;

/// <summary>
/// Sovereign heuristic analyzer inspecting raw file payloads to predict
/// accurate file formats, true extensions, and synthesize human-meaningful file names.
/// </summary>
public static class SmartFileIdentifier
{
    public static void Analyze(RecoverableFile file)
    {
        if (file == null) return;

        // Obtain sample payload buffer (up to 4KB)
        byte[]? buffer = file.PreviewBytes ?? file.ResidentData;

        if ((buffer == null || buffer.Length < 16) && !string.IsNullOrEmpty(file.PhysicalPath) && File.Exists(file.PhysicalPath))
        {
            try
            {
                using var fs = File.OpenRead(file.PhysicalPath);
                buffer = new byte[Math.Min(4096, (int)fs.Length)];
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length) Array.Resize(ref buffer, read);
                file.PreviewBytes = buffer;
            }
            catch { }
        }

        if (buffer == null || buffer.Length == 0) return;

        ReadOnlySpan<byte> span = buffer;

        // 1. Check Magic Byte Signatures
        if (span.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) // PNG
        {
            file.SuggestedExtension = ".png";
            file.DetectedFormat = "PNG Portable Network Graphics";
            file.Category = FileCategory.Pictures;
            SuggestNameForHash(file, "image");
            return;
        }

        if (span.Length >= 3 && span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF) // JPEG
        {
            file.SuggestedExtension = ".jpg";
            file.DetectedFormat = "JPEG Compressed Image";
            file.Category = FileCategory.Pictures;
            SuggestNameForHash(file, "photo");
            return;
        }

        if (span.StartsWith("GIF87a"u8) || span.StartsWith("GIF89a"u8)) // GIF
        {
            file.SuggestedExtension = ".gif";
            file.DetectedFormat = "GIF Animated Image";
            file.Category = FileCategory.Pictures;
            SuggestNameForHash(file, "animation");
            return;
        }

        if (span.StartsWith("%PDF-"u8)) // PDF
        {
            file.SuggestedExtension = ".pdf";
            file.DetectedFormat = "Adobe PDF Document";
            file.Category = FileCategory.Documents;
            ExtractPdfTitle(file, span);
            return;
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04])) // ZIP / Office
        {
            string hexStr = Encoding.ASCII.GetString(buffer);
            if (hexStr.Contains("word/"))
            {
                file.SuggestedExtension = ".docx";
                file.DetectedFormat = "Microsoft Word Document (DOCX)";
                file.Category = FileCategory.Documents;
                SuggestNameForHash(file, "document");
                return;
            }
            if (hexStr.Contains("xl/"))
            {
                file.SuggestedExtension = ".xlsx";
                file.DetectedFormat = "Microsoft Excel Spreadsheet (XLSX)";
                file.Category = FileCategory.Documents;
                SuggestNameForHash(file, "spreadsheet");
                return;
            }
            if (hexStr.Contains("ppt/"))
            {
                file.SuggestedExtension = ".pptx";
                file.DetectedFormat = "Microsoft PowerPoint Presentation (PPTX)";
                file.Category = FileCategory.Documents;
                SuggestNameForHash(file, "presentation");
                return;
            }

            file.SuggestedExtension = ".zip";
            file.DetectedFormat = "ZIP Compressed Archive";
            file.Category = FileCategory.Archives;
            SuggestNameForHash(file, "archive");
            return;
        }

        if (span.StartsWith("SQLite format 3"u8))
        {
            file.SuggestedExtension = ".sqlite";
            file.DetectedFormat = "SQLite 3 Database";
            file.Category = FileCategory.Databases;
            SuggestNameForHash(file, "database");
            return;
        }

        if (span.StartsWith("BM"u8))
        {
            file.SuggestedExtension = ".bmp";
            file.DetectedFormat = "Windows Bitmap Image";
            file.Category = FileCategory.Pictures;
            SuggestNameForHash(file, "bitmap");
            return;
        }

        if (span.StartsWith("RIFF"u8) && span.Length >= 12 && span.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            file.SuggestedExtension = ".webp";
            file.DetectedFormat = "WebP Image";
            file.Category = FileCategory.Pictures;
            SuggestNameForHash(file, "image");
            return;
        }

        // 2. Check Text / JSON / XML Formats
        if (IsLikelyText(span))
        {
            string text = Encoding.UTF8.GetString(buffer);
            string trimmed = text.TrimStart();

            // Check JSON
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    file.SuggestedExtension = ".json";
                    file.DetectedFormat = "JSON Data Object";
                    file.Category = FileCategory.Documents;

                    // Format JSON cleanly for preview
                    using var ms = new MemoryStream();
                    using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                    doc.WriteTo(writer);
                    writer.Flush();
                    file.PreviewText = Encoding.UTF8.GetString(ms.ToArray());

                    // Extract smart title from JSON properties
                    ExtractJsonTitle(file, doc.RootElement);
                    return;
                }
                catch { }
            }

            // Check XML / HTML
            if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
                {
                    file.SuggestedExtension = ".svg";
                    file.DetectedFormat = "Scalable Vector Graphics (SVG)";
                    file.Category = FileCategory.Pictures;
                }
                else if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                {
                    file.SuggestedExtension = ".html";
                    file.DetectedFormat = "HTML Web Document";
                    file.Category = FileCategory.Documents;
                }
                else
                {
                    file.SuggestedExtension = ".xml";
                    file.DetectedFormat = "XML Structured Data";
                    file.Category = FileCategory.Documents;
                }
                file.PreviewText = text;
                ExtractXmlTitle(file, text);
                return;
            }

            // Check Markdown
            if (trimmed.StartsWith('#'))
            {
                file.SuggestedExtension = ".md";
                file.DetectedFormat = "Markdown Document";
                file.Category = FileCategory.Documents;
                file.PreviewText = text;

                string firstLine = trimmed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('#', ' ');
                if (!string.IsNullOrWhiteSpace(firstLine) && firstLine.Length <= 40)
                {
                    file.SuggestedFileName = SanitizeFileName(firstLine) + ".md";
                }
                return;
            }

            // Check Source Code
            if (trimmed.Contains("using System;") || trimmed.Contains("namespace "))
            {
                file.SuggestedExtension = ".cs";
                file.DetectedFormat = "C# Source File";
                file.Category = FileCategory.Documents;
                file.PreviewText = text;
                SuggestNameForHash(file, "SourceCode");
                return;
            }

            if (trimmed.Contains("import ") || trimmed.Contains("export ") || trimmed.Contains("function ") || trimmed.Contains("const "))
            {
                file.SuggestedExtension = ".js";
                file.DetectedFormat = "JavaScript / TypeScript File";
                file.Category = FileCategory.Documents;
                file.PreviewText = text;
                SuggestNameForHash(file, "script");
                return;
            }

            // Generic UTF-8 Text
            file.SuggestedExtension = ".txt";
            file.DetectedFormat = "Plain Text Document";
            file.Category = FileCategory.Documents;
            file.PreviewText = text;
            SuggestNameForHash(file, "text");
            return;
        }

        // Default fallback if format already known by extension
        if (!string.IsNullOrEmpty(file.Extension))
        {
            file.SuggestedExtension = file.Extension;
            file.DetectedFormat = $"{file.Category} File ({file.Extension})";
        }
    }

    private static void ExtractJsonTitle(RecoverableFile file, JsonElement root)
    {
        string? candidate = null;

        if (root.ValueKind == JsonValueKind.Object)
        {
            string[] keys = ["email", "name", "title", "username", "id", "service", "type", "fileName"];
            foreach (var key in keys)
            {
                if (root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    string val = prop.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        if (val.Contains('@'))
                        {
                            val = val.Split('@')[0];
                        }
                        candidate = SanitizeFileName(val);
                        break;
                    }
                }
            }
        }

        string folderHint = ExtractFolderHint(file.OriginalPath);
        if (!string.IsNullOrEmpty(candidate))
        {
            file.SuggestedFileName = !string.IsNullOrEmpty(folderHint)
                ? $"{folderHint}_{candidate}.json"
                : $"{candidate}_data.json";
        }
        else
        {
            SuggestNameForHash(file, "data");
        }
    }

    private static void ExtractXmlTitle(RecoverableFile file, string xml)
    {
        int titleStart = xml.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (titleStart >= 0)
        {
            int titleEnd = xml.IndexOf("</title>", titleStart, StringComparison.OrdinalIgnoreCase);
            if (titleEnd > titleStart)
            {
                string title = xml.Substring(titleStart + 7, titleEnd - (titleStart + 7)).Trim();
                if (!string.IsNullOrWhiteSpace(title) && title.Length <= 40)
                {
                    file.SuggestedFileName = SanitizeFileName(title) + (file.SuggestedExtension ?? ".xml");
                    return;
                }
            }
        }
        SuggestNameForHash(file, "document");
    }

    private static void ExtractPdfTitle(RecoverableFile file, ReadOnlySpan<byte> span)
    {
        string ascii = Encoding.ASCII.GetString(span);
        int titleIdx = ascii.IndexOf("/Title (", StringComparison.Ordinal);
        if (titleIdx >= 0)
        {
            int end = ascii.IndexOf(')', titleIdx + 8);
            if (end > titleIdx + 8)
            {
                string title = ascii.Substring(titleIdx + 8, end - (titleIdx + 8)).Trim();
                if (!string.IsNullOrWhiteSpace(title) && title.Length <= 40)
                {
                    file.SuggestedFileName = SanitizeFileName(title) + ".pdf";
                    return;
                }
            }
        }
        SuggestNameForHash(file, "document");
    }

    private static void SuggestNameForHash(RecoverableFile file, string defaultPrefix)
    {
        string currentName = file.FileName;
        string ext = file.SuggestedExtension ?? file.Extension;

        bool isHashOrTemp = IsHashLike(currentName) || currentName.StartsWith("~") || currentName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

        if (isHashOrTemp)
        {
            string folderHint = ExtractFolderHint(file.OriginalPath);
            string prefix = !string.IsNullOrEmpty(folderHint) ? folderHint : defaultPrefix;
            string shortHash = currentName.Length > 8 ? currentName.Substring(0, 8) : currentName;
            file.SuggestedFileName = $"{prefix}_{shortHash}{ext}";
        }
        else if (!currentName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            file.SuggestedFileName = Path.GetFileNameWithoutExtension(currentName) + ext;
        }
    }

    private static string ExtractFolderHint(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                string name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("$") && !name.Equals("Temp", StringComparison.OrdinalIgnoreCase))
                {
                    return SanitizeFileName(name);
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static bool IsHashLike(string name)
    {
        string pure = Path.GetFileNameWithoutExtension(name);
        if (pure.Length >= 16)
        {
            int hexCount = pure.Count(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
            return hexCount >= pure.Length * 0.8;
        }
        return false;
    }

    private static bool IsLikelyText(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0) return false;
        int printable = 0;
        int sampleSize = Math.Min(span.Length, 512);

        for (int i = 0; i < sampleSize; i++)
        {
            byte b = span[i];
            if (b == 0) return false;
            if (b is >= 0x20 and <= 0x7E or 0x09 or 0x0A or 0x0D or >= 0x80)
            {
                printable++;
            }
        }

        return (printable / (double)sampleSize) > 0.90;
    }

    private static string SanitizeFileName(string input)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (Array.IndexOf(invalid, c) < 0 && c != ' ')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('_');
        }
        string result = sb.ToString().Trim('_');
        return result.Length > 32 ? result.Substring(0, 32) : result;
    }
}
