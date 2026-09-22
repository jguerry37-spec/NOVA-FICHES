using System.Globalization;
using System.IO;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Module manager "Contrôle classe de précision" : page de garde, paramètres/résumé, conditions
/// réglementaires, conclusion globale, barycentres, histogrammes, courbes de Gauss, scatterplot,
/// tableau détaillé des points, annexe texte (arrêté du 16 septembre 2003). Le payload est déjà
/// entièrement calculé côté MainForm.cs (ControlePrecisionService.Analyze) - ce renderer ne fait
/// que le mettre en page, comme FicheSignaletiqueRenderer/StationPlanRenderer (référence à sens
/// unique NovaFiches -&gt; PdfSharpEngine : ControlePrecisionService, qui vit dans NovaFiches,
/// n'est pas accessible ici). La carte de situation et les 6 graphiques experts (vecteurs,
/// boxplot, CDF, rose des vents, QQ-plot, lollipop) sont différés en Phase 3 (voir le plan).
///
/// Toutes les fonctions de dessin qui peuvent déclencher un saut de page (EnsureSpace/NewPage)
/// sont des fonctions locales à Render plutôt que des méthodes statiques prenant XGraphics en
/// paramètre : un saut de page dispose l'ancien XGraphics et en recrée un nouveau sur la nouvelle
/// page, donc toute méthode qui recevrait "gfx" par simple paramètre continuerait de dessiner sur
/// l'objet disposé après le saut. Les fonctions locales referment directement sur les variables
/// gfx/page/y de Render, qui sont donc toujours à jour.
/// </summary>
internal static class ControlePrecisionRenderer
{
    private const double MarginL = 40;
    private const double MarginR = 40;
    private const double MarginTop = 40;
    private static double MarginBottom => LayoutConstants.FooterReservePt;

    private static readonly XColor OkGreen = XColor.FromArgb(21, 128, 61);
    private static readonly XColor FailRed = XColor.FromArgb(185, 28, 28);

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

        // Numérotation continue des sections principales du rapport (1. Paramètres, 2. Conditions,
        // ... jusqu'à 12. Analyses Visuelles), comme le compteur sectionCounter de l'outil de
        // référence. Les sous-titres des 6 graphiques experts (1. Carte des vecteurs, 2. Boxplot,
        // ...) restent une numérotation locale à part, non liée à ce compteur.
        int sectionCounter = 1;
        string SecTitle(string title) => $"{sectionCounter++}. {title}";

        double DrawWrapped(string text, XFont font, XBrush brush, XStringFormat format, double extraGapAfter = 4)
        {
            if (string.IsNullOrWhiteSpace(text)) return y;
            var lines = WrapText(gfx, text, font, contentW);
            // Interligne aéré (1.3->1.5) - retour utilisateur : le texte (notamment l'Annexe)
            // restait trop dense, laissant de grands blocs de blanc en fin de page au lieu
            // d'occuper l'espace disponible plus régulièrement.
            double lineH = font.GetHeight() * 1.5;
            foreach (var line in lines)
            {
                EnsureSpace(lineH);
                gfx.DrawString(line, font, brush, new XRect(MarginL, y, contentW, lineH), format);
                y += lineH;
            }
            y += extraGapAfter;
            return y;
        }

        // Réduit progressivement la taille de police jusqu'à ce que le texte tienne dans la
        // largeur donnée, plutôt que de le laisser déborder par-dessus la colonne voisine (bug
        // constaté avec des libellés longs comme "Moyenne écart planimétrique (points
        // sélectionnés)" qui se superposaient à la valeur) - même principe que
        // TableRenderer.DrawCellTextFit pour la colonne ID.
        void DrawTextFit(string text, XRect rect, XFont baseFont, XStringFormat format)
        {
            double fs = baseFont.Size;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var f = new XFont("Arial", fs, baseFont.Style);
                if (gfx.MeasureString(text, f).Width <= rect.Width || fs <= 6.5)
                {
                    gfx.DrawString(text, f, XBrushes.Black, rect, format);
                    return;
                }
                fs -= 0.5;
            }
        }

        void DrawInfoTable(string? title, (string Label, string? Value)[] rows, double width, (string Left, string Right)? colHeaders = null)
        {
            // labelW plus large + rowH plus généreux que le format initial : la référence laisse
            // beaucoup plus d'air par ligne (constaté visuellement page par page), et les libellés
            // longs ("Moyenne écart planimétrique (points sélectionnés)") avaient besoin de plus de
            // place pour ne plus déborder sur la valeur même après réduction de police.
            double labelW = Math.Min(260, width * 0.5);
            double valueW = width - labelW;
            // Hauteur de ligne relevée (20->23) - retour utilisateur : les cellules restaient trop
            // serrées sur l'ensemble des tableaux du rapport.
            const double rowH = 23;
            var zebraBrush = new XSolidBrush(XColor.FromArgb(248, 249, 251));

            if (title != null)
            {
                EnsureSpace(rowH);
                gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, width, rowH);
                gfx.DrawString(title, NovatlasTheme.FontBold(10), XBrushes.Black, new XRect(MarginL + 4, y, width - 8, rowH), XStringFormats.CenterLeft);
                y += rowH;
            }

            // En-tête à 2 colonnes ("Paramètre | Valeur", "Statistique | Valeur") - comme les
            // tables autoTable de la référence, qui affichent toujours un vrai en-tête de colonnes
            // (distinct du titre de section qui, lui, s'affiche au-dessus sans être découpé).
            if (colHeaders != null)
            {
                EnsureSpace(rowH);
                gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, width, rowH);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL + labelW, y, MarginL + labelW, y + rowH);
                gfx.DrawString(colHeaders.Value.Left, NovatlasTheme.FontBold(9.5), XBrushes.Black, new XRect(MarginL + 5, y, labelW - 9, rowH), XStringFormats.CenterLeft);
                gfx.DrawString(colHeaders.Value.Right, NovatlasTheme.FontBold(9.5), XBrushes.Black, new XRect(MarginL + labelW + 5, y, valueW - 9, rowH), XStringFormats.CenterLeft);
                y += rowH;
            }

            int idx = 0;
            foreach (var (label, value) in rows)
            {
                EnsureSpace(rowH);
                if (idx % 2 == 1) gfx.DrawRectangle(zebraBrush, MarginL, y, width, rowH);
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y, width, rowH);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL + labelW, y, MarginL + labelW, y + rowH);
                DrawTextFit(label, new XRect(MarginL + 5, y, labelW - 9, rowH), NovatlasTheme.FontBodyBold(9.5), XStringFormats.CenterLeft);
                gfx.DrawString(string.IsNullOrWhiteSpace(value) ? "N/A" : value, NovatlasTheme.FontBodyItalic(9.5), XBrushes.Black,
                    new XRect(MarginL + labelW + 5, y, valueW - 9, rowH), XStringFormats.CenterLeft);
                y += rowH;
                idx++;
            }
            y += 10;
        }

        void DrawConditionTable(string title, JsonElement? condOpt, string unit)
        {
            if (condOpt == null) return;
            var c = condOpt.Value;
            if (!(GetBool(c, "applicable") ?? false)) return;

            const double rowH = 23;
            EnsureSpace(rowH * 4 + 10);
            var zebraBrush = new XSolidBrush(XColor.FromArgb(248, 249, 251));

            bool overallOk = GetBool(c, "overallOk") ?? false;
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
            gfx.DrawString(title, NovatlasTheme.FontBold(10), XBrushes.Black, new XRect(MarginL + 4, y, contentW * 0.6, rowH), XStringFormats.CenterLeft);
            gfx.DrawString(overallOk ? "CONFORME" : "NON CONFORME", NovatlasTheme.FontBold(10),
                new XSolidBrush(overallOk ? OkGreen : FailRed), new XRect(MarginL, y, contentW - 4, rowH), XStringFormats.CenterRight);
            y += rowH;

            int rowIdx = 0;
            void Row(string label, string detail, bool ok)
            {
                EnsureSpace(rowH);
                if (rowIdx % 2 == 1) gfx.DrawRectangle(zebraBrush, MarginL, y, contentW, rowH);
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y, contentW, rowH);
                gfx.DrawString(label, NovatlasTheme.FontBodyItalic(9.5), XBrushes.Black, new XRect(MarginL + 5, y, contentW * 0.3, rowH), XStringFormats.CenterLeft);
                gfx.DrawString(detail, NovatlasTheme.FontBodyItalic(9.5), XBrushes.Black, new XRect(MarginL + contentW * 0.3, y, contentW * 0.5, rowH), XStringFormats.CenterLeft);
                gfx.DrawString(ok ? "OK" : "ÉCHEC", NovatlasTheme.FontBodyBold(9.5), new XSolidBrush(ok ? OkGreen : FailRed),
                    new XRect(MarginL, y, contentW - 5, rowH), XStringFormats.CenterRight);
                y += rowH;
                rowIdx++;
            }

            Row("Condition 1 (Moyenne)", $"{F3v(c, "meanDev")}{unit} <= {F3v(c, "seuilS1")}{unit}", GetBool(c, "cond1Ok") ?? false);
            Row("Condition 2 (Nb > T1)", $"{IntStr(c, "countOverT1")} <= {IntStr(c, "nPrimeMax")} (T1={F3v(c, "seuilT1")}{unit})", GetBool(c, "cond2Ok") ?? false);
            Row("Condition 3 (Nb > T2)", $"{IntStr(c, "countOverT2")} == 0 (T2={F3v(c, "seuilT2")}{unit})", GetBool(c, "cond3Ok") ?? false);
            y += 8;
        }

        // Table à barres horizontales (histogramme) : 1 colonne "Plage" + 3 colonnes 1D/2D/3D,
        // largeur de barre proportionnelle à la plus grande valeur toutes séries confondues.
        void DrawHistogramTable(string title, string[] labels, double[] v1D, double[] v2D, double[] v3D, Func<double, string> fmt, (string Label, string V1, string V2, string V3)[]? footerRows = null)
        {
            gfx.DrawString(title, NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 26;

            const double rowH = 16;
            double labelW = 55;
            double barColW = (contentW - labelW) / 3.0;

            EnsureSpace(rowH);
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
            gfx.DrawString("Plage (mm)", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + 2, y, labelW - 4, rowH), XStringFormats.CenterLeft);
            gfx.DrawString("1D", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + labelW, y, barColW, rowH), XStringFormats.Center);
            gfx.DrawString("2D", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + labelW + barColW, y, barColW, rowH), XStringFormats.Center);
            gfx.DrawString("3D", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + labelW + 2 * barColW, y, barColW, rowH), XStringFormats.Center);
            y += rowH;

            double maxV = 0;
            foreach (var v in v1D) maxV = Math.Max(maxV, v);
            foreach (var v in v2D) maxV = Math.Max(maxV, v);
            foreach (var v in v3D) maxV = Math.Max(maxV, v);
            if (maxV <= 0) maxV = 1;

            void Bar(double x, double value, XColor color)
            {
                const double padY = 2, padX = 2, numW = 26;
                double barMaxW = barColW - padX * 2 - numW;
                double barW = Math.Max(0, (value / maxV) * barMaxW);
                if (value > 0 && barW < 1.5) barW = 1.5;
                if (barW > 0) gfx.DrawRectangle(new XSolidBrush(color), x + padX, y + padY, barW, rowH - padY * 2);
                gfx.DrawString(fmt(value), NovatlasTheme.FontBody(7), XBrushes.Black,
                    new XRect(x + padX + barMaxW + 2, y, numW, rowH), XStringFormats.CenterLeft);
            }

            for (int i = 0; i < labels.Length; i++)
            {
                EnsureSpace(rowH);
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y, contentW, rowH);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL + labelW, y, MarginL + labelW, y + rowH);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), MarginL + labelW + barColW, y, MarginL + labelW + barColW, y + rowH);
                gfx.DrawString(labels[i], NovatlasTheme.FontBody(7), XBrushes.Black, new XRect(MarginL + 2, y, labelW - 4, rowH), XStringFormats.CenterLeft);
                Bar(MarginL + labelW, i < v1D.Length ? v1D[i] : 0, XColor.FromArgb(129, 140, 248));
                Bar(MarginL + labelW + barColW, i < v2D.Length ? v2D[i] : 0, XColor.FromArgb(96, 165, 250));
                Bar(MarginL + labelW + 2 * barColW, i < v3D.Length ? v3D[i] : 0, XColor.FromArgb(52, 211, 153));
                y += rowH;
            }

            if (footerRows != null)
            {
                double colW = (contentW - labelW) / 3.0;
                foreach (var (label, v1, v2, v3) in footerRows)
                {
                    EnsureSpace(rowH);
                    gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
                    gfx.DrawString(label, NovatlasTheme.FontBodyBold(7), XBrushes.Black, new XRect(MarginL + 2, y, labelW - 4, rowH), XStringFormats.CenterLeft);
                    gfx.DrawString(v1, NovatlasTheme.FontBodyBold(7), XBrushes.Black, new XRect(MarginL + labelW, y, colW, rowH), XStringFormats.Center);
                    gfx.DrawString(v2, NovatlasTheme.FontBodyBold(7), XBrushes.Black, new XRect(MarginL + labelW + colW, y, colW, rowH), XStringFormats.Center);
                    gfx.DrawString(v3, NovatlasTheme.FontBodyBold(7), XBrushes.Black, new XRect(MarginL + labelW + 2 * colW, y, colW, rowH), XStringFormats.Center);
                    y += rowH;
                }
            }
            y += 8;
        }

        // Courbe de Gauss théorique (vectorielle) avec lignes de seuil S1/T1/T2 et moyenne. La
        // référence dessine les 3 courbes (1D/2D/3D) sur UNE seule page - contrairement à la
        // version précédente qui forçait un saut de page par courbe (chartH=180 + tableau
        // récapitulatif 4 lignes ne tenaient de toute façon pas 3x sur une A4). Le graphique est
        // donc réduit et le récapitulatif compacté en une seule ligne de texte pour que les 3
        // tiennent ensemble ; l'appelant ne force plus qu'UN SEUL saut de page avant la première
        // courbe (EnsureSpace gère naturellement un débordement si les données sont atypiques).
        void DrawGaussianPage(string title, JsonElement? gOpt, string axisLabel)
        {
            if (gOpt == null) return;
            var g = gOpt.Value;
            if (!(GetBool(g, "applicable") ?? false)) return;
            int n = (int)Num(g, "n");
            double mean = Num(g, "meanCm");
            double stdDev = Num(g, "stdDevCm");
            if (n < 2 || stdDev <= 0) return;

            const double chartH = 105;
            EnsureSpace(chartH + 55);
            gfx.DrawString(title, NovatlasTheme.FontBold(11), XBrushes.Black, new XRect(MarginL, y, contentW, 16), XStringFormats.TopLeft);
            y += 18;

            double ciLower = Num(g, "ciLowerCm"), ciUpper = Num(g, "ciUpperCm");
            double s1 = Num(g, "s1Cm"), t1 = Num(g, "t1Cm"), t2 = Num(g, "t2Cm");

            var domain = new List<double> { mean - 3.5 * stdDev, mean + 3.5 * stdDev, mean, ciLower, ciUpper };
            if (s1 > 0) domain.Add(s1);
            if (t1 > 0) domain.Add(t1);
            if (t2 > 0) domain.Add(t2);
            double xMin = domain.Min() - 0.5 * stdDev;
            double xMax = domain.Max() + 0.5 * stdDev;

            double chartX = MarginL, chartY = y;

            double XScale(double v) => chartX + (v - xMin) / (xMax - xMin) * contentW;

            gfx.DrawLine(NovatlasTheme.GridPenThin(), chartX, chartY + chartH, chartX + contentW, chartY + chartH);
            const int tickCount = 7;
            for (int i = 0; i <= tickCount; i++)
            {
                double v = xMin + (xMax - xMin) * i / tickCount;
                double px = XScale(v);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), px, chartY + chartH, px, chartY + chartH + 3);
                gfx.DrawString(v.ToString("0.0", CultureInfo.InvariantCulture), NovatlasTheme.FontBody(7), XBrushes.Black,
                    new XRect(px - 15, chartY + chartH + 4, 30, 10), XStringFormats.Center);
            }
            gfx.DrawString(axisLabel, NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(chartX, chartY + chartH + 16, contentW, 12), XStringFormats.Center);

            double GaussPdf(double x) => (1.0 / (stdDev * Math.Sqrt(2 * Math.PI))) * Math.Exp(-0.5 * Math.Pow((x - mean) / stdDev, 2));
            double yMaxDensity = GaussPdf(mean);
            double YScale(double density) => chartY + chartH - (density / yMaxDensity) * (chartH - 10);

            const int steps = 100;
            var pts = new XPoint[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double xv = xMin + (xMax - xMin) * i / steps;
                pts[i] = new XPoint(XScale(xv), YScale(GaussPdf(xv)));
            }
            gfx.DrawLines(new XPen(NovatlasTheme.ResolveBlue(root), 1.5), pts);

            void ThresholdLine(double v, XColor color, string label)
            {
                if (v <= 0) return;
                double px = XScale(v);
                if (px < chartX || px > chartX + contentW) return;
                gfx.DrawLine(new XPen(color, 1) { DashStyle = XDashStyle.Dash }, px, chartY, px, chartY + chartH);
                gfx.DrawString(label, NovatlasTheme.FontBody(7), new XSolidBrush(color), new XRect(px + 2, chartY, 40, 10), XStringFormats.TopLeft);
            }
            ThresholdLine(s1, XColor.FromArgb(34, 197, 94), "S1");
            ThresholdLine(t1, XColor.FromArgb(249, 115, 22), "T1");
            ThresholdLine(t2, XColor.FromArgb(239, 68, 68), "T2");

            double meanPx = XScale(mean);
            gfx.DrawLine(new XPen(XColors.Blue, 1) { DashStyle = XDashStyle.Dash }, meanPx, chartY, meanPx, chartY + chartH);

            y = chartY + chartH + 32;
            string summary = $"Moyenne {mean:F1} cm — IC95% [{ciLower:F1} ; {ciUpper:F1}] cm — Seuils S1/T1/T2 : {s1:F1} / {t1:F1} / {t2:F1} cm — N = {n}";
            gfx.DrawString(summary, NovatlasTheme.FontBodyItalic(8), XBrushes.Black, new XRect(MarginL, y, contentW, 12), XStringFormats.TopLeft);
            y += 20;
        }

        // Nuage de points global (tous les points Levé, appariés en vert / non appariés en noir),
        // échelle X/Y identique pour ne pas déformer la répartition spatiale réelle.
        void DrawScatterplotPage(string title, JsonElement scatterArr)
        {
            var pts = new List<(double X, double Y, bool Matched)>();
            foreach (var p in scatterArr.EnumerateArray())
                pts.Add((Num(p, "x"), Num(p, "y"), GetBool(p, "matched") ?? false));
            if (pts.Count == 0) return;

            NewPage();
            gfx.DrawString(title, NovatlasTheme.FontBold(13), XBrushes.Black,
                new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 28;

            const double chartH = 320;
            EnsureSpace(chartH + 30);
            double chartX = MarginL, chartY = y;

            // Cadrage résistant aux valeurs aberrantes (clôture de Tukey, facteur large x5) plutôt
            // qu'un simple min/max brut : un fichier Levé de plusieurs milliers de points importé
            // tel quel contient parfois une poignée de lignes mal formées côté source (ex. un champ
            // "code" à plusieurs valeurs séparées par ";" dont un fragment numérique se fait lire
            // comme coordonnée) - une seule valeur ainsi complètement fausse suffisait à écraser
            // l'échelle et à rendre tout le nuage réel invisible (retour utilisateur : plan quasi
            // vide). Les points hors clôture restent dessinés, juste épinglés au bord du cadre
            // plutôt que de dicter l'échelle.
            static (double lo, double hi) RobustBounds(List<double> values)
            {
                var sorted = values.OrderBy(v => v).ToList();
                double Percentile(double p)
                {
                    double pos = (sorted.Count - 1) * p;
                    int baseIdx = (int)Math.Floor(pos);
                    double rest = pos - baseIdx;
                    return baseIdx + 1 < sorted.Count ? sorted[baseIdx] + rest * (sorted[baseIdx + 1] - sorted[baseIdx]) : sorted[baseIdx];
                }
                double q1 = Percentile(0.25), q3 = Percentile(0.75);
                double iqr = q3 - q1;
                if (iqr <= 0) return (sorted[0], sorted[^1]);
                return (Math.Max(sorted[0], q1 - 5 * iqr), Math.Min(sorted[^1], q3 + 5 * iqr));
            }

            var (xMin, xMax) = RobustBounds(pts.Select(p => p.X).ToList());
            var (yMin, yMax) = RobustBounds(pts.Select(p => p.Y).ToList());
            double xPad = (xMax - xMin) * 0.05; if (xPad <= 0) xPad = 1;
            double yPad = (yMax - yMin) * 0.05; if (yPad <= 0) yPad = 1;
            xMin -= xPad; xMax += xPad; yMin -= yPad; yMax += yPad;

            double dataW = xMax - xMin, dataH = yMax - yMin;
            double scale = Math.Min(contentW / dataW, chartH / dataH);
            double usedW = dataW * scale, usedH = dataH * scale;
            double offX = chartX + (contentW - usedW) / 2.0;
            double offY = chartY + (chartH - usedH) / 2.0;

            double XScale(double v) => offX + (Math.Clamp(v, xMin, xMax) - xMin) * scale;
            double YScale(double v) => offY + usedH - (Math.Clamp(v, yMin, yMax) - yMin) * scale;

            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), chartX, chartY, contentW, chartH);

            // Points non appariés ("Levé seul") volontairement plus petits et gris clair (au lieu
            // de noir plein) : à taille égale ils écrasaient visuellement les points appariés
            // (verts), rendant le nuage illisible.
            foreach (var p in pts)
            {
                double px = XScale(p.X), py = YScale(p.Y);
                double r = p.Matched ? 2.2 : 0.7;
                var brush = p.Matched ? new XSolidBrush(XColor.FromArgb(16, 185, 129)) : new XSolidBrush(XColor.FromArgb(156, 163, 175));
                gfx.DrawEllipse(brush, px - r, py - r, r * 2, r * 2);
            }

            y = chartY + chartH + 10;
            gfx.DrawEllipse(new XSolidBrush(XColors.Black), MarginL, y + 2, 4, 4);
            gfx.DrawString("Levé seul", NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(MarginL + 8, y, 80, 10), XStringFormats.CenterLeft);
            gfx.DrawEllipse(new XSolidBrush(XColor.FromArgb(16, 185, 129)), MarginL + 90, y + 2, 4, 4);
            gfx.DrawString("Apparié", NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(MarginL + 98, y, 80, 10), XStringFormats.CenterLeft);
            y += 20;
        }

        // Tableau "Analyse Mathématique des Écarts" : Min/Max/Moyenne/Médiane/Écart-Type/Écart
        // Moyen Abs./IC 95% Inf./Sup. pour chacune des 6 dimensions (dX/dY/dZ/Plani/Alti/3D) -
        // mêmes CpStats déjà calculées côté MainForm.cs (ControlePrecisionService.CalculateStats),
        // simplement mises en page ici en tableau à 7 colonnes.
        void DrawMathAnalysisTable(string title, JsonElement? mathStatsEl)
        {
            if (mathStatsEl == null) return;
            var ms = mathStatsEl.Value;
            string[] dims = { "dX", "dY", "dZ", "Plani.", "Alti.", "3D" };
            string[] keys = { "dX", "dY", "dZ", "plani", "alti", "threeD" };
            var stats = keys.Select(k => GetProp(ms, k)).ToArray();
            if (stats.Any(s => s == null)) return;

            NewPage();
            gfx.DrawString(title, NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 26;

            const double rowH = 18;
            double labelW = 90;
            double colW = (contentW - labelW) / 6.0;

            EnsureSpace(rowH);
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
            gfx.DrawString("Statistique", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + 2, y, labelW - 4, rowH), XStringFormats.CenterLeft);
            for (int c = 0; c < dims.Length; c++)
                gfx.DrawString(dims[c], NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + labelW + c * colW, y, colW, rowH), XStringFormats.Center);
            y += rowH;

            void Row(string label, string key)
            {
                EnsureSpace(rowH);
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y, contentW, rowH);
                gfx.DrawString(label, NovatlasTheme.FontBodyBold(8), XBrushes.Black, new XRect(MarginL + 2, y, labelW - 4, rowH), XStringFormats.CenterLeft);
                for (int c = 0; c < stats.Length; c++)
                    gfx.DrawString(F3v(stats[c]!.Value, key), NovatlasTheme.FontBody(8), XBrushes.Black,
                        new XRect(MarginL + labelW + c * colW, y, colW, rowH), XStringFormats.Center);
                y += rowH;
            }

            Row("Min", "min");
            Row("Max", "max");
            Row("Moyenne", "mean");
            Row("Médiane", "median");
            Row("Écart-Type", "stdDev");
            Row("Écart Moyen Abs.", "meanAbs");
            Row("IC 95% Inf.", "ciLower");
            Row("IC 95% Sup.", "ciUpper");
            y += 8;
        }

        // Encart texte à bordure colorée (pédagogie / aide à l'interprétation / conclusion),
        // utilisé par les 6 graphiques experts - reprend l'esprit visuel des .expert-peda-box /
        // .expert-conc-box de l'outil de référence en vectoriel PdfSharp.
        void DrawTextBox(string label, string text, XColor borderColor, XColor bgColor)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = WrapText(gfx, text, NovatlasTheme.FontBody(8), contentW - 16);
            const double lineH = 10;
            double boxH = lines.Count * lineH + (string.IsNullOrEmpty(label) ? 10 : 20);
            EnsureSpace(boxH + 8);
            gfx.DrawRectangle(new XSolidBrush(bgColor), MarginL, y, contentW, boxH);
            gfx.DrawRectangle(new XPen(borderColor, 2.5), MarginL, y, 2.5, boxH);
            double ty = y + 5;
            if (!string.IsNullOrEmpty(label))
            {
                gfx.DrawString(label, NovatlasTheme.FontBold(7), new XSolidBrush(borderColor), new XRect(MarginL + 8, ty, contentW - 16, 10), XStringFormats.TopLeft);
                ty += 11;
            }
            foreach (var line in lines)
            {
                gfx.DrawString(line, NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(MarginL + 8, ty, contentW - 16, lineH), XStringFormats.TopLeft);
                ty += lineH;
            }
            y += boxH + 8;
        }

        var pedaColor = XColor.FromArgb(14, 165, 233); var pedaBg = XColor.FromArgb(240, 249, 255);
        var concColor = XColor.FromArgb(34, 197, 94); var concBg = XColor.FromArgb(240, 253, 244);
        var conclColor = XColor.FromArgb(71, 85, 105); var conclBg = XColor.FromArgb(248, 250, 252);

        // ===== 1. Carte des vecteurs d'écarts planimétriques =====
        // newPage=false pour le tout premier graphique expert : la référence le fait suivre
        // directement l'intro de la section "12. Analyses Visuelles..." sur la même page, plutôt
        // que de forcer un saut de page (seuls les graphiques suivants en forcent un).
        void DrawExpertVectorPlot(JsonElement[] pts, bool newPage = true)
        {
            if (pts.Length == 0) return;
            if (newPage) NewPage(); else EnsureSpace(60);
            gfx.DrawString("1. Carte des Vecteurs d'Écarts Planimétriques", NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped("Visualisation spatiale de l'orientation et de l'amplitude des écarts planimétriques exagérés.", NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);
            DrawTextBox("APPROCHE PÉDAGOGIQUE", "Cette carte amplifie volontairement l'amplitude des vecteurs d'erreur pour les rendre lisibles à l'échelle du chantier. Elle est utile pour détecter les erreurs systématiques : si une majorité de flèches pointe dans le même azimut, cela signale un probable défaut de calage (translation) du levé par rapport aux points de contrôle.", pedaColor, pedaBg);

            var cxs = pts.Select(p => Num(p, "controleX")).ToList();
            var cys = pts.Select(p => Num(p, "controleY")).ToList();
            var dxs = pts.Select(p => Num(p, "dX")).ToList();
            var dys = pts.Select(p => Num(p, "dY")).ToList();

            double xMin = cxs.Min(), xMax = cxs.Max(), yMin = cys.Min(), yMax = cys.Max();
            double xPad = (xMax - xMin) * 0.05; if (xPad <= 0) xPad = 1;
            double yPad = (yMax - yMin) * 0.05; if (yPad <= 0) yPad = 1;
            xMin -= xPad; xMax += xPad; yMin -= yPad; yMax += yPad;

            double maxDev = 0;
            for (int i = 0; i < dxs.Count; i++) maxDev = Math.Max(maxDev, Math.Sqrt(dxs[i] * dxs[i] + dys[i] * dys[i]));

            if (maxDev > 0)
            {
                const double chartH = 300;
                EnsureSpace(chartH + 20);
                double chartX = MarginL, chartY = y;
                double dataW = xMax - xMin, dataH = yMax - yMin;
                double scale = Math.Min(contentW / dataW, chartH / dataH);
                double usedW = dataW * scale, usedH = dataH * scale;
                double offX = chartX + (contentW - usedW) / 2.0;
                double offY = chartY + (chartH - usedH) / 2.0;
                double XScale(double v) => offX + (v - xMin) * scale;
                double YScale(double v) => offY + usedH - (v - yMin) * scale;
                // Facteur d'exagération porté à 12% de la largeur du cadre (au lieu de 5%) - retour
                // utilisateur après comparaison avec le modèle de référence (qui exagère nettement
                // plus, x381 constaté contre x150 ici sur le même jeu de données réel) : à 5%, les
                // vecteurs restaient trop courts pour que la pointe de flèche se voie clairement.
                double exFact = (usedW * 0.12) / maxDev;

                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), chartX, chartY, contentW, chartH);
                var arrowColor = XColor.FromArgb(239, 68, 68);
                var pointColor = XColor.FromArgb(59, 130, 246);
                for (int i = 0; i < cxs.Count; i++)
                {
                    double px = XScale(cxs[i]), py = YScale(cys[i]);
                    double ex = XScale(cxs[i] + dxs[i] * exFact), ey = YScale(cys[i] + dys[i] * exFact);
                    gfx.DrawLine(new XPen(arrowColor, 1.6), px, py, ex, ey);
                    // Pointe de flèche triangulaire à l'extrémité du vecteur : sans elle, un simple
                    // trait ne montre que l'AXE de l'écart, pas son SENS (retour utilisateur - "on
                    // ne voit pas assez le sens des vecteurs"). Taille plafonnée pour rester nette
                    // même sur les vecteurs très courts ; plafond et ratio relevés (5→8, 0.55→0.65)
                    // pour une pointe bien plus visible, alignée sur le modèle de référence.
                    double vx = ex - px, vy = ey - py;
                    double vlen = Math.Sqrt(vx * vx + vy * vy);
                    if (vlen > 0.5)
                    {
                        double ux = vx / vlen, uy = vy / vlen;
                        double perpX = -uy, perpY = ux;
                        double ah = Math.Min(8.0, vlen * 0.7), aw = ah * 0.65;
                        var baseC = new XPoint(ex - ux * ah, ey - uy * ah);
                        var baseL = new XPoint(baseC.X + perpX * aw, baseC.Y + perpY * aw);
                        var baseR = new XPoint(baseC.X - perpX * aw, baseC.Y - perpY * aw);
                        gfx.DrawPolygon(new XSolidBrush(arrowColor), new[] { new XPoint(ex, ey), baseL, baseR }, XFillMode.Winding);
                    }
                    gfx.DrawEllipse(new XSolidBrush(pointColor), px - 2.5, py - 2.5, 5, 5);
                }
                y = chartY + chartH + 4;
                gfx.DrawString($"Échelle d'exagération : x{Math.Round(exFact)}", NovatlasTheme.FontBody(7), XBrushes.Gray,
                    new XRect(MarginL, y, contentW, 10), XStringFormats.TopRight);
                y += 16;
            }

            double meanDx = dxs.Average(), meanDy = dys.Average();
            double meanMag = Math.Sqrt(meanDx * meanDx + meanDy * meanDy);
            string autoText = $"Le vecteur moyen des écarts planimétriques est de {meanMag:F3} m (dX moy: {meanDx:F3} m, dY moy: {meanDy:F3} m). " +
                (meanMag > 0.02
                    ? "L'amplitude de ce vecteur moyen suggère une dérive systématique ou un léger défaut de calage en translation du levé par rapport au contrôle. Il est conseillé de vérifier les paramètres de transformation (GPS ou Station Totale)."
                    : "Les écarts planimétriques semblent globalement centrés et aléatoires. Aucune translation globale majeure ou erreur systématique flagrante de calage n'est mise en évidence sur ce plan.");

            DrawTextBox("AIDE À L'INTERPRÉTATION", "Cherchez un motif : un agencement \"chaotique\" des flèches rassure sur la nature purement accidentelle des erreurs. À l'inverse, un mouvement d'ensemble trahit une dérive systématique ou un pivotement à corriger.", concColor, concBg);
            DrawTextBox("CONCLUSION", autoText, conclColor, conclBg);
        }

        // ===== 2. Diagramme de dispersion (boxplot) Planimétrie / Altimétrie =====
        void DrawExpertBoxplot(JsonElement[] pts, JsonElement? statsBoxEl)
        {
            if (pts.Length == 0 || statsBoxEl == null) return;
            var planiStatsOpt = GetProp(statsBoxEl.Value, "plani");
            var altiStatsOpt = GetProp(statsBoxEl.Value, "alti");
            if (planiStatsOpt == null || altiStatsOpt == null) return;
            var planiStats = planiStatsOpt.Value;
            var altiStats = altiStatsOpt.Value;

            NewPage();
            gfx.DrawString("2. Diagramme de Dispersion (Planimétrie & Altimétrie)", NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped("Représentation synthétique de l'étalement des écarts en planimétrie et altimétrie avec superposition des points.", NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);
            DrawTextBox("APPROCHE PÉDAGOGIQUE", "La boîte colorée délimite les 50% d'écarts centraux (du 1er au 3ème quartile). Les limites extrêmes définissent la zone de normalité statistique. Les points dessinés individuellement permettent d'apprécier la densité réelle et de repérer les points potentiellement aberrants.", pedaColor, pedaBg);

            var planiVals = pts.Select(p => Num(p, "planiDev")).ToList();
            var altiVals = pts.Select(p => Num(p, "altiDev")).ToList();

            // Axe X partagé (chiffré) sous les 2 rangées + étiquette "Médiane" + embouts de
            // moustache - la version précédente ne dessinait aucune graduation, rendant le
            // diagramme illisible (le "problème d'échelle" signalé).
            const double rowH = 20, rowGap = 60;
            const double chartH = 140;
            EnsureSpace(chartH + 20);
            double chartX = MarginL, chartY = y;
            double maxVal = Math.Max(Num(planiStats, "max"), Num(altiStats, "max"));
            if (maxVal <= 0) maxVal = 0.01;
            double plotW = contentW - 80;
            double XScale(double v) => chartX + 80 + (v / maxVal) * plotW;

            int outliersPlani = 0, outliersAlti = 0;
            void Row(double rowY, string label, List<double> vals, JsonElement stats, XColor boxColor, XColor pointColor, ref int outliers)
            {
                double q1 = Num(stats, "q1"), q3 = Num(stats, "q3"), median = Num(stats, "median");
                double iqr = q3 - q1;
                double minLine = Math.Max(Num(stats, "min"), q1 - 1.5 * iqr);
                double maxLine = Math.Min(Num(stats, "max"), q3 + 1.5 * iqr);
                outliers = vals.Count(v => v > maxLine || v < minLine);

                gfx.DrawString(label, NovatlasTheme.FontBodyBold(9), XBrushes.Black, new XRect(MarginL, rowY, 76, 20), XStringFormats.CenterLeft);
                double cy = rowY + 10;
                var whiskerPen = new XPen(XColor.FromArgb(51, 65, 85), 1.5);
                gfx.DrawLine(whiskerPen, XScale(minLine), cy, XScale(maxLine), cy);
                gfx.DrawLine(whiskerPen, XScale(minLine), cy - 5, XScale(minLine), cy + 5);
                gfx.DrawLine(whiskerPen, XScale(maxLine), cy - 5, XScale(maxLine), cy + 5);
                double boxX1 = XScale(q1), boxX2 = XScale(q3);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(30, 41, 59)), new XSolidBrush(boxColor), boxX1, rowY, Math.Max(1, boxX2 - boxX1), rowH);
                double medX = XScale(median);
                gfx.DrawLine(new XPen(XColor.FromArgb(15, 23, 42), 2), medX, rowY, medX, rowY + rowH);
                gfx.DrawString($"Médiane : {median:F3} m", NovatlasTheme.FontBodyBold(6.5), XBrushes.Black,
                    new XRect(medX - 40, rowY - 9, 80, 9), XStringFormats.Center);

                var rnd = new Random(label.GetHashCode());
                foreach (var v in vals)
                {
                    double px = XScale(v);
                    double py = rowY + 2 + rnd.NextDouble() * (rowH - 4);
                    gfx.DrawEllipse(new XSolidBrush(pointColor), px - 1.6, py - 1.6, 3.2, 3.2);
                }
            }

            Row(chartY + 10, "Planimétrie (m)", planiVals, planiStats, XColor.FromArgb(96, 165, 250), XColor.FromArgb(37, 99, 235), ref outliersPlani);
            Row(chartY + 10 + rowGap, "Altimétrie (m)", altiVals, altiStats, XColor.FromArgb(129, 140, 248), XColor.FromArgb(79, 70, 229), ref outliersAlti);

            double axisY = chartY + 10 + rowGap + rowH + 6;
            gfx.DrawLine(NovatlasTheme.GridPenThin(), chartX + 80, axisY, chartX + 80 + plotW, axisY);
            const int xTicks = 6;
            for (int i = 0; i <= xTicks; i++)
            {
                double v = maxVal * i / xTicks;
                double px = XScale(v);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), px, axisY, px, axisY + 3);
                gfx.DrawString(v.ToString("0.00", CultureInfo.InvariantCulture), NovatlasTheme.FontBody(6.5), XBrushes.Black,
                    new XRect(px - 15, axisY + 4, 30, 10), XStringFormats.Center);
            }
            gfx.DrawString("Amplitude de l'écart (m)", NovatlasTheme.FontBody(7.5), XBrushes.Black, new XRect(chartX + 80, axisY + 14, plotW, 10), XStringFormats.Center);

            y = chartY + chartH + 10;

            string autoText = $"En planimétrie, la zone de normalité (la boîte regroupant 50% des mesures) se situe entre {Num(planiStats, "q1"):F3} m et {Num(planiStats, "q3"):F3} m (médiane : {Num(planiStats, "median"):F3} m). En altimétrie, cette plage est de {Num(altiStats, "q1"):F3} m à {Num(altiStats, "q3"):F3} m (médiane : {Num(altiStats, "median"):F3} m). " +
                (outliersPlani > 0 || outliersAlti > 0
                    ? $"L'analyse statistique révèle {outliersPlani} point(s) aberrant(s) potentiel(s) en planimétrie et {outliersAlti} point(s) en altimétrie (situés au-delà des limites théoriques du diagramme). Ces points hors norme doivent faire l'objet d'une attention particulière."
                    : "La dispersion globale des erreurs est homogène. Aucun point aberrant ne se détache statistiquement en dehors des limites de tolérance du diagramme.");

            DrawTextBox("AIDE À L'INTERPRÉTATION", "Des limites d'étendue courtes et une boîte resserrée garantissent un levé homogène. Un nuage de points très étalé ou fortement asymétrique met en évidence une qualité de mesure irrégulière.", concColor, concBg);
            DrawTextBox("CONCLUSION", autoText, conclColor, conclBg);
        }

        // ===== 3. Courbe des fréquences cumulées (CDF) Planimétrie =====
        void DrawExpertCDF(JsonElement[] pts, double s1)
        {
            if (pts.Length == 0) return;
            var sorted = pts.Select(p => Num(p, "planiDev")).OrderBy(v => v).ToList();

            NewPage();
            gfx.DrawString("3. Courbe des Fréquences Cumulées (Planimétrie)", NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped("Analyse du pourcentage de points respectant un écart donné.", NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);
            DrawTextBox("APPROCHE PÉDAGOGIQUE", "La courbe CDF lit directement la proportion du levé satisfaisant une tolérance. Si l'on fixe l'axe X à un seuil réglementaire (ex: S1), l'axe Y donne immédiatement le pourcentage de points conformes.", pedaColor, pedaBg);

            // Axe X/Y avec graduations chiffrées - la version précédente ne dessinait qu'un cadre
            // vide sans aucune échelle lisible (le "problème d'échelle" signalé : impossible de
            // savoir à quel écart correspond un point de la courbe).
            const double chartH = 190;
            double axisPadL = 30, axisPadB = 24;
            double chartW = contentW - axisPadL;
            EnsureSpace(chartH + axisPadB + 40);
            double chartX = MarginL + axisPadL, chartY = y;
            double xMax = sorted[^1] * 1.05; if (xMax <= 0) xMax = 0.01;
            double XScale(double v) => chartX + (v / xMax) * chartW;
            double YScale(double pct) => chartY + chartH - pct * chartH;

            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), chartX, chartY, chartW, chartH);
            var lightGrid = new XPen(XColor.FromArgb(230, 230, 230), 0.5);
            for (int i = 0; i <= 10; i++)
            {
                double py = YScale(i / 10.0);
                if (i > 0 && i < 10) gfx.DrawLine(lightGrid, chartX, py, chartX + chartW, py);
                gfx.DrawString($"{i * 10}%", NovatlasTheme.FontBody(6.5), XBrushes.Black, new XRect(MarginL, py - 5, axisPadL - 3, 10), XStringFormats.CenterRight);
            }
            const int xTicks = 6;
            for (int i = 0; i <= xTicks; i++)
            {
                double v = xMax * i / xTicks;
                double px = XScale(v);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), px, chartY + chartH, px, chartY + chartH + 3);
                gfx.DrawString(v.ToString("0.00", CultureInfo.InvariantCulture), NovatlasTheme.FontBody(6.5), XBrushes.Black,
                    new XRect(px - 15, chartY + chartH + 4, 30, 10), XStringFormats.Center);
            }
            gfx.DrawString("Écart Planimétrique (m)", NovatlasTheme.FontBody(7.5), XBrushes.Black, new XRect(chartX, chartY + chartH + 15, chartW, 10), XStringFormats.Center);

            var linePts = new List<XPoint>();
            for (int i = 0; i < sorted.Count; i++)
            {
                double pct = (i + 1) / (double)sorted.Count;
                if (i > 0) linePts.Add(new XPoint(XScale(sorted[i]), YScale((i) / (double)sorted.Count)));
                linePts.Add(new XPoint(XScale(sorted[i]), YScale(pct)));
            }
            if (linePts.Count > 1) gfx.DrawLines(new XPen(XColor.FromArgb(2, 132, 199), 2), linePts.ToArray());

            double pctBelowS1 = 0;
            if (s1 > 0 && s1 <= xMax)
            {
                double px = XScale(s1);
                gfx.DrawLine(new XPen(XColor.FromArgb(22, 163, 74), 1.5) { DashStyle = XDashStyle.Dash }, px, chartY, px, chartY + chartH);
                gfx.DrawString("Seuil S1", NovatlasTheme.FontBody(7), new XSolidBrush(XColor.FromArgb(22, 163, 74)), new XRect(px + 2, chartY, 50, 10), XStringFormats.TopLeft);
                pctBelowS1 = sorted.Count(v => v <= s1) / (double)sorted.Count * 100;
                gfx.DrawString($"{pctBelowS1:F1}% des points <= S1", NovatlasTheme.FontBodyBold(8), new XSolidBrush(XColor.FromArgb(2, 132, 199)),
                    new XRect(chartX, chartY + chartH - 14, chartW - 4, 12), XStringFormats.TopRight);
            }

            y = chartY + chartH + axisPadB + 12;

            string autoText = s1 > 0
                ? $"{pctBelowS1:F1}% des points contrôlés respectent le seuil réglementaire S1 ({s1:F3} m)."
                : "Analyse du pourcentage cumulé de points respectant chaque niveau d'écart planimétrique.";

            DrawTextBox("AIDE À L'INTERPRÉTATION", "Une montée rapide (courbe abrupte) vers les 100% dans les faibles écarts est la signature d'un levé d'excellente qualité.", concColor, concBg);
            DrawTextBox("CONCLUSION", autoText, conclColor, conclBg);
        }

        // ===== 4. Rose des écarts (biais directionnel) =====
        void DrawExpertRoseDiagram(JsonElement[] pts)
        {
            if (pts.Length == 0) return;
            NewPage();
            gfx.DrawString("4. Rose des Écarts (Biais Directionnel)", NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped("Analyse des orientations préférentielles des erreurs planimétriques.", NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);
            DrawTextBox("APPROCHE PÉDAGOGIQUE", "Ce diagramme polaire découpe l'espace en 8 secteurs cardinaux. La taille du pétale est proportionnelle au nombre de vecteurs d'erreur pointant dans cette direction.", pedaColor, pedaBg);

            var bins = new int[8];
            foreach (var p in pts)
            {
                double dx = Num(p, "dX"), dy = Num(p, "dY");
                double angle = Math.Atan2(dx, dy);
                if (angle < 0) angle += 2 * Math.PI;
                double shifted = angle + Math.PI / 8;
                if (shifted >= 2 * Math.PI) shifted -= 2 * Math.PI;
                int idx = (int)Math.Floor(shifted / (Math.PI / 4));
                if (idx >= 0 && idx < 8) bins[idx]++;
            }

            string[] labels = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
            var petalColors = new[]
            {
                XColor.FromArgb(239, 68, 68), XColor.FromArgb(249, 115, 22), XColor.FromArgb(234, 179, 8), XColor.FromArgb(132, 204, 22),
                XColor.FromArgb(34, 197, 94), XColor.FromArgb(6, 182, 212), XColor.FromArgb(59, 130, 246), XColor.FromArgb(168, 85, 247)
            };

            const double chartH = 240;
            EnsureSpace(chartH + 20);
            double cx = MarginL + contentW / 2.0, cy = y + chartH / 2.0;
            double radius = Math.Min(contentW, chartH) / 2.0 - 30;
            int maxBin = bins.Max(); if (maxBin <= 0) maxBin = 1;

            for (int i = 0; i < 8; i++)
            {
                double startAngle = i * 45 - 22.5 - 90;
                double r = radius * (bins[i] / (double)maxBin);
                if (r > 0)
                    gfx.DrawPie(new XSolidBrush(petalColors[i]), cx - r, cy - r, r * 2, r * 2, startAngle, 45);
            }
            gfx.DrawEllipse(NovatlasTheme.GridPenThin(), cx - radius, cy - radius, radius * 2, radius * 2);

            // Pourcentage de points par secteur, affiché au milieu de chaque pétale non vide -
            // comme .petal-label de la référence (manquant dans la version précédente).
            for (int i = 0; i < 8; i++)
            {
                if (bins[i] <= 0) continue;
                double r = radius * (bins[i] / (double)maxBin);
                double midAngleDeg = i * 45 - 90;
                double midAngleRad = midAngleDeg * Math.PI / 180.0;
                double labelR = Math.Max(10, r * 0.62);
                double lx = cx + labelR * Math.Cos(midAngleRad);
                double ly = cy + labelR * Math.Sin(midAngleRad);
                double pct = bins[i] / (double)pts.Length * 100;
                gfx.DrawString($"{pct:F0}%", NovatlasTheme.FontBodyBold(7), XBrushes.White, new XRect(lx - 14, ly - 5, 28, 10), XStringFormats.Center);
            }

            for (int i = 0; i < 8; i++)
            {
                double angleRad = i * Math.PI / 4;
                double lx = cx + (radius + 14) * Math.Sin(angleRad);
                double ly = cy - (radius + 14) * Math.Cos(angleRad);
                gfx.DrawString(labels[i], NovatlasTheme.FontBodyBold(8), XBrushes.Black, new XRect(lx - 12, ly - 5, 24, 10), XStringFormats.Center);
            }

            y = cy + radius + 24;

            int maxIdx = Array.IndexOf(bins, maxBin);
            double pctMaxDir = maxBin / (double)pts.Length * 100;
            string dirLabel = labels[maxIdx];
            string autoText = $"L'orientation préférentielle des vecteurs d'erreurs se situe majoritairement vers le secteur {dirLabel}, regroupant {pctMaxDir:F1}% des points contrôlés. " +
                (pctMaxDir / 100.0 > 0.25
                    ? "Ce regroupement supérieur à 25% dans un seul cadran caractérise un net biais directionnel, signalant un glissement d'ensemble, une rotation du bloc levé ou une potentielle erreur de rattachement initial."
                    : "Ce pourcentage modéré dépeint une répartition azimutale relativement équilibrée. Les erreurs apparaissent donc accidentelles, non corrélées à une direction d'erreur systématique.");

            DrawTextBox("AIDE À L'INTERPRÉTATION", "Une \"fleur\" symétrique traduit un levé sain où les erreurs s'annulent globalement. Un pétale étiré signale un glissement directionnel du bloc levé.", concColor, concBg);
            DrawTextBox("CONCLUSION", autoText, conclColor, conclBg);
        }

        // ===== 5. Diagramme quantile-quantile (Q-Q Plot) des écarts 3D =====
        void DrawExpertQQPlot(JsonElement[] pts)
        {
            if (pts.Length < 5) return;
            var sorted = pts.Select(p => Num(p, "threeDDev")).OrderBy(v => v).ToList();
            int n = sorted.Count;

            NewPage();
            gfx.DrawString("5. Diagramme de Normalité des Écarts 3D (Q-Q Plot)", NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped("Validation du postulat de normalité des erreurs (Loi de Gauss).", NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);
            DrawTextBox("APPROCHE PÉDAGOGIQUE", "Les formules de tolérance de l'arrêté 2003 présupposent que vos erreurs suivent une courbe de Gauss. Le Q-Q Plot confronte la réalité (axe Y) à une distribution théorique parfaite (axe X).", pedaColor, pedaBg);

            double ApproxInvNormal(double p) => 4.91 * (Math.Pow(p, 0.14) - Math.Pow(1 - p, 0.14));
            var xs = new double[n];
            for (int i = 0; i < n; i++) xs[i] = ApproxInvNormal((i + 0.5) / n);

            double xMin = xs.Min() - 0.5, xMax = xs.Max() + 0.5;
            double yMin = sorted[0] * 0.9, yMax = sorted[^1] * 1.1;
            if (yMax <= yMin) yMax = yMin + 0.01;

            // Axes chiffrés + légende - la version précédente traçait juste un cadre vide avec la
            // diagonale et les points, sans aucune échelle ni légende (le "problème d'échelle").
            const double chartH = 220;
            double axisPadL = 38, axisPadB = 24;
            double chartW = contentW - axisPadL;
            EnsureSpace(chartH + axisPadB + 40);
            double chartX = MarginL + axisPadL, chartY = y;
            double XScale(double v) => chartX + (v - xMin) / (xMax - xMin) * chartW;
            double YScale(double v) => chartY + chartH - (v - yMin) / (yMax - yMin) * chartH;

            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), chartX, chartY, chartW, chartH);
            const int yTicks = 6;
            for (int i = 0; i <= yTicks; i++)
            {
                double v = yMin + (yMax - yMin) * i / yTicks;
                double py = YScale(v);
                gfx.DrawLine(new XPen(XColor.FromArgb(230, 230, 230), 0.5), chartX, py, chartX + chartW, py);
                gfx.DrawString(v.ToString("0.00", CultureInfo.InvariantCulture), NovatlasTheme.FontBody(6.5), XBrushes.Black,
                    new XRect(MarginL, py - 5, axisPadL - 3, 10), XStringFormats.CenterRight);
            }
            const int xTicksQq = 6;
            for (int i = 0; i <= xTicksQq; i++)
            {
                double v = xMin + (xMax - xMin) * i / xTicksQq;
                double px = XScale(v);
                gfx.DrawLine(NovatlasTheme.GridPenThin(), px, chartY + chartH, px, chartY + chartH + 3);
                gfx.DrawString(v.ToString("0.0", CultureInfo.InvariantCulture), NovatlasTheme.FontBody(6.5), XBrushes.Black,
                    new XRect(px - 15, chartY + chartH + 4, 30, 10), XStringFormats.Center);
            }
            gfx.DrawString("Quantiles Théoriques (Loi Normale Standard)", NovatlasTheme.FontBody(7.5), XBrushes.Black,
                new XRect(chartX, chartY + chartH + 15, chartW, 10), XStringFormats.Center);

            // Légende
            double legX = chartX + 8, legY = chartY + 8;
            gfx.DrawLine(new XPen(XColor.FromArgb(239, 68, 68), 1.5) { DashStyle = XDashStyle.Dash }, legX, legY + 4, legX + 16, legY + 4);
            gfx.DrawString("Distribution normale théorique", NovatlasTheme.FontBody(7), XBrushes.Black, new XRect(legX + 20, legY - 1, 160, 10), XStringFormats.CenterLeft);
            gfx.DrawEllipse(new XSolidBrush(XColor.FromArgb(59, 130, 246)), legX + 6 - 2, legY + 14 - 2, 4, 4);
            gfx.DrawString("Écarts réels observés", NovatlasTheme.FontBody(7), XBrushes.Black, new XRect(legX + 20, legY + 9, 160, 10), XStringFormats.CenterLeft);

            if (n > 3)
            {
                int q1Idx = (int)(n * 0.25), q3Idx = (int)(n * 0.75);
                double q1x = xs[q1Idx], q1y = sorted[q1Idx], q3x = xs[q3Idx], q3y = sorted[q3Idx];
                if (q3x != q1x)
                {
                    double slope = (q3y - q1y) / (q3x - q1x);
                    double intercept = q1y - slope * q1x;
                    double ly1 = xMin * slope + intercept, ly2 = xMax * slope + intercept;
                    // La droite théorique évaluée aux bornes X peut sortir très largement du
                    // cadre en Y quand la pente est raide (données réelles, contrairement au jeu
                    // synthétique de test) - on la découpe strictement au cadre du graphique
                    // plutôt que de la laisser traverser texte/légende en dehors de la boîte.
                    var clipState = gfx.Save();
                    gfx.IntersectClip(new XRect(chartX, chartY, chartW, chartH));
                    gfx.DrawLine(new XPen(XColor.FromArgb(239, 68, 68), 1.5) { DashStyle = XDashStyle.Dash }, XScale(xMin), YScale(ly1), XScale(xMax), YScale(ly2));
                    gfx.Restore(clipState);
                }
            }

            for (int i = 0; i < n; i++)
                gfx.DrawEllipse(new XSolidBrush(XColor.FromArgb(59, 130, 246)), XScale(xs[i]) - 2, YScale(sorted[i]) - 2, 4, 4);

            y = chartY + chartH + axisPadB + 12;

            double mean3D = sorted.Average();
            double std3D = sorted.Count > 1 ? Math.Sqrt(sorted.Sum(v => Math.Pow(v - mean3D, 2)) / (sorted.Count - 1)) : 0;
            double max3D = sorted[^1];
            string autoText = $"L'écart dimensionnel 3D maximum observé est de {max3D:F3} m (Moyenne : {mean3D:F3} m, Écart-type : {std3D:F3} m). " +
                (max3D > mean3D + 3 * std3D
                    ? "Les points situés aux extrémités supérieures dévient fortement de l'alignement théorique tracé en diagonale rouge. Cette cassure de la courbe de normalité souligne la présence de résidus lourds accidentels (fautes manifestes)."
                    : "L'alignement global des quantiles le long de la diagonale théorique rouge valide avec satisfaction l'hypothèse d'une distribution normale des erreurs.");

            DrawTextBox("AIDE À L'INTERPRÉTATION", "Si vos points s'alignent sur la diagonale rouge, le modèle gaussien est pleinement justifié. Des points qui \"décollent\" fortement aux extrémités indiquent des erreurs accidentelles lourdes atypiques.", concColor, concBg);
            DrawTextBox("CONCLUSION", autoText, conclColor, conclBg);
        }

        // ===== 6. Profil séquentiel des écarts altimétriques (lollipop chart) =====
        void DrawExpertLollipop(JsonElement[] pts)
        {
            if (pts.Length == 0) return;
            NewPage();
            gfx.DrawString("6. Profil Séquentiel des Écarts (Altimétrie / dZ)", NovatlasTheme.FontBold(13), XBrushes.Black, new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped("Analyse individuelle et chronologique des écarts altimétriques avec notion de signe (+/-).", NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);
            DrawTextBox("APPROCHE PÉDAGOGIQUE", "Contrairement à l'écart absolu, le delta Z (dZ) conserve son signe. Il permet d'étudier point par point si le levé est au-dessus ou en dessous du contrôle.", pedaColor, pedaBg);

            var dz = pts.Select(p => Num(p, "dZ")).ToList();
            int n = dz.Count;
            double maxAbs = dz.Count > 0 ? dz.Select(Math.Abs).Max() : 0.01;
            if (maxAbs <= 0) maxAbs = 0.01;

            const double chartH = 200;
            EnsureSpace(chartH + 20);
            double chartX = MarginL, chartY = y;
            double zeroY = chartY + chartH / 2.0;
            double YScale(double v) => zeroY - (v / (maxAbs * 1.1)) * (chartH / 2.0);
            double stepX = n > 1 ? contentW / n : contentW;

            gfx.DrawLine(new XPen(XColors.Black, 1.2), chartX, zeroY, chartX + contentW, zeroY);

            for (int i = 0; i < n; i++)
            {
                double px = chartX + stepX * (i + 0.5);
                double py = YScale(dz[i]);
                var color = dz[i] >= 0 ? XColor.FromArgb(16, 185, 129) : XColor.FromArgb(239, 68, 68);
                gfx.DrawLine(new XPen(color, 1.2), px, zeroY, px, py);
                gfx.DrawEllipse(new XSolidBrush(color), px - 2, py - 2, 4, 4);
            }

            y = chartY + chartH + 12;

            int posCount = dz.Count(v => v > 0), negCount = dz.Count(v => v < 0);
            double pctPos = n > 0 ? posCount / (double)n * 100 : 0, pctNeg = n > 0 ? negCount / (double)n * 100 : 0;
            double meanDz = n > 0 ? dz.Average() : 0;
            string autoText = $"Sur la base séquentielle des {n} points examinés, {pctPos:F1}% des valeurs levées sont situées au-dessus du nuage de contrôle (dZ &gt; 0) et {pctNeg:F1}% en dessous (dZ &lt; 0). Le biais altimétrique constant moyen est évalué à {meanDz:F3} m. ";
            autoText += (Math.Abs(meanDz) > 0.02 || pctPos / 100.0 > 0.75 || pctNeg / 100.0 > 0.75)
                ? $"La forte dissymétrie des barres par rapport à l'axe zéro détecte un biais altimétrique systématique : le levé semble être lissé globalement trop {(meanDz > 0 ? "haut" : "bas")}. Il est prescrit de vérifier la présence d'un décalage constant originel."
                : "Le nuage de points oscille de part et d'autre de la référence zéro. Cette alternance équilibrée permet d'écarter l'hypothèse d'un biais d'altitude constant imposé au levé.";

            DrawTextBox("AIDE À L'INTERPRÉTATION", "Idéal pour le contrôle de nivellement : une tendance prolongée du même côté de la ligne zéro confirme un biais d'altitude constant.", concColor, concBg);
            DrawTextBox("CONCLUSION", autoText, conclColor, conclBg);
        }

        var meta = root.TryGetProperty("meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object ? metaEl : default;
        var prms = root.TryGetProperty("params", out var prmsEl) && prmsEl.ValueKind == JsonValueKind.Object ? prmsEl : default;
        var counts = root.TryGetProperty("counts", out var countsEl) && countsEl.ValueKind == JsonValueKind.Object ? countsEl : default;
        var conditions = root.TryGetProperty("conditions", out var condEl) && condEl.ValueKind == JsonValueKind.Object ? condEl : default;

        // ===== Page 1 : Page de garde =====
        gfx.DrawString("NOVATLAS GROUPE", NovatlasTheme.FontBold(22), new XSolidBrush(NovatlasTheme.ResolveBlue(root)),
            new XRect(MarginL, y, contentW, 30), XStringFormats.Center);
        y += 30;
        gfx.DrawString("RAPPORT DE CONTRÔLE POUR L'APPLICATION DE CLASSES DE PRÉCISION", NovatlasTheme.FontBody(12), XBrushes.Black,
            new XRect(MarginL, y, contentW, 18), XStringFormats.Center);
        y += 22;
        DrawWrapped(
            "Arrêté du 16 septembre 2003 portant sur les classes de précision applicables aux catégories de travaux topographiques réalisés par l'Etat, les collectivités locales et leurs établissements publics ou exécutés pour leur compte.",
            NovatlasTheme.FontBody(8), XBrushes.Gray, XStringFormats.Center, 8);
        DrawWrapped("Gabarit: modèle standard", NovatlasTheme.FontBold(11), XBrushes.Black, XStringFormats.Center, 2);
        DrawWrapped(
            "Type de travaux topographiques analysés: POLYGONATION - PLAN TOPOGRAPHIQUE - RECOLEMENT DE TRAVAUX - NIVELLEMENT - BÂTIMENT",
            NovatlasTheme.FontBody(8), XBrushes.Gray, XStringFormats.Center, 8);

        var logo = NovatlasTheme.ResolveLogo(root);
        if (logo != null)
        {
            double maxW = Units.MmToPt(45), maxH = Units.MmToPt(35);
            double ar = (double)logo.PixelWidth / Math.Max(1, logo.PixelHeight);
            double iw = maxW, ih = iw / ar;
            if (ih > maxH) { ih = maxH; iw = ih * ar; }
            EnsureSpace(ih + 8);
            gfx.DrawImage(logo, MarginL + (contentW - iw) / 2.0, y, iw, ih);
            y += ih + 8;
        }

        DrawInfoTable("Informations sur l'affaire", new (string, string?)[]
        {
            ("Commanditaire", Str(meta, "commanditaire")),
            ("Rédacteur du rapport", Str(meta, "redacteur")),
            ("Date de rédaction", FormatDate(Str(meta, "dateRedaction"))),
            ("Prestataire du Levé", Str(meta, "prestataireLeve")),
            ("Prestataire du Contrôle", Str(meta, "prestataireControle")),
            ("Commune", Str(meta, "commune")),
            ("Adresse", Str(meta, "adresse")),
            ("Type de Plan Contrôlé", Str(meta, "typePlan")),
            ("Référence / Dossier", Str(meta, "reference")),
        }, contentW);

        // Carte de situation (facultative - présente seulement si l'utilisateur a cliqué sur
        // "Générer la carte" avant l'export ; image PNG déjà géocodée/téléchargée par
        // MainForm.GenerateCpMapAsync, reçue ici en data URL base64).
        var mapDataUrl = Str(root, "mapImageDataUrl");
        if (!string.IsNullOrWhiteSpace(mapDataUrl))
        {
            try
            {
                var commaIdx = mapDataUrl.IndexOf(',');
                var base64 = commaIdx >= 0 ? mapDataUrl[(commaIdx + 1)..] : mapDataUrl;
                var bytes = Convert.FromBase64String(base64);
                var mapImg = XImage.FromStream(new MemoryStream(bytes));

                // La carte doit occuper tout le reste de la page de garde plutôt qu'être limitée
                // à une hauteur fixe (70mm) qui laissait beaucoup de blanc en dessous : on
                // maximise sa taille dans l'espace restant (largeur x hauteur disponible) tout en
                // conservant son ratio d'aspect. Si peu de place reste sur la page courante, on
                // passe à une nouvelle page pour que la carte ait vraiment de la place.
                double titleH = 16;
                double availH = pageH - MarginBottom - y - titleH;
                if (availH < 150) { NewPage(); availH = pageH - MarginBottom - y - titleH; }

                gfx.DrawString("Plan de situation", NovatlasTheme.FontBold(11), XBrushes.Black, new XRect(MarginL, y, contentW, 14), XStringFormats.TopLeft);
                y += titleH;

                double scale = Math.Min(contentW / mapImg.PixelWidth, availH / mapImg.PixelHeight);
                double mapW = mapImg.PixelWidth * scale;
                double mapH = mapImg.PixelHeight * scale;
                double mapX = MarginL + (contentW - mapW) / 2.0;
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), mapX, y, mapW, mapH);
                gfx.DrawImage(mapImg, mapX, y, mapW, mapH);
                y += mapH + 10;
            }
            catch
            {
                // Image corrompue/illisible : on l'omet simplement plutôt que de faire échouer
                // tout le rapport (même discipline que NovatlasTheme.ResolveLogo).
            }
        }

        // ===== Page 2 : Paramètres et résumé =====
        NewPage();
        gfx.DrawString(SecTitle("Paramètres et Résumé Statistique du Contrôle"), NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 26;

        string controlType = Str(prms, "controlType") ?? "N/A";
        var mathStatsEl = root.TryGetProperty("mathStats", out var msElAll) && msElAll.ValueKind == JsonValueKind.Object ? (JsonElement?)msElAll : null;

        // Taux de confiance affiché en clair (68/95/99%) plutôt que le seul coefficient Z brut -
        // mêmes paliers que le select cpConfidence de l'UI (1.00/1.96/2.58).
        string ConfidenceLabel(double z) => z switch
        {
            <= 1.05 => "68 %",
            <= 2.0 => "95 %",
            _ => "99 %"
        };
        double confidenceZ = Num(prms, "confidenceZ");

        // Scindé en 2 tables distinctes (Paramètre/Valeur puis Statistique/Valeur), comme la
        // référence - la version précédente les fusionnait en une seule table, ce qui faisait
        // disparaître le taux de confiance et distinguait mal "points appariés avant sélection"
        // de "points sélectionnés pour l'analyse" (2 compteurs différents).
        DrawInfoTable(null, new (string, string?)[]
        {
            ("Tolérance XY", $"{Num(prms, "tolXYcm"):0.0} cm"),
            ("Tolérance Z", $"{Num(prms, "tolZcm"):0.0} cm"),
            ("Tolérance 3D (calculée)", $"{Num(counts, "tolerance3Dcm"):0.0} cm"),
            ("Type de contrôle", controlType),
            ("Coefficient de sécurité (C)", Num(prms, "securityCoefficient").ToString("0.00", CultureInfo.InvariantCulture)),
            ("Taux de confiance (IC)", $"{ConfidenceLabel(confidenceZ)} (Z={confidenceZ:0.00})"),
        }, contentW, colHeaders: ("Paramètre", "Valeur"));
        y += 6;

        DrawInfoTable(null, new (string, string?)[]
        {
            ("Points Levé (Total)", IntStr(counts, "levePoints")),
            ("Points Contrôle (Total)", IntStr(counts, "controlePoints")),
            ("Points Appariés (avant sélection)", IntStr(counts, "matchedPoints")),
            ("Points Sélectionnés pour Analyse", IntStr(counts, "includedPoints")),
            ("Taux de Contrôle Analysé", $"{Num(counts, "tauxControlePct"):0.0} % (sur points levé total)"),
            ("Moyenne Écart Planimétrique (points sélectionnés)", mathStatsEl != null ? $"{Num(GetProp(mathStatsEl.Value, "plani")!.Value, "mean"):F3} m" : "N/A"),
            ("Moyenne Écart Altimétrique (points sélectionnés)", mathStatsEl != null ? $"{Num(GetProp(mathStatsEl.Value, "alti")!.Value, "mean"):F3} m" : "N/A"),
            ("Moyenne Écart 3D (points sélectionnés)", mathStatsEl != null ? $"{Num(GetProp(mathStatsEl.Value, "threeD")!.Value, "mean"):F3} m" : "N/A"),
        }, contentW, colHeaders: ("Statistique", "Valeur"));
        y += 6;

        // ===== Conditions réglementaires =====
        EnsureSpace(24);
        gfx.DrawString(SecTitle("Vérification des Conditions Réglementaires"), NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 26;

        if (controlType.Contains("1D"))
            DrawConditionTable("Contrôle Altimétrique (1D)", GetProp(conditions, "alti"), "m");
        if (controlType.Contains("2D"))
            DrawConditionTable("Contrôle Planimétrique (2D)", GetProp(conditions, "plani"), "m");
        // Toujours affiché (contrôle croisé informatif) - pas conditionné à controlType, voir
        // commentaire sur "threeD" dans MainForm.cs (CpConditionToJs).
        DrawConditionTable("Contrôle 3D Isotrope", GetProp(conditions, "threeD"), "m");

        // ===== Conclusion globale =====
        // Saut de page forcé avant la conclusion : la référence fait "1+2 sur une page, 3 sur la
        // suivante" (doc.addPage() explicite juste avant "Conclusion Globale" dans son export) -
        // laisser faire l'empilement naturel ne respectait pas systématiquement cette coupure.
        NewPage();
        gfx.DrawString(SecTitle("Conclusion Globale"), NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 26;
        bool overall = GetBool(conditions, "overallConformity") ?? false;
        gfx.DrawString($"Conclusion (Type : {controlType}) : {(overall ? "CONFORME" : "NON CONFORME")}", NovatlasTheme.FontBold(14),
            new XSolidBrush(overall ? OkGreen : FailRed), new XRect(MarginL, y, contentW, 20), XStringFormats.Center);
        y += 30;

        // Rappel des seuils par type d'écart, en vrai tableau à 5 colonnes (Type d'Écart / Moy.
        // Observée / S1 / T1 / T2) - comme la référence, qui affiche toujours les 3 types (N/A si
        // non applicable) plutôt que de les omettre.
        void DrawSeuilsRecapTable()
        {
            const double rowH = 21;
            double labelW = 140;
            double colW = (contentW - labelW) / 4.0;
            EnsureSpace(rowH * 4 + 10);
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
            gfx.DrawString("Type d'Écart", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + 3, y, labelW - 5, rowH), XStringFormats.CenterLeft);
            string[] heads = { "Moy. Observée (m)", "Seuil S1 (m)", "Seuil T1 (m)", "Seuil T2 (m)" };
            for (int c = 0; c < heads.Length; c++)
                gfx.DrawString(heads[c], NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + labelW + c * colW, y, colW, rowH), XStringFormats.Center);
            y += rowH;

            void Row(string label, JsonElement? condOpt)
            {
                EnsureSpace(rowH);
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y, contentW, rowH);
                gfx.DrawString(label, NovatlasTheme.FontBodyBold(8), XBrushes.Black, new XRect(MarginL + 3, y, labelW - 5, rowH), XStringFormats.CenterLeft);
                bool applicable = condOpt != null && (GetBool(condOpt.Value, "applicable") ?? false);
                string[] vals = applicable
                    ? new[] { F3v(condOpt!.Value, "meanDev"), F3v(condOpt.Value, "seuilS1"), F3v(condOpt.Value, "seuilT1"), F3v(condOpt.Value, "seuilT2") }
                    : new[] { "N/A", "N/A", "N/A", "N/A" };
                for (int c = 0; c < vals.Length; c++)
                    gfx.DrawString(vals[c], NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(MarginL + labelW + c * colW, y, colW, rowH), XStringFormats.Center);
                y += rowH;
            }
            Row("Écart Altimétrique (1D)", GetProp(conditions, "alti"));
            Row("Écart Planimétrique (2D)", GetProp(conditions, "plani"));
            Row("Écart 3D Isotrope", GetProp(conditions, "threeD"));
            y += 10;
        }
        DrawSeuilsRecapTable();

        // Phrases d'analyse auto-générées par type applicable (ordre référence : 2D, 1D, 3D),
        // entièrement absentes de la version précédente - seul le verdict CONFORME/NON CONFORME
        // était affiché, sans le détail condition par condition.
        DrawWrapped("Les trois conditions suivantes doivent être cumulativement respectées (cf. annexe) :", NovatlasTheme.FontBodyItalic(9.5), XBrushes.Black, XStringFormats.TopLeft, 6);

        void DrawConditionAnalysisText(string title, JsonElement? condOpt)
        {
            if (condOpt == null) return;
            var c = condOpt.Value;
            if (!(GetBool(c, "applicable") ?? false)) return;
            DrawWrapped(title, NovatlasTheme.FontBold(10), XBrushes.Black, XStringFormats.TopLeft, 3);
            bool c1 = GetBool(c, "cond1Ok") ?? false, c2 = GetBool(c, "cond2Ok") ?? false, c3 = GetBool(c, "cond3Ok") ?? false;
            DrawWrapped($"La condition N°1 de l'arrêté du 16 Septembre 2003 {(c1 ? "est validée" : "n'est pas validée")} pour ce contrôle de précision.", NovatlasTheme.FontBody(9.5), XBrushes.Black, XStringFormats.TopLeft, 2);
            DrawWrapped($"La condition N°2 de l'arrêté du 16 Septembre 2003 {(c2 ? "est validée" : "n'est pas validée")} pour ce contrôle de précision.", NovatlasTheme.FontBody(9.5), XBrushes.Black, XStringFormats.TopLeft, 2);
            DrawWrapped($"La condition N°3 de l'arrêté du 16 Septembre 2003 {(c3 ? "est validée" : "n'est pas validée")} pour ce contrôle de précision.", NovatlasTheme.FontBody(9.5), XBrushes.Black, XStringFormats.TopLeft, 6);
        }
        if (controlType.Contains("2D")) DrawConditionAnalysisText("Pour le contrôle Planimétrique (2D) :", GetProp(conditions, "plani"));
        if (controlType.Contains("1D")) DrawConditionAnalysisText("Pour le contrôle Altimétrique (1D) :", GetProp(conditions, "alti"));
        DrawConditionAnalysisText("Pour le contrôle 3D Isotrope :", GetProp(conditions, "threeD"));

        y += 10;
        string finalConclusionText = overall
            ? "LE PRÉSENT RAPPORT VALIDE LA CLASSE DE PRÉCISION TOTALE DE L'AFFAIRE REPRISE AU CHAPITRE 1. PARAMÈTRES ET RÉSUMÉ STATISTIQUE DU CONTRÔLE"
            : "LE PRÉSENT RAPPORT INVALIDE LA CLASSE DE PRÉCISION TOTALE DE L'AFFAIRE REPRISE AU CHAPITRE 1. PARAMÈTRES ET RÉSUMÉ STATISTIQUE DU CONTRÔLE";
        DrawWrapped(finalConclusionText, NovatlasTheme.FontBold(11), new XSolidBrush(overall ? OkGreen : FailRed), XStringFormats.Center, 6);

        // ===== Répartition spatiale (scatterplot), juste après la conclusion, comme la référence =====
        if (root.TryGetProperty("scatter", out var scatterEl) && scatterEl.ValueKind == JsonValueKind.Array)
            DrawScatterplotPage(SecTitle("Répartition Spatiale des Points"), scatterEl);

        // Nombre de points sélectionnés, rappelé dans les titres des sections 5/6/7/8 - comme la
        // référence ("... (sur X points sélectionnés)"), manquant dans la version précédente.
        string nSelForTitles = IntStr(counts, "includedPoints");

        // ===== Analyse mathématique des écarts =====
        DrawMathAnalysisTable(SecTitle($"Analyse Mathématique des Écarts (sur {nSelForTitles} points sélectionnés)"), mathStatsEl);

        // ===== Barycentres (hors arrêté 2003 - information complémentaire) =====
        var barycentres = root.TryGetProperty("barycentres", out var baryEl) && baryEl.ValueKind == JsonValueKind.Object ? baryEl : default;
        EnsureSpace(60);
        gfx.DrawString(SecTitle($"Barycentres (hors arrêté 2003, sur {nSelForTitles} points sélectionnés)"), NovatlasTheme.FontBold(12), XBrushes.Black,
            new XRect(MarginL, y, contentW, 18), XStringFormats.TopLeft);
        y += 22;
        // Tableau à 4 colonnes (Description | X/ΔX | Y/ΔY | Z/ΔZ) avec la ligne "Vecteur (Δ)"
        // manquante dans la version précédente (Δ = Barycentre Levé - Barycentre Contrôle, même
        // convention que vectorSurveyToControl de la référence).
        var baryLeve = GetProp(barycentres, "leve");
        var baryControle = GetProp(barycentres, "controle");
        {
            const double rowH = 21;
            double descW = 170;
            double colW = (contentW - descW) / 3.0;
            EnsureSpace(rowH * 4 + 10);
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), MarginL, y, contentW, rowH);
            gfx.DrawString("Description", NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + 3, y, descW - 5, rowH), XStringFormats.CenterLeft);
            string[] baryHeads = { "X / ΔX (m)", "Y / ΔY (m)", "Z / ΔZ (m)" };
            for (int c = 0; c < baryHeads.Length; c++)
                gfx.DrawString(baryHeads[c], NovatlasTheme.FontBold(8), XBrushes.Black, new XRect(MarginL + descW + c * colW, y, colW, rowH), XStringFormats.Center);
            y += rowH;

            void BaryRow(string label, double? x, double? y2, double? z)
            {
                EnsureSpace(rowH);
                gfx.DrawRectangle(NovatlasTheme.GridPenThin(), MarginL, y, contentW, rowH);
                gfx.DrawString(label, NovatlasTheme.FontBodyBold(8), XBrushes.Black, new XRect(MarginL + 3, y, descW - 5, rowH), XStringFormats.CenterLeft);
                string[] vals = { x?.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A", y2?.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A", z?.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A" };
                for (int c = 0; c < vals.Length; c++)
                    gfx.DrawString(vals[c], NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(MarginL + descW + c * colW, y, colW, rowH), XStringFormats.Center);
                y += rowH;
            }
            BaryRow("Bary. Levé Sélectionné", baryLeve != null ? Num(baryLeve.Value, "x") : null, baryLeve != null ? Num(baryLeve.Value, "y") : null, baryLeve != null ? Num(baryLeve.Value, "z") : null);
            BaryRow("Bary. Contrôle Sélectionné", baryControle != null ? Num(baryControle.Value, "x") : null, baryControle != null ? Num(baryControle.Value, "y") : null, baryControle != null ? Num(baryControle.Value, "z") : null);
            BaryRow("Vecteur (Δ)",
                baryLeve != null && baryControle != null ? Num(baryLeve.Value, "x") - Num(baryControle.Value, "x") : null,
                baryLeve != null && baryControle != null ? Num(baryLeve.Value, "y") - Num(baryControle.Value, "y") : null,
                baryLeve != null && baryControle != null ? Num(baryLeve.Value, "z") - Num(baryControle.Value, "z") : null);
            y += 8;
        }

        // ===== Histogrammes des écarts (nombres puis pourcentages) =====
        if (root.TryGetProperty("histogram", out var histEl) && histEl.ValueKind == JsonValueKind.Object)
        {
            var labels = histEl.TryGetProperty("ranges", out var rangesEl) && rangesEl.ValueKind == JsonValueKind.Array
                ? rangesEl.EnumerateArray().Select(r => r.GetString() ?? "").ToArray()
                : Array.Empty<string>();
            double[] ReadArr(string key) => histEl.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(v => v.TryGetDouble(out var d) ? d : 0).ToArray()
                : Array.Empty<double>();

            var c1D = ReadArr("counts1D"); var c2D = ReadArr("counts2D"); var c3D = ReadArr("counts3D");
            var p1D = ReadArr("percent1D"); var p2D = ReadArr("percent2D"); var p3D = ReadArr("percent3D");
            string nSel = IntStr(counts, "includedPoints");
            string out1 = IntStr(histEl, "outside1D"), out2 = IntStr(histEl, "outside2D"), out3 = IntStr(histEl, "outside3D");
            string outPct1 = $"{Num(histEl, "outsidePercent1D"):0.0}%", outPct2 = $"{Num(histEl, "outsidePercent2D"):0.0}%", outPct3 = $"{Num(histEl, "outsidePercent3D"):0.0}%";
            string totPct1 = $"{(100.0 - Num(histEl, "outsidePercent1D")):0.0}%", totPct2 = $"{(100.0 - Num(histEl, "outsidePercent2D")):0.0}%", totPct3 = $"{(100.0 - Num(histEl, "outsidePercent3D")):0.0}%";

            if (labels.Length > 0)
            {
                NewPage();
                DrawHistogramTable(SecTitle($"Histogramme des Écarts (Nombres de points) (sur {nSel} points sélectionnés)"), labels, c1D, c2D, c3D,
                    v => v.ToString("0", CultureInfo.InvariantCulture),
                    new (string, string, string, string)[]
                    {
                        ("Nb. Pts Sélectionnés", nSel, nSel, nSel),
                        ("Nb. Pts Hors Plage (>300mm)", out1, out2, out3),
                    });

                NewPage();
                DrawHistogramTable(SecTitle($"Histogramme des Écarts (%) (sur {nSel} points sélectionnés)"), labels, p1D, p2D, p3D,
                    v => v.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                    new (string, string, string, string)[]
                    {
                        ("Nb. Pts Sélectionnés", nSel, nSel, nSel),
                        ("Total % Dans Plages (0-300mm)", totPct1, totPct2, totPct3),
                        ("% Pts Hors Plage (>300mm)", outPct1, outPct2, outPct3),
                    });
            }
        }

        // ===== Courbes de Gauss (1D / 2D / 3D) - une seule numérotation de section pour les 3
        // courbes, regroupées sur UNE seule page comme la référence (un seul saut de page avant
        // la première ; les 3 s'empilent ensuite, DrawGaussianPage ne force plus de saut). =====
        if (root.TryGetProperty("gaussian", out var gaussEl) && gaussEl.ValueKind == JsonValueKind.Object)
        {
            int gaussNum = sectionCounter++;
            NewPage();
            DrawGaussianPage($"{gaussNum}. Courbe de Gauss des Écarts Altimétriques (1D)", GetProp(gaussEl, "alti"), "Écart Alti (cm)");
            DrawGaussianPage($"{gaussNum}. Courbe de Gauss des Écarts Planimétriques (2D)", GetProp(gaussEl, "plani"), "Écart Plani (cm)");
            DrawGaussianPage($"{gaussNum}. Courbe de Gauss des Écarts 3D", GetProp(gaussEl, "threeD"), "Écart 3D (cm)");
        }

        gfx.Dispose();

        // ===== Tableau détaillé des points (pagination auto via TableRenderer, sans son propre
        // pied de page - buildFooter vide = footerHUsed=0 côté TableRenderer, mais la marge basse
        // reste réservée dans tous les cas (LayoutConstants.FooterReservePt) : le pied de page
        // uniforme de ce rapport est appliqué en une seule passe sur tout le document à la fin. =====
        var pointsPayload = BuildDetailsTablePayload(root);
        pointsPayload.Title = $"{sectionCounter++}. {pointsPayload.Title}";
        if (pointsPayload.Rows.Count > 0)
        {
            var layout = new TableRenderer.TableLayout
            {
                ColumnWidths = new double[] { 38, 42, 42, 36, 42, 42, 36, 34, 34, 34, 42, 42, 42 },
                ThickAllVerticals = false,
                CellBackground = BuildDetailsCellBackground(root, mathStatsEl)
            };
            TableRenderer.RenderImplantationTable(doc, pointsPayload, layout, "");
        }

        // ===== Annexe (sections 1-2 : règles de validation + principes de géoréférencement) =====
        page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        gfx = XGraphics.FromPdfPage(page);
        y = MarginTop;

        gfx.DrawString(SecTitle("Annexe"), NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
        y += 26;

        DrawWrapped("1. RÈGLES DE VALIDATION D'UN CONTRÔLE DE PRÉCISION (ARRÊTÉ DU 16 SEPTEMBRE 2003)", NovatlasTheme.FontBold(10), XBrushes.Black, XStringFormats.TopLeft, 14);
        DrawWrapped(AnnexeIntro, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 14);
        DrawWrapped(AnnexeCond1, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 14);
        DrawWrapped(AnnexeCond2, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 12);

        EnsureSpace(40);
        DrawInfoTable(null, new (string, string?)[] { ("n", "1        2        3"), ("k", "3.23   2.42   2.11") }, 160);
        y += 12;

        // Table du nombre limite N' d'écarts tolérés au-delà de T1, en fonction du nombre de
        // points contrôlés N (arrêté 2003, condition n°2) - reprise à l'identique de la table
        // annexeContent de la référence (colonnes "de 1 à 4" ... "de 423 à 487").
        {
            string[] nRanges = { "de 1 à 4", "de 5 à 13", "de 14 à 44", "de 45 à 85", "de 86 à 132", "de 133 à 184", "de 185 à 240", "de 241 à 298", "de 299 à 359", "de 360 à 422", "de 423 à 487" };
            string[] nPrimeVals = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
            const double gridRowH = 16;
            double gridLabelW = 32;
            double gridColW = (contentW - gridLabelW) / nRanges.Length;
            EnsureSpace(gridRowH * 2 + 6);
            double gridX = MarginL, gridY = y;
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), gridX, gridY, contentW, gridRowH);
            gfx.DrawString("N", NovatlasTheme.FontBold(7), XBrushes.Black, new XRect(gridX, gridY, gridLabelW, gridRowH), XStringFormats.Center);
            for (int i = 0; i < nRanges.Length; i++)
                gfx.DrawString(nRanges[i], NovatlasTheme.FontBold(6.5), XBrushes.Black, new XRect(gridX + gridLabelW + i * gridColW, gridY, gridColW, gridRowH), XStringFormats.Center);
            gridY += gridRowH;
            gfx.DrawRectangle(NovatlasTheme.GridPenThin(), gridX, gridY, contentW, gridRowH);
            gfx.DrawString("N'", NovatlasTheme.FontBodyBold(7), XBrushes.Black, new XRect(gridX, gridY, gridLabelW, gridRowH), XStringFormats.Center);
            for (int i = 0; i < nPrimeVals.Length; i++)
                gfx.DrawString(nPrimeVals[i], NovatlasTheme.FontBody(7), XBrushes.Black, new XRect(gridX + gridLabelW + i * gridColW, gridY, gridColW, gridRowH), XStringFormats.Center);
            for (int i = 0; i <= nRanges.Length; i++)
            {
                double lx = gridX + gridLabelW + i * gridColW;
                gfx.DrawLine(NovatlasTheme.GridPenThin(), lx, y, lx, gridY + gridRowH);
            }
            gfx.DrawLine(NovatlasTheme.GridPenThin(), gridX, y, gridX, gridY + gridRowH);
            gfx.DrawLine(NovatlasTheme.GridPenThin(), gridX + contentW, y, gridX + contentW, gridY + gridRowH);
            y = gridY + gridRowH + 14;
        }

        DrawWrapped(AnnexeCond3, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 16);
        DrawWrapped(AnnexeBullets, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 14);

        NewPage();
        DrawWrapped("2. PRINCIPES DE GÉORÉFÉRENCEMENT ET DE CONTRÔLE DES LEVÉS", NovatlasTheme.FontBold(10), XBrushes.Black, XStringFormats.TopLeft, 16);
        DrawWrapped(AnnexeGeoref, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 20);

        EnsureSpace(30);
        DrawWrapped("3. ANALYSE SOMMAIRE DES BARYCENTRES DES POINTS LEVÉS ET CONTRÔLÉS (HORS ARRÊTÉ de 2003)", NovatlasTheme.FontBold(10), XBrushes.Black, XStringFormats.TopLeft, 16);
        DrawWrapped(AnnexeBarycentres, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 20);

        EnsureSpace(30);
        DrawWrapped("4. ANALYSE DE LA QUALITÉ DES POINTS TOPOGRAPHIQUES : OBJECTIFS, LIMITES ET CONCEPTS STATISTIQUES (HORS ARRÊTÉ de 2003)", NovatlasTheme.FontBold(10), XBrushes.Black, XStringFormats.TopLeft, 16);
        DrawWrapped(AnnexeQualiteObjectif, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 16);
        DrawWrapped(AnnexeQualiteLimites, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 16);
        DrawWrapped(AnnexeQualiteRappel, NovatlasTheme.FontBody(9), XBrushes.Black, XStringFormats.TopLeft, 6);

        // ===== Annexes graphiques d'expertise (hors cadre réglementaire strict de l'arrêté 2003 -
        // même avertissement que l'outil de référence) : 6 graphiques, un par page, à la toute fin
        // du document (même position que l'ANNEXES GRAPHIQUES D'EXPERTISE de référence). Ne dispose
        // pas gfx avant/entre ces appels : chacun commence par NewPage(), qui gère lui-même le
        // Dispose() de la page précédente.
        var pointsForExpert = root.TryGetProperty("points", out var ptsElAll) && ptsElAll.ValueKind == JsonValueKind.Array
            ? ptsElAll.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        if (pointsForExpert.Length > 0)
        {
            NewPage();
            gfx.DrawString(SecTitle("Analyses Visuelles et Statistiques Avancées (Expertise Topométrique)"), NovatlasTheme.FontBold(14), new XSolidBrush(NovatlasTheme.ResolveBlue(root)),
                new XRect(MarginL, y, contentW, 20), XStringFormats.TopLeft);
            y += 24;
            DrawWrapped(
                "À titre de complément d'expertise topométrique, ces visualisations permettent de détecter d'éventuelles erreurs systématiques, d'analyser les dérives de cheminement et d'évaluer la robustesse globale du géoréférencement. Il convient de préciser que ces analyses statistiques avancées s'inscrivent en dehors du cadre réglementaire strict de l'arrêté du 16 septembre 2003 : les observations formulées ici ne remettent aucunement en cause la conclusion officielle sur la validation des conditions de conformité.",
                NovatlasTheme.FontBody(9), XBrushes.Gray, XStringFormats.TopLeft, 6);

            var planiCondForCdf = GetProp(conditions, "plani");
            double s1ForCdf = planiCondForCdf != null ? Num(planiCondForCdf.Value, "seuilS1") : 0;
            var statsBoxEl = root.TryGetProperty("statsBox", out var sbElAll) && sbElAll.ValueKind == JsonValueKind.Object ? (JsonElement?)sbElAll : null;

            DrawExpertVectorPlot(pointsForExpert, newPage: false);
            DrawExpertBoxplot(pointsForExpert, statsBoxEl);
            DrawExpertCDF(pointsForExpert, s1ForCdf);
            DrawExpertRoseDiagram(pointsForExpert);
            DrawExpertQQPlot(pointsForExpert);
            DrawExpertLollipop(pointsForExpert);
        }

        gfx.Dispose();

        RestampFooters(doc, buildFooter, root);
    }

    // Code couleur par tercile (vert/jaune/rouge) des colonnes Éc. Plani/Alti/3D, comme
    // getColorClass(value, stats) de la référence : vert si <= P33, jaune si <= P67, rouge
    // au-delà - pas de couleur si la série n'a aucune variation (min == max).
    private static readonly XColor TercileGreen = XColor.FromArgb(220, 252, 231);
    private static readonly XColor TercileYellow = XColor.FromArgb(254, 249, 195);
    private static readonly XColor TercileRed = XColor.FromArgb(254, 226, 226);

    private static Func<int, int, XColor?>? BuildDetailsCellBackground(JsonElement root, JsonElement? mathStatsEl)
    {
        if (mathStatsEl == null) return null;
        if (!root.TryGetProperty("points", out var ptsEl) || ptsEl.ValueKind != JsonValueKind.Array) return null;
        var points = ptsEl.EnumerateArray().ToArray();
        var ms = mathStatsEl.Value;
        var planiStats = GetProp(ms, "plani");
        var altiStats = GetProp(ms, "alti");
        var threeDStats = GetProp(ms, "threeD");

        XColor? Tercile(JsonElement? statsOpt, double val)
        {
            if (statsOpt == null) return null;
            var s = statsOpt.Value;
            double min = Num(s, "min"), max = Num(s, "max");
            if (max <= min) return null;
            double p33 = Num(s, "p33"), p67 = Num(s, "p67");
            if (val <= p33) return TercileGreen;
            if (val <= p67) return TercileYellow;
            return TercileRed;
        }

        return (rowIdx, col) =>
        {
            if (rowIdx < 0 || rowIdx >= points.Length) return null;
            var p = points[rowIdx];
            return col switch
            {
                10 => Tercile(planiStats, Num(p, "planiDev")),
                11 => Tercile(altiStats, Num(p, "altiDev")),
                12 => Tercile(threeDStats, Num(p, "threeDDev")),
                _ => null
            };
        };
    }

    private static ImplantationTablePayload BuildDetailsTablePayload(JsonElement root)
    {
        var rows = new List<string[]>();
        if (root.TryGetProperty("points", out var ptsEl) && ptsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in ptsEl.EnumerateArray())
            {
                rows.Add(new[]
                {
                    Str(p, "id") ?? "",
                    F3(p, "leveX"), F3(p, "leveY"), F3(p, "leveZ"),
                    F3(p, "controleX"), F3(p, "controleY"), F3(p, "controleZ"),
                    F3(p, "dX"), F3(p, "dY"), F3(p, "dZ"),
                    F3(p, "planiDev"), F3(p, "altiDev"), F3(p, "threeDDev"),
                });
            }
        }
        return new ImplantationTablePayload
        {
            Title = "Détails des Points Comparés (inclus dans l'analyse)",
            SubTitle = "",
            Header = new[] { "ID", "X Levé", "Y Levé", "Z Levé", "X Contr.", "Y Contr.", "Z Contr.", "dX", "dY", "dZ", "Éc. Plani", "Éc. Alti", "Éc. 3D" },
            Rows = rows
        };
    }

    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var result = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (para.Length == 0) { result.Add(""); continue; }
            var words = para.Split(' ');
            var line = "";
            foreach (var w in words)
            {
                var candidate = line.Length == 0 ? w : line + " " + w;
                if (gfx.MeasureString(candidate, font).Width > maxWidth && line.Length > 0)
                {
                    result.Add(line);
                    line = w;
                }
                else line = candidate;
            }
            if (line.Length > 0) result.Add(line);
        }
        return result;
    }

    // Réécrit le pied de page (adresse + version build + "Page N / total") sur TOUTES les pages du
    // document, en une seule passe finale - même principe que FicheSignaletiqueRenderer.RestampFooters
    // (le nombre total de pages n'est connu qu'une fois tout le contenu généré).
    private static void RestampFooters(PdfDocument doc, string buildFooter, JsonElement root)
    {
        int total = doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var page = doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            double yLine = page.Height.Point - Units.MmToPt(14);
            g.DrawLine(NovatlasTheme.GridPenThin(), MarginL, yLine, page.Width.Point - MarginR, yLine);

            // Adresse et numéro de page partagent la même ligne (gauche/droite, jamais assez longs
            // pour se toucher) ; la version build est sur sa PROPRE ligne en dessous - les 3 ne
            // doivent jamais partager exactement le même XRect (l'adresse, longue, chevauchait
            // auparavant le texte centré de version).
            g.DrawString(NovatlasTheme.ResolveFooterAddress(root), NovatlasTheme.FontBody(8), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(2), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                XStringFormats.CenterLeft);

            g.DrawString($"Page {i + 1} / {total}", NovatlasTheme.FontBody(8), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(2), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                XStringFormats.CenterRight);

            if (!string.IsNullOrWhiteSpace(buildFooter))
            {
                g.DrawString(buildFooter, NovatlasTheme.FontBody(8), XBrushes.Black,
                    new XRect(MarginL, yLine + Units.MmToPt(6.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(4.5)),
                    XStringFormats.Center);
            }
        }
    }

    // ---------- Lecture JSON ----------

    private static string? Str(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private static bool? GetBool(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) &&
        (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null;

    private static JsonElement? GetProp(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) ? v : (JsonElement?)null;

    private static string IntStr(JsonElement el, string key) => ((int)Num(el, key)).ToString(CultureInfo.InvariantCulture);
    private static string F3(JsonElement el, string key) => Num(el, key).ToString("F3", CultureInfo.InvariantCulture);
    private static string F3v(JsonElement el, string key) => Num(el, key).ToString("F3", CultureInfo.InvariantCulture);

    private static string FormatDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "N/A";
        var parts = iso.Split('-');
        return parts.Length == 3 ? $"{parts[2]}/{parts[1]}/{parts[0]}" : iso;
    }

    // ---------- Texte réglementaire (annexe, sections 1-2) ----------

    private const string AnnexeIntro =
        "Les trois conditions suivantes doivent être cumulativement respectées. La validation d'une classe de précision, notamment selon le modèle standard, repose sur la vérification de trois conditions principales appliquées aux écarts constatés lors des mesures de contrôle :";

    private const string AnnexeCond1 =
        "N°1. L'écart moyen en position (Emoy pos) de l'échantillon doit être inférieur à une valeur limite calculée. Cette valeur limite est définie par la formule : Emax = [xx] × (1 + 1/(2×C²)) cm\nOù : [xx] représente la classe de précision visée (par exemple, 4 cm, 5 cm, 10 cm, etc.). C est le coefficient de sécurité des mesures de contrôle, qui doit être au moins égal à 2. Ce coefficient exprime que les mesures de contrôle doivent être significativement plus précises que la classe de précision recherchée pour le levé.";

    private const string AnnexeCond2 =
        "N°2. Le nombre d'écarts dépassant un premier seuil (T1) doit être limité. Le premier seuil est défini par la formule : T1 = k × Emax, où k est un coefficient dépendant du type de contrôle. Le nombre d'écarts (N′) autorisés à dépasser ce premier seuil est calculé en fonction du nombre total de points de contrôle (N). L'arrêté mentionne une formule de type : N′ = entier supérieur à (0.01×N + 0.232×√N), équivalent à environ 1% des points pour un modèle gaussien.\nAttention : Si N est inférieur à 5 alors N' = 0";

    private const string AnnexeCond3 =
        "N°3. Aucun des écarts en position ne doit dépasser un second seuil (T2). Ce second seuil est plus strict et est généralement défini par la formule : T2 = 1.5×T1. Cela signifie qu'aucun point contrôlé ne doit présenter un écart excessif, même si la moyenne et le nombre d'écarts modérés sont acceptables.";

    private const string AnnexeBullets =
        "Points importants à considérer pour le contrôle :\n" +
        "• Mesures de contrôle : les mesures doivent être réalisées avec des procédés d'une précision supérieure à celle de la classe de précision recherchée pour le levé, avec un coefficient de sécurité C >= 2.\n" +
        "• Identification des points : les mesures d'écarts s'appliquent sur des points caractéristiques des objets levés, qui doivent être bien identifiés et ne présenter aucun caractère d'ambiguïté.\n" +
        "• Échantillonnage : la taille et la composition de l'échantillon des objets géographiques de contrôle sont précisées par contrat.\n" +
        "• Analyse des écarts : les écarts obtenus sont analysés au regard des prescriptions de l'article 5 de l'arrêté.\n" +
        "• Précision interne vs. globale : l'arrêté permet d'évaluer la précision interne du levé (en s'affranchissant des erreurs de rattachement) si le contrôle est réalisé sur des points géodésiques du levé lui-même, plutôt que sur des points extérieurs.\n" +
        "En résumé, pour qu'un contrôle de précision soit validé, il faut que l'écart moyen, le nombre d'écarts dépassant un premier seuil, et l'absence d'écarts dépassant un second seuil respectent les critères définis par l'arrêté, en tenant compte de la classe de précision visée et du coefficient de sécurité des mesures de contrôle.";

    private const string AnnexeGeoref =
        "L'ensemble du levé de contrôle et des éléments associés doivent être géoréférencés en projection, et levés à l'aide de points GNSS de rattachement terrain. Les points de rattachement seront matérialisés par un repère adapté au terrain, de manière à en assurer la pérennité.\n" +
        "Tout contrôle implique l'emploi de mesures de contrôle fournissant à priori des résultats d'une précision au moins deux fois meilleure que celle des objets à tester. La précision des mesures de contrôle sera déduite des règles de l'art et des connaissances généralement admises par les professionnels.\n" +
        "Une mesure de contrôle n'implique pas nécessairement l'emploi d'autres instruments : on peut souvent obtenir une meilleure précision avec les mêmes instruments et des méthodes opératoires différentes, par exemple des mesures de plus longues durées (cas du GNSS) ou avec plus de réitérations (cas des mesures au théodolite), etc.\n" +
        "L'arrêté indique par ailleurs que l'on ne comptabilise ensemble que les données suivant le même modèle statistique selon la nature du levé. Ainsi, le modèle altimétrique étant différent du modèle planimétrique, des classes de précision et des modalités de vérification des relevés altimétriques et des relevés planimétriques différents s'appliquent.";

    private const string AnnexeBarycentres =
        "Les mesures des barycentres fournies sont des estimations complémentaires aux observations. Elles ne doivent pas être considérées comme des valeurs exactes qui entrent dans la conclusion du rapport. Il est opportun de réserver l'analyse de ces barycentres uniquement pour certains types de travaux topographiques ; les plans topographiques, les plans de récolement de travaux.";

    private const string AnnexeQualiteObjectif =
        "Objectif : L'analyse de la qualité des points topographiques est une étape facultative de ce rapport. Elle vise à évaluer la précision et la cohérence des données topographiques collectées à la stricte condition d'avoir un écart en position E, N, H valide dans le contrôle actuel. On peut sous cette condition, obtenir le calcul d'intervalle de confiance de la moyenne de chaque écart en position.";

    private const string AnnexeQualiteLimites =
        "Limites : Il est important de noter que les méthodes statistiques ont des limitations et doivent être utilisées avec prudence (si Epos (E)x, (N)y, (H)z, > à la classe de précision N, E, H, alors sont pas concernées). La conclusion statistique des points reprise dans ce document, est rendue à titre informative, hors des résultats et conclusions réglementés par l'arrêté du 16 septembre 2003. Elle se limite à valider la valeur d'une moyenne d'écart en position entre 2 limites établies par une loi statistique. Les autres éléments calculés (barycentres, EMQ) sont donnés strictement pour information.";

    private const string AnnexeQualiteRappel =
        "RAPPEL :\nDifférence entre l'écart type et l'Erreur Moyenne Quadratique (EMQ) :\nL'écart type et l'EMQ (Erreur Moyenne Quadratique) sont deux mesures statistiques couramment utilisées pour quantifier la dispersion des données. Cependant, il existe des différences importantes entre ces deux mesures. L'EMQ est une mesure de l'écart entre les valeurs prédites et les valeurs réelles dans un modèle. Elle est calculée en prenant la racine carrée de la moyenne des erreurs quadratiques. Un écart type plus grand indique que les données sont plus dispersées autour de la moyenne. L'écart type est une mesure plus utile que l'écart moyen car il est plus sensible aux changements dans les données.";
}
