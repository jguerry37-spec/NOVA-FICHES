using System.Globalization;
using System.Linq;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Module manager "Contrôle de polygonale" : page de garde + infos projet, un tableau (+
/// synthèse) par onglet de contrôle XY, un tableau (+ synthèse) par onglet de contrôle Z, tableau
/// GeoBase, conclusion. Portage v1 ("essentiel d'abord") de l'appli indépendante de référence
/// (rapport_polygonale_app, moteur Python python-docx) : organigramme d'équipe, blocs matériel
/// avec photos/fiches techniques et blocs de méthodologie pré-remplis sont volontairement hors
/// périmètre (voir le plan) - le payload est déjà entièrement calculé côté MainForm.cs
/// (ControlePolygonaleService), ce renderer ne fait que le mettre en page, comme
/// ControlePrecisionRenderer. Les tableaux de points réutilisent TableRenderer.RenderImplantationTable
/// (pagination automatique déjà éprouvée par tous les autres rapports à tableau de points).
/// </summary>
internal static class ControlePolygonaleRenderer
{
    private const double MarginL = 40;
    private const double MarginR = 40;
    private const double MarginTop = 40;
    private static double MarginBottom => LayoutConstants.FooterReservePt;

    public static void Render(PdfDocument doc, string payloadJson, string buildFooter)
    {
        JsonElement root;
        try
        {
            using var jd = JsonDocument.Parse(payloadJson);
            root = jd.RootElement.Clone();
        }
        catch
        {
            return;
        }

        var project = GetProp(root, "project") ?? default;

        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        double pageH = page.Height.Point;
        double contentW = page.Width.Point - MarginL - MarginR;
        var gfx = XGraphics.FromPdfPage(page);
        double y = MarginTop;

        void NewPage()
        {
            gfx.Dispose();
            page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            y = MarginTop;
        }

        void EnsureSpace(double needed)
        {
            if (y + needed > pageH - MarginBottom) NewPage();
        }

        double DrawWrapped(string text, XFont font, XBrush brush, XStringFormat format, double extraGapAfter = 4)
        {
            if (string.IsNullOrWhiteSpace(text)) return y;
            var lines = WrapText(gfx, text, font, contentW);
            double lineH = font.GetHeight() * 1.4;
            foreach (var line in lines)
            {
                EnsureSpace(lineH);
                gfx.DrawString(line, font, brush, new XRect(MarginL, y, contentW, lineH), format);
                y += lineH;
            }
            y += extraGapAfter;
            return y;
        }

        void DrawTextFit(string text, XRect rect, XFont baseFont, XStringFormat format)
        {
            double fs = baseFont.Size;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var f = new XFont("Arial", fs, baseFont.Style);
                if (gfx.MeasureString(text, f).Width <= rect.Width) { gfx.DrawString(text, f, XBrushes.Black, rect, format); return; }
                fs = Math.Max(6.0, fs - 0.75);
            }
            gfx.DrawString(text, baseFont, XBrushes.Black, rect, format);
        }

        void DrawRevisionTable(JsonElement? rowsEl)
        {
            string[] headers = { "Indice", "Date", "Établi par", "Vérifié par", "Approuvé par" };
            double[] widths = { contentW * 0.10, contentW * 0.14, contentW * 0.253, contentW * 0.253, contentW * 0.254 };
            const double rowH = 20;
            var zebraBrush = new XSolidBrush(XColor.FromArgb(248, 249, 251));

            var rows = new List<(string Indice, string Date, string Etabli, string Verifie, string Approuve)>();
            if (rowsEl != null && rowsEl.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rowsEl.Value.EnumerateArray())
                    rows.Add((Str(r, "indice") ?? "", Str(r, "date") ?? "", Str(r, "etabli") ?? "", Str(r, "verifie") ?? "", Str(r, "approuve") ?? ""));
            }
            if (rows.Count == 0) rows.Add(("A", "", "", "", ""));

            EnsureSpace(rowH * (rows.Count + 1));
            double cx = MarginL;
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
            for (int c = 0; c < headers.Length; c++)
            {
                gfx.DrawString(headers[c], NovatlasTheme.FontBold(9), XBrushes.Black, new XRect(cx + 2, y, widths[c] - 4, rowH), XStringFormats.Center);
                cx += widths[c];
            }
            y += rowH;

            int idx = 0;
            foreach (var row in rows)
            {
                if (idx % 2 == 1) gfx.DrawRectangle(zebraBrush, MarginL, y, contentW, rowH);
                var cells = new[] { row.Indice, row.Date, row.Etabli, row.Verifie, row.Approuve };
                cx = MarginL;
                for (int c = 0; c < cells.Length; c++)
                {
                    DrawTextFit(cells[c], new XRect(cx + 3, y, widths[c] - 6, rowH), NovatlasTheme.FontBody(9), XStringFormats.Center);
                    cx += widths[c];
                }
                y += rowH;
                idx++;
            }

            // Grille (bordure extérieure + séparateurs verticaux/horizontaux)
            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y - rowH * (rows.Count + 1), contentW, rowH * (rows.Count + 1));
            cx = MarginL;
            for (int c = 0; c < widths.Length - 1; c++) { cx += widths[c]; gfx.DrawLine(NovatlasTheme.GridPenThin(), cx, y - rowH * (rows.Count + 1), cx, y); }
            for (int r = 0; r <= rows.Count; r++)
            {
                double ly = y - rowH * (rows.Count + 1) + r * rowH;
                gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL, ly, MarginL + contentW, ly);
            }
            y += 12;
        }

        // ===== Page de garde =====
        // Titre adapté au type de rapport (Contrôle = écarts théo/mesuré, déjà existant ; Création =
        // mise en place, coordonnées brutes) et au niveau (primaire/secondaire) - simple substitution
        // de texte, aucun impact de structure (confirmé avec l'utilisateur).
        string reportType = (Str(project, "reportType") ?? "controle").ToLowerInvariant();
        string level = (Str(project, "level") ?? "primaire").ToLowerInvariant();
        string levelLabel = level == "secondaire" ? "SECONDAIRE" : "PRIMAIRE";
        string reportTitle = reportType == "creation"
            ? $"MISE EN PLACE DE LA POLYGONALE {levelLabel}"
            : $"CONTRÔLE DE LA POLYGONALE {levelLabel}";

        gfx.DrawString("NOVATLAS GROUPE", NovatlasTheme.FontBold(22), new XSolidBrush(NovatlasTheme.ResolveBlue(root)),
            new XRect(MarginL, y, contentW, 30), XStringFormats.Center);
        y += 30;
        gfx.DrawString(reportTitle, NovatlasTheme.FontBody(14), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.Center);
        y += 30;

        var logo = NovatlasTheme.ResolveLogo(root);
        if (logo != null)
        {
            double maxW = Units.MmToPt(45), maxH = Units.MmToPt(35);
            double ar = (double)logo.PixelWidth / Math.Max(1, logo.PixelHeight);
            double iw = maxW, ih = iw / ar;
            if (ih > maxH) { ih = maxH; iw = ih * ar; }
            EnsureSpace(ih + 12);
            gfx.DrawImage(logo, MarginL + (contentW - iw) / 2.0, y, iw, ih);
            y += ih + 16;
        }

        // Tableau de révision (Indice / Date / Établi par / Vérifié par / Approuvé par) - texte
        // libre, plusieurs lignes possibles (historique des indices), comme le document de
        // référence réel (pas de case signature image, contrairement à l'ancienne appli Python).
        DrawRevisionTable(GetProp(project, "revisionRows"));

        gfx.Dispose();

        // ===== Tableaux de contrôle XY (un par onglet détecté) =====
        var xySheets = GetProp(root, "xySheets");
        if (xySheets != null && xySheets.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var sheet in xySheets.Value.EnumerateArray())
                RenderXySheet(doc, sheet, buildFooter);
        }

        // ===== Tableaux de contrôle Z (un par onglet détecté) =====
        var zSheets = GetProp(root, "zSheets");
        if (zSheets != null && zSheets.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var sheet in zSheets.Value.EnumerateArray())
                RenderZSheet(doc, sheet, buildFooter);
        }

        // ===== GeoBase =====
        var geobase = GetProp(root, "geobase");
        if (geobase != null && geobase.Value.ValueKind == JsonValueKind.Array && geobase.Value.GetArrayLength() > 0)
        {
            var geoPayload = new ImplantationTablePayload
            {
                Title = "Coordonnées issues de la GeoBase",
                SubTitle = "",
                Header = new[] { "Point", "X", "Y", "Z" },
                Rows = geobase.Value.EnumerateArray().Select(r => new[]
                {
                    Str(r, "point") ?? "",
                    F3(r, "x"),
                    F3(r, "y"),
                    F3(r, "z"),
                }).ToList()
            };
            var layout = new TableRenderer.TableLayout { ColumnWidths = new double[] { 130, 130, 130, 125} };
            TableRenderer.RenderImplantationTable(doc, geoPayload, layout, buildFooter);
        }

        // ===== Conclusion =====
        page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        gfx = XGraphics.FromPdfPage(page);
        y = MarginTop;

        gfx.DrawString("Conclusion", NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 26;

        var forcedConclusion = Str(project, "conclusionText");
        string conclusion = !string.IsNullOrWhiteSpace(forcedConclusion)
            ? forcedConclusion!
            : BuildAutoConclusion(project, xySheets, zSheets);
        DrawWrapped(conclusion, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);

        gfx.Dispose();
    }

    private static void RenderXySheet(PdfDocument doc, JsonElement sheet, string buildFooter)
    {
        string name = Str(sheet, "name") ?? "Contrôle XY";
        var stats = GetProp(sheet, "stats") ?? default;
        var rowsEl = GetProp(sheet, "rows");

        var rows = new List<string[]>();
        if (rowsEl != null && rowsEl.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rowsEl.Value.EnumerateArray())
            {
                rows.Add(new[]
                {
                    Str(r, "point") ?? "",
                    F3OrDash(r, "xTheo"),
                    F3OrDash(r, "yTheo"),
                    F4OrDash(r, "xCtrl"),
                    F4OrDash(r, "yCtrl"),
                    F4OrDash(r, "dx"),
                    F4OrDash(r, "dy"),
                    F4OrDash(r, "deltaXy"),
                });
            }
        }

        var payload = new ImplantationTablePayload
        {
            Title = $"Contrôle GNSS/optique — {name}",
            SubTitle = XySummaryText(stats),
            Header = new[] { "Point", "X théo", "Y théo", "X ctrl", "Y ctrl", "ΔX", "ΔY", "ΔXY" },
            Rows = rows
        };
        var layout = new TableRenderer.TableLayout { ColumnWidths = new double[] { 70, 68, 68, 68, 68, 55, 55, 63 } };
        TableRenderer.RenderImplantationTable(doc, payload, layout, buildFooter);
    }

    private static void RenderZSheet(PdfDocument doc, JsonElement sheet, string buildFooter)
    {
        string name = Str(sheet, "name") ?? "Contrôle Z";
        var stats = GetProp(sheet, "stats") ?? default;
        var rowsEl = GetProp(sheet, "rows");

        var rows = new List<string[]>();
        if (rowsEl != null && rowsEl.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rowsEl.Value.EnumerateArray())
            {
                rows.Add(new[]
                {
                    Str(r, "point") ?? "",
                    F3OrDash(r, "zTheo"),
                    F4OrDash(r, "zCtrl"),
                    F4OrDash(r, "dz"),
                });
            }
        }

        var payload = new ImplantationTablePayload
        {
            Title = $"Contrôle altimétrique — {name}",
            SubTitle = ZSummaryText(stats),
            Header = new[] { "Point", "Z théo", "Z ctrl", "ΔZ" },
            Rows = rows
        };
        var layout = new TableRenderer.TableLayout { ColumnWidths = new double[] { 148, 148, 148, 71 } };
        TableRenderer.RenderImplantationTable(doc, payload, layout, buildFooter);
    }

    private static string XySummaryText(JsonElement stats)
    {
        int count = (int)Num(stats, "count");
        int complete = (int)Num(stats, "complete");
        double? meanDeltaXy = NumOrNull(stats, "meanDeltaXy");
        double? maxDeltaXy = NumOrNull(stats, "maxDeltaXy");
        string? maxPoint = Str(stats, "maxDeltaXyPoint");
        return $"{complete} point(s) contrôlé(s) sur {count}. Écart planimétrique moyen : {FmtOrDash(meanDeltaXy, 4)} m. "
             + $"Écart planimétrique maximal : {FmtOrDash(maxDeltaXy, 4)} m"
             + (string.IsNullOrWhiteSpace(maxPoint) ? "." : $" sur le point {maxPoint}.");
    }

    private static string ZSummaryText(JsonElement stats)
    {
        int count = (int)Num(stats, "count");
        int complete = (int)Num(stats, "complete");
        double? meanAbsDz = NumOrNull(stats, "meanAbsDz");
        double? maxAbsDz = NumOrNull(stats, "maxAbsDz");
        string? maxPoint = Str(stats, "maxAbsDzPoint");
        return $"{complete} point(s) contrôlé(s) sur {count}. Écart altimétrique moyen : {FmtOrDash(meanAbsDz, 4)} m. "
             + $"Écart altimétrique maximal : {FmtOrDash(maxAbsDz, 4)} m"
             + (string.IsNullOrWhiteSpace(maxPoint) ? "." : $" sur le point {maxPoint}.");
    }

    // Conclusion générée à partir des écarts max toutes tableaux confondus, quand l'utilisateur n'a
    // pas forcé de texte - même logique que conclusion_text() de l'appli source.
    private static string BuildAutoConclusion(JsonElement project, JsonElement? xySheets, JsonElement? zSheets)
    {
        double maxXy = 0;
        if (xySheets != null && xySheets.Value.ValueKind == JsonValueKind.Array)
            foreach (var s in xySheets.Value.EnumerateArray())
                maxXy = Math.Max(maxXy, NumOrNull(GetProp(s, "stats") ?? default, "maxDeltaXy") ?? 0);

        double maxZ = 0;
        bool hasZ = false;
        if (zSheets != null && zSheets.Value.ValueKind == JsonValueKind.Array)
            foreach (var s in zSheets.Value.EnumerateArray())
            {
                hasZ = true;
                maxZ = Math.Max(maxZ, NumOrNull(GetProp(s, "stats") ?? default, "maxAbsDz") ?? 0);
            }
        bool hasXy = xySheets != null && xySheets.Value.ValueKind == JsonValueKind.Array && xySheets.Value.GetArrayLength() > 0;

        var parts = new List<string>
        {
            $"Ce rapport rend compte du contrôle de polygonale pour le projet {Str(project, "projectName") ?? ""}."
        };
        if (hasXy) parts.Add($"Les contrôles planimétriques présentent un écart maximal de {maxXy.ToString("F4", CultureInfo.InvariantCulture)} m.");
        if (hasZ) parts.Add($"Les contrôles altimétriques présentent un écart maximal de {maxZ.ToString("F4", CultureInfo.InvariantCulture)} m.");
        parts.Add("Les résultats sont à apprécier au regard des tolérances et des conditions d'intervention.");
        parts.Add("Une validation par la maîtrise d'œuvre reste nécessaire lorsque les points doivent être considérés comme références géométriques officielles.");
        return string.Join(" ", parts);
    }

    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = "";
            foreach (var w in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? w : line + " " + w;
                if (gfx.MeasureString(candidate, font).Width <= maxWidth) { line = candidate; continue; }
                if (!string.IsNullOrEmpty(line)) result.Add(line);
                line = w;
            }
            result.Add(line);
        }
        return result;
    }

    private static string? Str(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private static double? NumOrNull(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : (double?)null;

    private static bool? GetBool(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) &&
        (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null;

    private static JsonElement? GetProp(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) ? v : (JsonElement?)null;

    private static string F3(JsonElement el, string key) => Num(el, key).ToString("F3", CultureInfo.InvariantCulture);

    private static string F3OrDash(JsonElement el, string key) => NumOrNull(el, key) is double d ? d.ToString("F3", CultureInfo.InvariantCulture) : "—";
    private static string F4OrDash(JsonElement el, string key) => NumOrNull(el, key) is double d ? d.ToString("F4", CultureInfo.InvariantCulture) : "—";
    private static string FmtOrDash(double? d, int digits) => d is double v ? v.ToString("F" + digits, CultureInfo.InvariantCulture) : "—";
}
