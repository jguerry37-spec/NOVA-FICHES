using System.Globalization;
using System.Linq;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Module manager "Contrôle de polygonale" - restructuré thème par thème sur un vrai rapport de
/// référence (voir le plan) : page de garde + tableau de révision, objectifs de la mission,
/// déroulé de mission (dates par technique + organigramme d'équipe), référentiels du projet,
/// tiroirs matériel/méthodologie par technique activée (GNSS fait ; Optique/Nivellement à venir),
/// tableaux de contrôle XY/Z + GeoBase, conclusion. Le payload est déjà entièrement calculé côté
/// MainForm.cs (ControlePolygonaleService), ce renderer ne fait que le mettre en page, comme
/// ControlePrecisionRenderer. Les tableaux de points réutilisent TableRenderer.RenderImplantationTable
/// (pagination automatique déjà éprouvée par tous les autres rapports à tableau de points).
/// </summary>
internal static class ControlePolygonaleRenderer
{
    private const double MarginL = 40;
    private const double MarginR = 40;
    private const double MarginTop = 40;
    private static double MarginBottom => LayoutConstants.FooterReservePt;

    // Fiches techniques par appareil (valeurs fixes issues du document de référence) - pas des
    // champs de formulaire, juste sélectionnées par clé depuis le tiroir matériel de chaque
    // technique.
    private static readonly Dictionary<string, (string Label, (string Field, string Value)[] Rows)> GnssDeviceSheets = new()
    {
        ["gs14_gs15"] = ("Leica GS14/GS15", new (string, string)[]
        {
            ("Constellations", "GPS (3 bandes), GLONASS (2 bandes), Galileo, BeiDou, SBAS"),
            ("Nombre satellites max", "60"),
            ("Précision NRTK horizontale/verticale", "8 mm + 0.5 ppm / 15 mm + 0.5 ppm"),
            ("Précision post-traitement horizontale/verticale", "3 mm + 0.1 ppm / 3.5 mm + 0.4 ppm"),
        }),
    };

    private static readonly Dictionary<string, (string Label, (string Field, string Value)[] Rows)> OptiqueDeviceSheets = new()
    {
        ["ts60"] = ("Leica TS 60", new (string, string)[]
        {
            ("Précision angulaire", "0.5″"),
        }),
    };

    private static readonly Dictionary<string, (string Label, (string Field, string Value)[] Rows)> NivellementDeviceSheets = new()
    {
        ["ls15"] = ("Leica LS15", new (string, string)[]
        {
            ("Configuration", "LS 15 - 0.3 + mire invar"),
            ("Précision avec mire Invar", "0.3 mm"),
            ("Mire invar", "Longueur de 2 m"),
        }),
    };

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

        List<(string Name, string Role)> ParsePeople(JsonElement? arrEl)
        {
            var list = new List<(string, string)>();
            if (arrEl != null && arrEl.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arrEl.Value.EnumerateArray())
                {
                    string n = Str(e, "name") ?? "";
                    string r = Str(e, "role") ?? "";
                    if (!string.IsNullOrWhiteSpace(n) || !string.IsNullOrWhiteSpace(r)) list.Add((n, r));
                }
            }
            return list;
        }

        void DrawOrgBox(double cx, double top, double w, double h, string name, string role)
        {
            var rect = new XRect(cx - w / 2.0, top, w, h);
            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), NovatlasTheme.HeaderFillBrush(), rect);
            var nameFont = NovatlasTheme.FontBold(9);
            var roleFont = NovatlasTheme.FontBody(7.5);
            var roleBrush = new XSolidBrush(XColor.FromArgb(255, 110, 110, 110));
            double ny = top + 3;
            foreach (var ln in WrapText(gfx, name, nameFont, w - 6).Take(2))
            {
                gfx.DrawString(ln, nameFont, XBrushes.Black, new XRect(rect.X + 3, ny, w - 6, 11), XStringFormats.Center);
                ny += 11;
            }
            foreach (var ln in WrapText(gfx, role, roleFont, w - 6).Take(2))
            {
                gfx.DrawString(ln, roleFont, roleBrush, new XRect(rect.X + 3, ny, w - 6, 9), XStringFormats.Center);
                ny += 9;
            }
        }

        // Organigramme à 3 niveaux (Responsable → encadrement 1..N → opérateurs/géomètres regroupés
        // dans une case). Ne dessine rien si aucune info d'équipe n'a été saisie (module utilisable
        // sans, comme les autres champs optionnels de ce rapport).
        void DrawOrgChart(JsonElement team)
        {
            string respName = Str(team, "responsibleName") ?? "";
            string respRole = Str(team, "responsibleRole") ?? "";
            var level2 = ParsePeople(GetProp(team, "level2"));
            string surveyorsText = Str(team, "surveyorsText") ?? "";
            string surveyorRole = Str(team, "surveyorRole") ?? "";
            bool hasLevel1 = !string.IsNullOrWhiteSpace(respName);
            bool hasLevel2 = level2.Count > 0;
            bool hasLevel3 = !string.IsNullOrWhiteSpace(surveyorsText) || !string.IsNullOrWhiteSpace(surveyorRole);

            if (!hasLevel1 && !hasLevel2 && !hasLevel3) return;

            int teamSize = (hasLevel1 ? 1 : 0) + level2.Count
                         + surveyorsText.Split(',').Select(s => s.Trim()).Count(s => s.Length > 0);
            DrawWrapped($"Une équipe de {teamSize} opérateur(s) a été missionnée. L'organisation était la suivante :",
                NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 10);

            const double boxH = 42, vGap = 28, hGap = 12, maxBoxW = 170;

            // Seuls les niveaux réellement renseignés occupent de la place - un niveau 2 vide (cas
            // fréquent : juste un responsable seul) ne doit pas laisser un grand vide muet avant la
            // section suivante, contrairement à une mise en page à 3 rangées fixes.
            int levelsCount = (hasLevel1 ? 1 : 0) + (hasLevel2 ? 1 : 0) + (hasLevel3 ? 1 : 0);
            double totalH = levelsCount * boxH + Math.Max(0, levelsCount - 1) * vGap;
            EnsureSpace(totalH + 10);

            double centerX = MarginL + contentW / 2.0;
            double curY = y;
            var prevCenters = new List<double>();
            double prevBottom = curY;

            if (hasLevel1)
            {
                DrawOrgBox(centerX, curY, maxBoxW, boxH, respName, respRole);
                prevCenters = new List<double> { centerX };
                prevBottom = curY + boxH;
                curY += boxH + vGap;
            }

            if (hasLevel2)
            {
                int count = level2.Count;
                double boxW = Math.Min(maxBoxW, (contentW - hGap * (count - 1)) / count);
                double rowW = boxW * count + hGap * (count - 1);
                double left = centerX - rowW / 2.0;
                var centers = new List<double>();
                for (int i = 0; i < count; i++)
                {
                    double bx = left + boxW / 2.0 + i * (boxW + hGap);
                    centers.Add(bx);
                    DrawOrgBox(bx, curY, boxW, boxH, level2[i].Name, level2[i].Role);
                    foreach (var px in prevCenters)
                        gfx.DrawLine(NovatlasTheme.GridPenNormal(), px, prevBottom, bx, curY);
                }
                prevCenters = centers;
                prevBottom = curY + boxH;
                curY += boxH + vGap;
            }

            if (hasLevel3)
            {
                DrawOrgBox(centerX, curY, maxBoxW, boxH, surveyorsText, surveyorRole);
                foreach (var px in prevCenters)
                    gfx.DrawLine(NovatlasTheme.GridPenNormal(), px, prevBottom, centerX, curY);
                curY += boxH;
            }
            else
            {
                curY -= vGap; // dernier niveau dessiné = pas de rangée suivante, annule l'espacement de trop
            }

            y = curY + 10;
        }

        // Fiche technique appareil : bandeau titre (nom du modèle) + tableau libellé/valeur,
        // réutilisée par tous les tiroirs matériel (GNSS ici, Optique/Nivellement plus tard).
        void DrawDeviceSheet(string deviceLabel, (string Field, string Value)[] rows)
        {
            double labelW = contentW * 0.42;
            double valueW = contentW - labelW;
            const double headerH = 22;
            var valueFont = NovatlasTheme.FontBody(9);
            var labelFont = NovatlasTheme.FontBold(9);
            var zebraBrush = new XSolidBrush(XColor.FromArgb(248, 249, 251));

            var wrappedValues = rows.Select(r => WrapText(gfx, r.Value, valueFont, valueW - 8)).ToList();
            var rowHeights = wrappedValues.Select(lines => Math.Max(18.0, lines.Count * 12 + 6)).ToList();
            double totalH = headerH + rowHeights.Sum();
            EnsureSpace(totalH + 6);

            double startY = y;
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, headerH);
            gfx.DrawString(deviceLabel, NovatlasTheme.FontBold(10), XBrushes.Black,
                new XRect(MarginL, y, contentW, headerH), XStringFormats.Center);
            y += headerH;

            for (int i = 0; i < rows.Length; i++)
            {
                double rh = rowHeights[i];
                if (i % 2 == 1) gfx.DrawRectangle(zebraBrush, MarginL, y, contentW, rh);
                gfx.DrawString(rows[i].Field, labelFont, XBrushes.Black,
                    new XRect(MarginL + 4, y + 3, labelW - 8, rh - 6), XStringFormats.TopLeft);
                double vy = y + 3;
                foreach (var ln in wrappedValues[i])
                {
                    gfx.DrawString(ln, valueFont, XBrushes.Black, new XRect(MarginL + labelW + 4, vy, valueW - 8, 12), XStringFormats.TopLeft);
                    vy += 12;
                }
                y += rh;
            }

            double totalDrawnH = y - startY;
            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, startY, contentW, totalDrawnH);
            gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL + labelW, startY + headerH, MarginL + labelW, y);
            double ly = startY + headerH;
            gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL, ly, MarginL + contentW, ly);
            for (int i = 0; i < rows.Length - 1; i++) { ly += rowHeights[i]; gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL, ly, MarginL + contentW, ly); }
            y += 8;
        }

        // Tiroir GNSS : disposition (photo optionnelle) + matériel (fiche appareil) + méthodologie
        // (texte pré-rempli côté JS, modifiable) - la date est déjà affichée dans "Déroulé de la
        // mission", pas répétée ici.
        void DrawGnssDrawer(JsonElement gnss)
        {
            string dispositionImg = Str(gnss, "dispositionImageDataUrl") ?? "";
            if (!string.IsNullOrWhiteSpace(dispositionImg))
            {
                EnsureSpace(18);
                gfx.DrawString("Disposition des couples GNSS", NovatlasTheme.FontBold(11), XBrushes.Black,
                    new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
                y += 20;
                string projectNameForCaption = Str(project, "projectName") ?? "";
                string caption = string.IsNullOrWhiteSpace(projectNameForCaption)
                    ? "La photo ci-dessous montre l'ensemble des couples GNSS mis en place."
                    : $"La photo ci-dessous montre l'ensemble des couples GNSS sur le tracé du projet {projectNameForCaption}.";
                DrawWrapped(caption, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 6);

                const double imgH = 220;
                EnsureSpace(imgH + 10);
                PdfImageHelper.DrawDataUrlImage(gfx, dispositionImg, new XRect(MarginL, y, contentW, imgH));
                y += imgH + 10;
            }

            EnsureSpace(18);
            gfx.DrawString("GNSS : matériel utilisé", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            string deviceKey = Str(gnss, "deviceKey") ?? "gs14_gs15";
            if (!GnssDeviceSheets.TryGetValue(deviceKey, out var sheet))
                sheet = GnssDeviceSheets["gs14_gs15"];
            DrawWrapped($"L'ensemble de ces points a été stationné à l'aide de récepteurs GNSS {sheet.Label} dont les caractéristiques sont détaillées ci-dessous :",
                NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 8);
            DrawDeviceSheet(sheet.Label, sheet.Rows);

            EnsureSpace(18);
            gfx.DrawString("GNSS : méthodologie", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            string methodology = Str(gnss, "methodologyText") ?? "";
            if (!string.IsNullOrWhiteSpace(methodology))
                DrawWrapped(methodology, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);
        }

        // Tiroir Optique : disposition (photo optionnelle) + matériel (fiche appareil) +
        // méthodologie (texte pré-rempli côté JS, modifiable) + "Paramètres de mesure" (bullets
        // fixes + puce PPM dynamique - c'est ici, et non dans les référentiels, que le PPM
        // apparaît dans le document réel).
        void DrawOptiqueDrawer(JsonElement optique)
        {
            string dispositionImg = Str(optique, "dispositionImageDataUrl") ?? "";
            if (!string.IsNullOrWhiteSpace(dispositionImg))
            {
                EnsureSpace(18);
                gfx.DrawString("Disposition des stations topographiques", NovatlasTheme.FontBold(11), XBrushes.Black,
                    new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
                y += 20;
                string projectNameForCaption = Str(project, "projectName") ?? "";
                string caption = string.IsNullOrWhiteSpace(projectNameForCaption)
                    ? "La photo ci-dessous montre l'ensemble des stations topographiques mises en place."
                    : $"La photo ci-dessous montre l'ensemble des stations topographiques sur le tracé du projet {projectNameForCaption}.";
                DrawWrapped(caption, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 6);

                const double imgH = 220;
                EnsureSpace(imgH + 10);
                PdfImageHelper.DrawDataUrlImage(gfx, dispositionImg, new XRect(MarginL, y, contentW, imgH));
                y += imgH + 10;
            }

            EnsureSpace(18);
            gfx.DrawString("Optique : matériel utilisé", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            string deviceKey = Str(optique, "deviceKey") ?? "ts60";
            if (!OptiqueDeviceSheets.TryGetValue(deviceKey, out var sheet))
                sheet = OptiqueDeviceSheets["ts60"];
            DrawWrapped($"L'ensemble de ces points a été stationné à l'aide d'une station {sheet.Label} dont les caractéristiques sont détaillées ci-dessous :",
                NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 8);
            DrawDeviceSheet(sheet.Label, sheet.Rows);

            EnsureSpace(18);
            gfx.DrawString("Optique : méthodologie", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            string methodology = Str(optique, "methodologyText") ?? "";
            if (!string.IsNullOrWhiteSpace(methodology))
                DrawWrapped(methodology, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);

            EnsureSpace(18);
            gfx.DrawString("Paramètres de mesure", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            DrawWrapped("Les mesures respectent également les paramètres conseillés, soit :",
                NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);
            DrawWrapped("•  Mise en station libre.", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
            DrawWrapped("•  Double retournement sur chaque point mesuré.", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
            DrawWrapped("•  Application des informations météorologiques (température, humidité et pression atmosphérique).",
                NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
            bool ppmApplied = GetBool(optique, "ppmApplied") ?? false;
            string ppmValue = Str(optique, "ppmValue") ?? "";
            string ppmBullet = ppmApplied
                ? (string.IsNullOrWhiteSpace(ppmValue) ? "•  Application des PPM." : $"•  Application des PPM (valeur : {ppmValue}).")
                : "•  Pas de correction PPM appliquée.";
            DrawWrapped(ppmBullet, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
        }

        // Tiroir Nivellement : matériel (fiche appareil) + méthodologie (texte pré-rempli côté JS,
        // modifiable). Pas de photo de disposition ni de "Paramètres de mesure" ici - le document
        // de référence n'en a que pour GNSS/Optique.
        void DrawNivellementDrawer(JsonElement niv)
        {
            EnsureSpace(18);
            gfx.DrawString("Nivellement : matériel utilisé", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            string deviceKey = Str(niv, "deviceKey") ?? "ls15";
            if (!NivellementDeviceSheets.TryGetValue(deviceKey, out var sheet))
                sheet = NivellementDeviceSheets["ls15"];
            DrawWrapped($"L'ensemble de ces points a été stationné à l'aide d'un niveau numérique {sheet.Label} avec des mires invar dont les caractéristiques sont détaillées ci-dessous :",
                NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 8);
            DrawDeviceSheet(sheet.Label, sheet.Rows);

            EnsureSpace(18);
            gfx.DrawString("Nivellement : méthodologie", NovatlasTheme.FontBold(11), XBrushes.Black,
                new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 20;
            string methodology = Str(niv, "methodologyText") ?? "";
            if (!string.IsNullOrWhiteSpace(methodology))
                DrawWrapped(methodology, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);
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

        // ===== Objectifs de la mission =====
        // Reprend la structure du document de référence (titre + "La mission concernait :" +
        // puce décrivant l'opération + 2 paragraphes fixes) - seule la puce varie selon le type de
        // rapport (Contrôle/Création) et le niveau (primaire/secondaire), en texte libre forçable
        // (missionObjectiveText) sinon générée automatiquement.
        y += 10;
        EnsureSpace(24);
        gfx.DrawString("Objectifs de la mission", NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 24;
        DrawWrapped("La mission concernait :", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);
        DrawWrapped("•  " + MissionObjectiveText(project, reportType, level), NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 6);
        DrawWrapped("Ces travaux ont été réalisés par des équipes de techniciens topographes qualifiés sous la direction d'un responsable désigné pour cette opération.",
            NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);
        DrawWrapped("Ce rapport précise de manière détaillée le process suivi, et les résultats du contrôle planimétrique et altimétrique des points.",
            NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);

        // ===== Déroulé de la mission =====
        // "Date et durée de la mission" : une ligne par technique activée (GNSS/Nivellement/Optique),
        // au lieu d'une date unique comme en v1 - conforme au document de référence, où chaque
        // technique a sa propre plage d'intervention. "Équipe mobilisée" : organigramme à 3 niveaux
        // (Responsable → encadrement (1..N) → opérateurs/géomètres, noms regroupés dans une case
        // comme le document réel) - portage du principe de l'appli Python de référence, en plus
        // simple (pas de photos de poste, juste nom + rôle par case).
        var techniques = GetProp(root, "techniques") ?? default;
        y += 10;
        EnsureSpace(24);
        gfx.DrawString("Déroulé de la mission", NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 24;

        EnsureSpace(18);
        gfx.DrawString("Date et durée de la mission", NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
        y += 20;

        string missionVerb = reportType == "creation" ? "de mise en place" : "de contrôle";
        DrawWrapped($"L'opération {missionVerb} s'est déroulée sur plusieurs interventions :",
            NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);

        foreach (var (key, label) in new[] { ("gnss", "GNSS"), ("nivellement", "Nivellement"), ("optique", "Optique") })
        {
            var tech = GetProp(techniques, key);
            if (tech == null) continue;
            bool enabled = GetBool(tech.Value, "enabled") ?? false;
            string dateText = Str(tech.Value, "dateText") ?? "";
            if (!enabled || string.IsNullOrWhiteSpace(dateText)) continue;
            DrawWrapped($"{label} : {dateText}", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
        }
        y += 8;

        EnsureSpace(18);
        gfx.DrawString("Équipe mobilisée", NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
        y += 20;

        DrawOrgChart(GetProp(project, "team") ?? default);

        // ===== Référentiels du projet =====
        // Planimétrique = Datum + Projection (2 champs séparés, comme le document de référence -
        // remplace l'ancien champ libre unique `coordSystem`). Le PPM n'est volontairement pas ici :
        // dans le document réel, c'est une puce de "Paramètres de mesure" côté méthodologie Optique
        // (tiroir Optique, thème suivant), pas un référentiel.
        y += 10;
        EnsureSpace(24);
        gfx.DrawString("Référentiels du projet", NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 24;

        EnsureSpace(18);
        gfx.DrawString("Référentiel planimétrique", NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
        y += 20;
        string datum = Str(project, "datum") ?? "";
        string projection = Str(project, "projection") ?? "";
        if (!string.IsNullOrWhiteSpace(datum)) DrawWrapped($"Datum : {datum}", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
        if (!string.IsNullOrWhiteSpace(projection)) DrawWrapped($"Projection : {projection}", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);
        y += 8;

        EnsureSpace(18);
        gfx.DrawString("Référentiel altimétrique", NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
        y += 20;
        string altSystem = Str(project, "altSystem") ?? "";
        if (!string.IsNullOrWhiteSpace(altSystem)) DrawWrapped($"Système altimétrique : {altSystem}", NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 2);

        // ===== Mise en place de la polygonale =====
        // Un tiroir par technique, affiché uniquement si elle est activée pour la mission (même
        // case à cocher que les dates du Déroulé). Le titre commun n'est dessiné qu'une seule fois,
        // avant le premier tiroir présent.
        var gnssTech = GetProp(techniques, "gnss") ?? default;
        bool gnssEnabled = GetBool(gnssTech, "enabled") ?? false;
        var optiqueTech = GetProp(techniques, "optique") ?? default;
        bool optiqueEnabled = GetBool(optiqueTech, "enabled") ?? false;
        var nivTech = GetProp(techniques, "nivellement") ?? default;
        bool nivEnabled = GetBool(nivTech, "enabled") ?? false;

        if (gnssEnabled || optiqueEnabled || nivEnabled)
        {
            y += 10;
            EnsureSpace(24);
            gfx.DrawString("Mise en place de la polygonale", NovatlasTheme.FontBold(13), XBrushes.Black,
                new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;

            if (gnssEnabled) DrawGnssDrawer(gnssTech);
            if (optiqueEnabled) DrawOptiqueDrawer(optiqueTech);
            if (nivEnabled) DrawNivellementDrawer(nivTech);
        }

        gfx.Dispose();

        // ===== RESULTAT =====
        // Se place en fin de rapport (pas juste après la page de garde comme en v1). Deux natures
        // exclusives selon reportType, comme la page de garde et les objectifs :
        // - Contrôle : tableaux d'écarts (Δ) repris tels quels des onglets XY/Z du classeur de
        //   comparaison importé (xySheets/zSheets) - inchangé depuis le v1.
        // - Création : tableaux de coordonnées brutes (N°/X/Y/Z), un par groupe défini côté JS à
        //   partir des préfixes de nom de point sur le GeoBase importé (resultTables) - il n'y a
        //   pas de "théo" à comparer pour des points nouvellement créés.
        var xySheets = GetProp(root, "xySheets");
        var zSheets = GetProp(root, "zSheets");
        var resultTables = GetProp(root, "resultTables");
        bool hasControleResults = reportType != "creation"
            && (((xySheets?.ValueKind == JsonValueKind.Array) && xySheets.Value.GetArrayLength() > 0)
             || ((zSheets?.ValueKind == JsonValueKind.Array) && zSheets.Value.GetArrayLength() > 0));
        bool hasCreationResults = reportType == "creation"
            && resultTables?.ValueKind == JsonValueKind.Array && resultTables.Value.GetArrayLength() > 0;

        if (hasControleResults || hasCreationResults)
        {
            // Pas de page dédiée juste pour le titre "RESULTAT" (resterait presque vide, chaque
            // tableau démarrant de toute façon sa propre page via TableRenderer) - le préfixe est
            // simplement ajouté au titre du tout premier tableau affiché.
            bool firstTable = true;
            string SectionPrefix() { bool f = firstTable; firstTable = false; return f ? "RESULTAT — " : ""; }

            if (hasControleResults)
            {
                if (xySheets?.ValueKind == JsonValueKind.Array)
                    foreach (var sheet in xySheets.Value.EnumerateArray())
                        RenderXySheet(doc, sheet, buildFooter, SectionPrefix());
                if (zSheets?.ValueKind == JsonValueKind.Array)
                    foreach (var sheet in zSheets.Value.EnumerateArray())
                        RenderZSheet(doc, sheet, buildFooter, SectionPrefix());
            }
            else
            {
                foreach (var table in resultTables!.Value.EnumerateArray())
                    RenderRawResultTable(doc, table, buildFooter, SectionPrefix());
            }
        }

        // ===== GeoBase (annexe) =====
        // Réservé au mode Contrôle : en Création, le GeoBase est déjà la source des tableaux
        // RESULTAT ci-dessus - le répéter ici en annexe serait redondant.
        var geobase = GetProp(root, "geobase");
        if (reportType != "creation" && geobase != null && geobase.Value.ValueKind == JsonValueKind.Array && geobase.Value.GetArrayLength() > 0)
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
            : BuildAutoConclusion(project, xySheets, zSheets, resultTables, reportType, level);
        DrawWrapped(conclusion, NovatlasTheme.FontBody(10), XBrushes.Black, XStringFormats.TopLeft, 4);

        gfx.Dispose();
    }

    private static void RenderXySheet(PdfDocument doc, JsonElement sheet, string buildFooter, string sectionPrefix = "")
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
            Title = $"{sectionPrefix}Contrôle GNSS/optique — {name}",
            SubTitle = XySummaryText(stats),
            Header = new[] { "Point", "X théo", "Y théo", "X ctrl", "Y ctrl", "ΔX", "ΔY", "ΔXY" },
            Rows = rows
        };
        var layout = new TableRenderer.TableLayout { ColumnWidths = new double[] { 70, 68, 68, 68, 68, 55, 55, 63 } };
        TableRenderer.RenderImplantationTable(doc, payload, layout, buildFooter);
    }

    private static void RenderZSheet(PdfDocument doc, JsonElement sheet, string buildFooter, string sectionPrefix = "")
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
            Title = $"{sectionPrefix}Contrôle altimétrique — {name}",
            SubTitle = ZSummaryText(stats),
            Header = new[] { "Point", "Z théo", "Z ctrl", "ΔZ" },
            Rows = rows
        };
        var layout = new TableRenderer.TableLayout { ColumnWidths = new double[] { 148, 148, 148, 71 } };
        TableRenderer.RenderImplantationTable(doc, payload, layout, buildFooter);
    }

    // RESULTAT en mode Création : coordonnées brutes (pas d'écart, rien à comparer pour un point
    // nouvellement créé), un tableau par groupe de points défini côté JS (préfixe de nom).
    private static void RenderRawResultTable(PdfDocument doc, JsonElement table, string buildFooter, string sectionPrefix = "")
    {
        string title = sectionPrefix + (Str(table, "title") ?? "Coordonnées");
        var rowsEl = GetProp(table, "rows");

        var rows = new List<string[]>();
        if (rowsEl != null && rowsEl.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rowsEl.Value.EnumerateArray())
            {
                rows.Add(new[]
                {
                    Str(r, "point") ?? "",
                    F3(r, "x"),
                    F3(r, "y"),
                    F3(r, "z"),
                });
            }
        }

        var payload = new ImplantationTablePayload
        {
            Title = title,
            SubTitle = $"{rows.Count} point(s).",
            Header = new[] { "Point", "X", "Y", "Z" },
            Rows = rows
        };
        var layout = new TableRenderer.TableLayout { ColumnWidths = new double[] { 130, 130, 130, 125 } };
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

    // Puce "Objectifs de la mission" générée automatiquement quand l'utilisateur n'a pas forcé de
    // texte. Le verbe suit le type de rapport (mise en place = Création, contrôle = Contrôle),
    // comme le titre de la page de garde. "sur le tracé du projet X" (plutôt que "du X") évite de
    // devoir déterminer le genre grammatical d'un nom de projet libre.
    private static string MissionObjectiveText(JsonElement project, string reportType, string level)
    {
        var forced = Str(project, "missionObjectiveText");
        if (!string.IsNullOrWhiteSpace(forced)) return forced!;

        string verbPhrase = reportType == "creation" ? "La mise en place de" : "Le contrôle de";
        string ville = Str(project, "ville") ?? "";
        string projectName = Str(project, "projectName") ?? "";
        string lieu = string.IsNullOrWhiteSpace(ville) ? "" : $" sur la ville de {ville}";
        string trace = string.IsNullOrWhiteSpace(projectName) ? "" : $", sur le tracé du projet {projectName}";
        return $"{verbPhrase} la polygonale {level}{lieu}{trace}.";
    }

    // Conclusion générée quand l'utilisateur n'a pas forcé de texte - adaptée au type de rapport
    // (Contrôle/Création) et au niveau (primaire/secondaire), comme la page de garde et les
    // objectifs. Reprend la structure du document de référence : phrase d'intro (même schéma que
    // MissionObjectiveText), puis soit les écarts max (Contrôle) soit un point par groupe RESULTAT
    // (Création, ex. "Les cibles : 23 point(s)." - la formulation neutre évite de devoir deviner un
    // accord grammatical pour un nom de groupe libre, contrairement au document réel qui accorde
    // "mises"/"placés" au cas par cas), et enfin la signature du responsable (nom — rôle), comme le
    // document réel qui se termine par "Julien GUERRY — Responsable topographique".
    private static string BuildAutoConclusion(JsonElement project, JsonElement? xySheets, JsonElement? zSheets, JsonElement? resultTables, string reportType, string level)
    {
        var parts = new List<string>();

        string verbNounPhrase = reportType == "creation" ? "de la mise en place de" : "du contrôle de";
        string projectName = Str(project, "projectName") ?? "";
        string ville = Str(project, "ville") ?? "";
        string trace = string.IsNullOrWhiteSpace(projectName) ? "" : $" du chantier du projet {projectName}";
        string lieu = string.IsNullOrWhiteSpace(ville) ? "" : $" sur la ville de {ville}";
        parts.Add($"Ce rapport rend compte {verbNounPhrase} la polygonale {level}{trace}{lieu}.");

        if (reportType == "creation")
        {
            if (resultTables != null && resultTables.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var table in resultTables.Value.EnumerateArray())
                {
                    string title = Str(table, "title") ?? "";
                    var rowsEl = GetProp(table, "rows");
                    int count = rowsEl?.ValueKind == JsonValueKind.Array ? rowsEl.Value.GetArrayLength() : 0;
                    if (!string.IsNullOrWhiteSpace(title) && count > 0)
                        parts.Add($"{title} : {count} point(s).");
                }
            }
            parts.Add("L'ensemble des calculs effectués ne présente aucune anomalie.");
        }
        else
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

            if (hasXy) parts.Add($"Les contrôles planimétriques présentent un écart maximal de {maxXy.ToString("F4", CultureInfo.InvariantCulture)} m.");
            if (hasZ) parts.Add($"Les contrôles altimétriques présentent un écart maximal de {maxZ.ToString("F4", CultureInfo.InvariantCulture)} m.");
            parts.Add("Les résultats sont à apprécier au regard des tolérances et des conditions d'intervention.");
            parts.Add("Une validation par la maîtrise d'œuvre reste nécessaire lorsque les points doivent être considérés comme références géométriques officielles.");
        }

        var team = GetProp(project, "team");
        string respName = team != null ? Str(team.Value, "responsibleName") ?? "" : "";
        string respRole = team != null ? Str(team.Value, "responsibleRole") ?? "" : "";
        if (!string.IsNullOrWhiteSpace(respName))
        {
            parts.Add("");
            parts.Add(string.IsNullOrWhiteSpace(respRole) ? respName : $"{respName} — {respRole}");
        }

        return string.Join("\n", parts);
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
