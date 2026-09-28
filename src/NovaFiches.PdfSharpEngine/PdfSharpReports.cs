using PdfSharp.Pdf;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NovaFiches.PdfSharpEngine;

public static class PdfSharpReports
{
    internal static TableRenderer.TableLayout MakeImplantationLayoutForPublic(int colCount) => MakeImplantationLayout(colCount);

    private static TableRenderer.TableLayout MakeImplantationLayout(int colCount)
    {
        // A4 width (points) minus margins (see TableRenderer constants)
        const double pageW = 595;
        const double left = 36;
        const double right = 36;
        double tableW = pageW - left - right;

        // Expected columns (11): ID, Xth, Yth, Zth, Xmes, Ymes, Zmes, Dx, Dy, Dz, Statut
        // Use weights (scaled to table width) to avoid number overlap.
        double[] weights = colCount == 11
            ? new double[] { 1.2, 1.80, 1.80, 0.90, 1.80, 1.80, 0.90, 0.85, 0.85, 0.85, 1.00 }
            : Enumerable.Repeat(1.0, colCount).ToArray();

        double sum = weights.Sum();
        var widths = weights.Select(w => tableW * (w / sum)).ToArray();

        return new TableRenderer.TableLayout
        {
            ColumnWidths = widths,
            // Thick verticals: both sides of ID (left border + after col 1),
            // then after col 4 (Z théo), after col 7 (Z mes), after col 10 (Dz), plus right border.
            ThickVerticalSeparators = colCount == 11 ? new List<int> { 0, 1, 4, 7, 10, 11 } : new List<int> { 0, colCount }
        };
    }

    public static void GenerateImplantation(string outputPath, ImplantationTablePayload payload, string buildFooter)
    {
        var doc = new PdfDocument();
        doc.Info.Title = payload.Title ?? "Implantation";
        TableRenderer.RenderTable(doc, payload, MakeImplantationLayout(payload.Header?.Length ?? 11), buildFooter);
        SaveWithFallback(doc, outputPath);
    }

    public static void GenerateRapportComplet(string outputPath, RapportCompletPayload payload, string buildFooter)
    {
        var doc = new PdfDocument();
        doc.Info.Title = "Rapport complet";

        // Cover page (simple, deterministic) — keeps legacy jsPDF issues out.
        CoverPageRenderer.Render(doc, payload.Info, buildFooter);

        // Tables on new pages (deterministic, with contract thick lines only)
        var layoutDefault = new TableRenderer.TableLayout();

        // Use same layout as standalone implantation for consistency
        var layoutImplantation = payload.Implantation != null ? MakeImplantationLayout(payload.Implantation.Header?.Length ?? 11) : layoutDefault;
        if (payload.Implantation != null)
        {
            payload.Implantation.Title ??= "IMPLANTATION";
            TableRenderer.RenderTable(doc, payload.Implantation, layoutImplantation, buildFooter);
        }

        if (payload.MesureSurLigne != null)
        {
            payload.MesureSurLigne.Title ??= "MESURE SUR LIGNE";
            // Alternate shading by point (groups of 3 rows per point), like legacy reports.
            var layoutMesure = new TableRenderer.TableLayout
            {
                AlternateRowShading = true,
                ShadeGroupSize = 3,
                ShadeBrush = new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColor.FromArgb(245, 245, 245))
            };

            TableRenderer.RenderTable(doc, payload.MesureSurLigne, layoutMesure, buildFooter);
        }

        SaveWithFallback(doc, outputPath);
    }


    /// <summary>
    /// Rapport complet V2 (cover + IMP full + LIGNE REF full).
    /// This reuses the already validated renderers to keep the exact NOVATLAS look.
    /// </summary>
    public static void GenerateRapportCompletV2(
        string outputPath,
        Dictionary<string, string> coverInfo,
        string implantationPayloadJson,
        string ligneRefPayloadJson,
        string buildFooter)
    {
        var doc = new PdfDocument();
        doc.Info.Title = "Rapport complet";

        // Keep signature compatible with the UI message contract; coverInfo intentionally unused.
        _ = coverInfo;

        // No cover page for the "rapport complet" (terrain): start directly with the first useful section.

        // Section: Implantation (validated renderer)
        if (!string.IsNullOrWhiteSpace(implantationPayloadJson))
            ImplantationFullReportRenderer.Render(doc, implantationPayloadJson, buildFooter);

        // Section: Ligne de référence + mesure sur ligne (validated renderer)
        if (!string.IsNullOrWhiteSpace(ligneRefPayloadJson))
            LigneReferenceReportRenderer.Render(doc, ligneRefPayloadJson, buildFooter);

        SaveWithFallback(doc, outputPath);
    }




public static void GenerateImplantationFullFromJson(string outputPath, string payloadJson, string buildFooter)
{
    var doc = new PdfDocument();
    doc.Info.Title = "Implantation";
    doc.Info.Creator = "Nova-Fiches (PdfSharp)";
ImplantationFullReportRenderer.Render(doc, payloadJson, buildFooter);
PhotoAppendixRenderer.AppendFromPayload(doc, payloadJson, buildFooter);
SaveWithFallback(doc, outputPath);
}

public static void GeneratePointsTopoFromJson(string outputPath, string payloadJson, string buildFooter)
{
    var doc = new PdfDocument();
    doc.Info.Title = "Points topo (levé)";
    doc.Info.Creator = "Nova-Fiches (PdfSharp)";

    // Reuse the validated common NOVATLAS renderer/layout used by the other reports.
    // The payload already carries `topoStations` + `stationLibreRuns`; in LEVÉ mode,
    // ImplantationFullReportRenderer keeps the standard cartouche/mise en page and only
    // swaps the per-station content to observations polaires + résultats rectangulaires.
ImplantationFullReportRenderer.Render(doc, payloadJson, buildFooter);
PhotoAppendixRenderer.AppendFromPayload(doc, payloadJson, buildFooter);
SaveWithFallback(doc, outputPath);
}

public static void GenerateHeightTransferFromJson(string outputPath, string payloadJson, string buildFooter)
{
    var doc = new PdfDocument();
    doc.Info.Title = "Transfert d'altitude";
    doc.Info.Creator = "Nova-Fiches (PdfSharp)";
HeightTransferReportRenderer.Render(doc, payloadJson, buildFooter);
PhotoAppendixRenderer.AppendFromPayload(doc, payloadJson, buildFooter);
SaveWithFallback(doc, outputPath);
}

public static void GeneratePhotoReportFromJson(string outputPath, string payloadJson, string buildFooter)
{
    var doc = new PdfDocument();
    doc.Info.Title = "Reportage photo";
    doc.Info.Creator = "Nova-Fiches (PdfSharp)";
    PhotoAppendixRenderer.RenderStandaloneReport(doc, payloadJson, buildFooter);
    SaveWithFallback(doc, outputPath);
}

private static void SaveBytesWithFallback(byte[] pdfBytes, string outputPath)
{
    try
    {
        File.WriteAllBytes(outputPath, pdfBytes);
        return;
    }
    catch
    {
        // fallthrough
    }

    try
    {
        var dir = Path.GetDirectoryName(outputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var name = Path.GetFileNameWithoutExtension(outputPath);
        var ext = Path.GetExtension(outputPath);
        var alt = Path.Combine(dir, $"{name}_ALT{ext}");
        File.WriteAllBytes(alt, pdfBytes);
    }
    catch
    {
        // last resort: ignore
    }
}

    /// <summary>
    /// PdfSharp's Save() fails if the target PDF is opened by another process (Edge/Adobe).
    /// To avoid "no change" situations, we fallback to an auto-suffixed filename.
    /// </summary>
    private static void SaveWithFallback(PdfDocument doc, string outputPath)
    {
        try
        {
            doc.Save(outputPath);
            return;
        }
        catch (IOException)
        {
            // fallthrough
        }

        var dir = Path.GetDirectoryName(outputPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(outputPath);
        var ext = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".pdf";

        for (int i = 1; i <= 99; i++)
        {
            var alt = Path.Combine(dir, $"{name}_{i}{ext}");
            try
            {
                doc.Save(alt);
                return;
            }
            catch (IOException)
            {
                // try next
            }
        }

        // last resort: rethrow original path
        doc.Save(outputPath);
    }



    public static void GenerateLigneReferenceFromJson(string outputPdfPath, string payloadJson, string buildProof)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = "Ligne de référence";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

    // Renderer gère tout (header/footer/pagination) + parse JSON en interne.
    LigneReferenceReportRenderer.Render(doc, payloadJson, buildProof);
    // Annexe graphique optionnelle (includeGraphicalPlan) - avant PhotoAppendixRenderer pour que
    // son RestampFooters(doc, buildProof) final retamponne aussi ces nouvelles pages.
    LigneReferencePlanRenderer.AppendFromPayload(doc, payloadJson, buildProof);
    PhotoAppendixRenderer.AppendFromPayload(doc, payloadJson, buildProof);

    Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }

    /// <summary>
    /// Station (PdfSharp) from JSON payload.
    /// First iteration: header + cartouche + TYPE DE STATION skeleton.
    /// We will extend the renderer progressively to match the legacy station PDF.
    /// </summary>
    public static void GenerateStationFromJson(string outputPdfPath, string payloadJson, string buildProof)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = "Station";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

    StationReportRenderer.Render(doc, payloadJson, buildProof);
    PhotoAppendixRenderer.AppendFromPayload(doc, payloadJson, buildProof);
    // "Envoyer sur la fiche station" (onglet Plan station) : ajoutée en tout dernier,
    // donc en toute dernière page du document, comme demandé.
    StationPlanRenderer.AppendFromPayload(doc, payloadJson, buildProof);

    Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }

    /// <summary>
    /// "Page de garde" : en-tête + cartouche (infos dossier) sur une seule page, corps
    /// vide. Indépendant de toute donnée de levé (LandXML/AppLog) - n'accepte que les
    /// champs de cartouche du payload.
    /// </summary>
    public static void GenerateCoverOnlyFromJson(string outputPdfPath, string payloadJson, string buildProof)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = "Page de garde";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

        CoverOnlyReportRenderer.Render(doc, payloadJson, buildProof);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }

    /// <summary>
    /// Module manager "Fiches signalétiques" : une page par ligne du payload
    /// (fichesSignaletiques.rows). Utilisé aussi bien pour l'export "toutes les fiches"
    /// (payload avec toutes les lignes) que pour "une fiche par fichier" (MainForm appelle
    /// cette même méthode une fois par ligne, avec un payload à une seule ligne).
    /// </summary>
    public static void GenerateFichesSignaletiquesFromJson(string outputPdfPath, string payloadJson, string buildProof)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = "Fiches signalétiques";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

        FicheSignaletiqueRenderer.AppendFromPayload(doc, payloadJson, buildProof);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }

    /// <summary>
    /// Module manager "Contrôle classe de précision" (Phase 1) : page de garde, paramètres/résumé,
    /// conditions réglementaires, conclusion, tableau détaillé, annexe. Le payload est déjà
    /// entièrement calculé côté MainForm.cs (ControlePrecisionService.Analyze).
    /// </summary>
    public static void GenerateControlePrecisionFromJson(string outputPdfPath, string payloadJson, string buildProof)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = "Contrôle classe de précision";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

        ControlePrecisionRenderer.Render(doc, payloadJson, buildProof);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }

    public static void GenerateControlePolygonaleFromJson(string outputPdfPath, string payloadJson, string buildProof)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = "Contrôle de polygonale";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

        ControlePolygonaleRenderer.Render(doc, payloadJson, buildProof);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }

    // Export KMZ : PDF pour une mesure de distance (outil "Mesurer une distance" sur la carte
    // Leaflet) - distance + carte de la zone mesurée (KmzMeasureMapService, tracé + points dans
    // les mêmes couleurs qu'à l'écran), mise à l'échelle pour occuper le reste de la page A4 sans
    // déborder ni déformer le rapport largeur/hauteur d'origine.
    public static void GenerateDistanceMeasurePdf(string outputPdfPath, string payloadJson, string buildFooter)
    {
        JsonElement root = default;
        try { using var jd = JsonDocument.Parse(payloadJson); root = jd.RootElement.Clone(); } catch { }

        using var doc = new PdfDocument();
        doc.Info.Title = "Mesure de distance";
        doc.Info.Creator = "Nova-Fiches (PdfSharp)";

        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        const double marginL = 40, marginR = 40, marginB = 40;
        double contentW = page.Width.Point - marginL - marginR;
        double y = 50;

        gfx.DrawString("NOVATLAS GROUPE", NovatlasTheme.FontBold(22), new PdfSharp.Drawing.XSolidBrush(NovatlasTheme.ResolveBlue(root)),
            new PdfSharp.Drawing.XRect(marginL, y, contentW, 30), PdfSharp.Drawing.XStringFormats.Center);
        y += 40;

        var logo = NovatlasTheme.ResolveLogo(root);
        if (logo != null)
        {
            double maxW = Units.MmToPt(35), maxH = Units.MmToPt(28);
            double ar = (double)logo.PixelWidth / Math.Max(1, logo.PixelHeight);
            double iw = maxW, ih = iw / ar;
            if (ih > maxH) { ih = maxH; iw = ih * ar; }
            gfx.DrawImage(logo, marginL + (contentW - iw) / 2.0, y, iw, ih);
            y += ih + 20;
        }

        gfx.DrawString("MESURE DE DISTANCE", NovatlasTheme.FontBold(14), PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XRect(marginL, y, contentW, 20), PdfSharp.Drawing.XStringFormats.Center);
        y += 36;

        double distanceMeters = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("distanceMeters", out var dEl) && dEl.TryGetDouble(out var dv) ? dv : 0;
        string distanceText = distanceMeters >= 1000
            ? (distanceMeters / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " km"
            : distanceMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m";

        gfx.DrawString($"Distance : {distanceText}", NovatlasTheme.FontBold(20), PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XRect(marginL, y, contentW, 30), PdfSharp.Drawing.XStringFormats.Center);
        y += 32;

        gfx.DrawString(DateTime.Now.ToString("dd/MM/yyyy HH:mm"), NovatlasTheme.FontBody(9), PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XRect(marginL, y, contentW, 16), PdfSharp.Drawing.XStringFormats.Center);
        y += 24;

        // Carte de la mesure (facultative - absente si le tracé fait moins de 2 points ou si le
        // fond de carte n'a pas pu être récupéré) : mise à l'échelle dans l'espace restant de la
        // page A4, ratio d'aspect conservé, jamais agrandie au-delà de sa taille native.
        string? mapDataUrl = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("mapImageDataUrl", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString() : null;
        if (!string.IsNullOrWhiteSpace(mapDataUrl))
        {
            try
            {
                var commaIdx = mapDataUrl.IndexOf(',');
                var base64 = commaIdx >= 0 ? mapDataUrl[(commaIdx + 1)..] : mapDataUrl;
                var bytes = Convert.FromBase64String(base64);
                using var mapImg = PdfSharp.Drawing.XImage.FromStream(new MemoryStream(bytes));

                double availH = page.Height.Point - marginB - y;
                double scale = Math.Min(contentW / mapImg.PixelWidth, availH / mapImg.PixelHeight);
                scale = Math.Min(scale, 1.0); // jamais d'agrandissement au-delà de la taille native
                double mapW = mapImg.PixelWidth * scale;
                double mapH = mapImg.PixelHeight * scale;
                double mapX = marginL + (contentW - mapW) / 2.0;
                gfx.DrawRectangle(new PdfSharp.Drawing.XPen(PdfSharp.Drawing.XColor.FromArgb(200, 200, 200), 0.6), mapX, y, mapW, mapH);
                gfx.DrawImage(mapImg, mapX, y, mapW, mapH);
            }
            catch { /* image corrompue/illisible : PDF quand même généré, juste sans carte */ }
        }

        gfx.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath)!);
        SaveWithFallback(doc, outputPdfPath);
    }
}
