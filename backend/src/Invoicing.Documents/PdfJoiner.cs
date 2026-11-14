using QuestPDF.Fluent;

namespace Burkard.Invoicing.Documents;

/// <summary>Joins finished PDFs into one, every page as it is and in the order
/// given — for printing a half-year's stored grave invoices in one go.</summary>
public static class PdfJoiner
{
    /// <summary>QuestPDF's document operations work on files, so each join gets
    /// a directory of its own under this one, removed again afterwards.</summary>
    private static readonly string WorkRoot = Path.Combine(
        Path.GetTempPath(),
        "invoicing-pdf-join"
    );

    public static byte[] Join(IReadOnlyList<byte[]> pdfs)
    {
        if (pdfs.Count == 0)
        {
            throw new ArgumentException("There is nothing to join.", nameof(pdfs));
        }

        var directory = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var inputs = new List<string>(pdfs.Count);
            for (var i = 0; i < pdfs.Count; i++)
            {
                var input = Path.Combine(directory, $"{i}.pdf");
                File.WriteAllBytes(input, pdfs[i]);
                inputs.Add(input);
            }

            var operation = DocumentOperation.LoadFile(inputs[0]);
            foreach (var input in inputs.Skip(1))
            {
                operation = operation.MergeFile(input);
            }

            var output = Path.Combine(directory, "joined.pdf");
            operation.Save(output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
