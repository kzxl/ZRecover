using System.Text;
using ZeroRecover.Core.Intelligence;
using ZeroRecover.Core.Models;
using Xunit;

namespace ZeroRecover.Tests;

public class SmartFileIdentifierTests
{
    [Fact]
    public void Analyze_JsonPayloadWithEmail_SuggestsExtensionAndName()
    {
        string json = "{\n  \"email\": \"phong.tv@gmail.com\",\n  \"updatedAt\": 1779526010834,\n  \"models\": [\"claude-4.5\"]\n}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        var file = new RecoverableFile
        {
            FileName = "297e0bffd2212b428a4d4f5979c64e5285ac7458",
            OriginalPath = @"C:\Users\phong.vo\AppData\Local\Anthropic\quota\297e0bffd2212b428a4d4f5979c64e5285ac7458",
            PreviewBytes = bytes,
            Size = bytes.Length
        };

        SmartFileIdentifier.Analyze(file);

        Assert.Equal(".json", file.SuggestedExtension);
        Assert.Equal("JSON Data Object", file.DetectedFormat);
        Assert.Equal(FileCategory.Documents, file.Category);
        Assert.True(file.HasSuggestion);
        Assert.NotNull(file.SuggestedFileName);
        Assert.Contains("phong.tv", file.SuggestedFileName);
        Assert.EndsWith(".json", file.SuggestedFileName);
        Assert.NotNull(file.PreviewText);
        Assert.Contains("\"phong.tv@gmail.com\"", file.PreviewText);

        // Apply suggestion
        file.ApplySuggestedName();
        Assert.Equal(file.SuggestedFileName, file.FileName);
        Assert.Equal(".json", file.Extension);
        Assert.False(file.HasSuggestion);
    }

    [Fact]
    public void Analyze_PngSignature_IdentifiesPngAndPicturesCategory()
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
        var file = new RecoverableFile
        {
            FileName = "9b4f2a1c8e3d7a6b5c4d3e2f1a0b9c8d",
            PreviewBytes = pngHeader,
            Size = pngHeader.Length
        };

        SmartFileIdentifier.Analyze(file);

        Assert.Equal(".png", file.SuggestedExtension);
        Assert.Equal("PNG Portable Network Graphics", file.DetectedFormat);
        Assert.Equal(FileCategory.Pictures, file.Category);
        Assert.True(file.HasSuggestion);
        Assert.EndsWith(".png", file.SuggestedFileName);
    }

    [Fact]
    public void Analyze_PdfSignatureWithTitle_ExtractsTitle()
    {
        string pdfContent = "%PDF-1.4\n1 0 obj\n<< /Title (Zero Universe Recovery Spec) >>\nendobj";
        byte[] pdfBytes = Encoding.ASCII.GetBytes(pdfContent);

        var file = new RecoverableFile
        {
            FileName = "temp_doc_7719",
            PreviewBytes = pdfBytes,
            Size = pdfBytes.Length
        };

        SmartFileIdentifier.Analyze(file);

        Assert.Equal(".pdf", file.SuggestedExtension);
        Assert.Equal("Adobe PDF Document", file.DetectedFormat);
        Assert.Equal(FileCategory.Documents, file.Category);
        Assert.Equal("Zero_Universe_Recovery_Spec.pdf", file.SuggestedFileName);
    }
}
