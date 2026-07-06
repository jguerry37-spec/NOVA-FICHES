namespace NovaFiches.PdfSharpEngine;

public static class Units
{
    // PDFsharp uses points (1/72 inch). 1 inch = 25.4 mm.
    public static double MmToPt(double mm) => mm * 72.0 / 25.4;
}
