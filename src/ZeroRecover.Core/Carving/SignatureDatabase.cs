namespace ZeroRecover.Core.Carving;

/// <summary>
/// Registry of all sovereign file signature carvers.
/// </summary>
public static class SignatureDatabase
{
    private static readonly List<IFileCarver> _carvers =
    [
        new JpegCarver(),
        new PngCarver(),
        new ZipOfficeCarver(),
        new PdfCarver(),
        new MediaBoxCarver(),
        new DatabaseCarver()
    ];

    public static IReadOnlyList<IFileCarver> Carvers => _carvers;

    public static void RegisterCarver(IFileCarver carver)
    {
        if (carver != null && !_carvers.Contains(carver))
        {
            _carvers.Add(carver);
        }
    }
}
