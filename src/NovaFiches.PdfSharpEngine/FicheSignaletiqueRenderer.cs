using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Module manager "Fiches signalétiques" : une page A4 vectorielle par ligne du CSV importé
/// (identité, géoréférencement X/Y/Z, carte de situation, photo, observations, révisions).
/// Le payload doit porter les coordonnées lon/lat déjà résolues par ligne (champs "lon"/"lat") :
/// la reprojection (FicheSignaletiqueReprojection) vit dans le projet NovaFiches, inaccessible
/// depuis PdfSharpEngine (référence à sens unique NovaFiches -> PdfSharpEngine, jamais l'inverse
/// - même contrainte que StationPlanRenderer/EnrichStationPlanViewWithLonLat). C'est donc
/// MainForm qui calcule lon/lat avant d'appeler ce renderer.
/// </summary>
internal static class FicheSignaletiqueRenderer
{
    private const double MarginL = 34;
    private const double MarginR = 34;
    private static readonly XColor LineGray = XColor.FromArgb(200, 200, 200);
    private static readonly XColor LightFill = XColor.FromArgb(238, 244, 248);

    // Voir PhotoAppendixRenderer._currentRoot / ImplantationFullReportRenderer._currentRoot :
    // RenderRow n'a pas "root" dans toute sa chaîne d'appel interne (DrawBlock, DrawRevisions,
    // ...), donc même pattern de champ statique ambiant pour le branding.
    private static JsonElement _currentRoot;

    public static void AppendFromPayload(PdfDocument doc, string payloadJson, string buildFooter)
    {
        JsonElement root;
        JsonElement fiches;
        try
        {
            using var jd = JsonDocument.Parse(payloadJson);
            root = jd.RootElement.Clone();
            _currentRoot = root;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("fichesSignaletiques", out fiches) ||
                fiches.ValueKind != JsonValueKind.Object)
                return;
        }
        catch
        {
            return;
        }

        if (!fiches.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return;

        string mapProvider = GetStr(fiches, "mapProvider") ?? "plan";
        int defaultZoom = (int)(GetNum(fiches, "defaultZoom") ?? 19);

        int before = doc.PageCount;
        foreach (var row in rowsEl.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var g = XGraphics.FromPdfPage(page);
            try
            {
                RenderRow(g, page, row, mapProvider, defaultZoom);
            }
            catch
            {
                // Une fiche en erreur (photo/carte corrompue, etc.) ne doit pas faire
                // disparaître les autres pages déjà générées.
            }
        }

        if (doc.PageCount > before)
            RestampFooters(doc, buildFooter, before);
    }

    private static void RenderRow(XGraphics g, PdfPage page, JsonElement row, string mapProvider, int defaultZoom)
    {
        double contentW = page.Width.Point - MarginL - MarginR;
        double y = Units.MmToPt(8);

        // ===== En-tête : logo + bandeau titre (Dossier déplacé dans le cadre Informations
        // générales, Chantier retiré) =====
        double headerH = Units.MmToPt(16);
        double logoW = Units.MmToPt(42);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), MarginL, y, logoW, headerH);
        var logo = NovatlasTheme.ResolveLogo(_currentRoot);
        if (logo != null)
        {
            double pad = Units.MmToPt(3);
            double maxW = logoW - pad * 2, maxH = headerH - pad * 2;
            double ar = (double)logo.PixelWidth / Math.Max(1, logo.PixelHeight);
            double iw = maxW, ih = iw / ar;
            if (ih > maxH) { ih = maxH; iw = ih * ar; }
            g.DrawImage(logo, MarginL + (logoW - iw) / 2.0, y + (headerH - ih) / 2.0, iw, ih);
        }

        double titleBandX = MarginL + logoW + Units.MmToPt(4);
        double titleBandW = contentW - logoW - Units.MmToPt(4);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveBlue(_currentRoot)), titleBandX, y, titleBandW, headerH);
        g.DrawString("FICHE SIGNALÉTIQUE", NovatlasTheme.FontBold(14), XBrushes.White,
            new XRect(titleBandX, y, titleBandW, headerH), XStringFormats.Center);
        y += headerH + Units.MmToPt(4);

        double titleH = Units.MmToPt(9);
        string pointName = First(GetStr(row, "point"), GetStr(row, "idFiche"), "Point non renseigné");
        g.DrawRectangle(new XPen(XColors.Black, 0.8), new XSolidBrush(LightFill), MarginL, y, contentW, titleH);
        g.DrawString(pointName, NovatlasTheme.FontBold(12), XBrushes.Black,
            new XRect(MarginL, y, contentW, titleH), XStringFormats.Center);
        y += titleH + Units.MmToPt(5);

        // ===== Blocs Informations générales / Géoréférencement =====
        // Même largeur de colonnes (et même écart) que les blocs carte/photo plus bas, pour que
        // les 4 cadres s'alignent verticalement (bord gauche, séparation centrale, bord droit).
        double blockGap = Units.MmToPt(5);
        double leftW = (contentW - blockGap) / 2.0;
        double rightW = leftW;
        double blockTop = y;
        // Dessiné en premier : le cadre "Informations générales" est ensuite étiré pour
        // s'aligner sur sa hauteur (voir forcedTotalHeight plus bas), quel que soit le nombre
        // de champs renseignés de chaque côté.
        double geoBottom = DrawGeoreferencingBlock(g, MarginL + leftW + blockGap, blockTop, rightW, row);

        // "Type de fiche" est le seul champ conditionnel du modèle d'origine (masqué si vide) :
        // tous les autres s'affichent toujours, valeur vide ou non (voir DrawInfoBlock). Dossier
        // vient de l'en-tête (Chantier retiré, voir plus haut) - il occupe l'espace libéré par
        // l'alignement sur la hauteur du cadre Géoréférencement.
        var infoRows = new List<(string, string?)>
        {
            ("Dossier", GetStr(row, "dossier")),
            ("Client / MOA", GetStr(row, "client")),
            ("Prestataire", First(GetStr(row, "prestataire"), "NOVATLAS")),
            ("Commune", GetStr(row, "commune")),
            ("Département", GetStr(row, "departement")),
            ("Adresse", GetStr(row, "adresse")),
            ("Site / opération", GetStr(row, "site")),
            ("Nature", GetStr(row, "nature")),
            ("Date détermination", GetStr(row, "dateDetermination")),
            ("Date édition", GetStr(row, "dateEdition")),
        };
        var typeFiche = GetStr(row, "typeFiche");
        if (!string.IsNullOrWhiteSpace(typeFiche))
            infoRows.Insert(0, ("Type de fiche", typeFiche));

        double leftBottom = DrawInfoBlock(g, MarginL, blockTop, leftW, "INFORMATIONS GÉNÉRALES", infoRows.ToArray(), geoBottom - blockTop);
        y = Math.Max(leftBottom, geoBottom) + Units.MmToPt(5);

        // ===== Médias : carte de situation + photo =====
        // Blocs volontairement plus grands que le modèle d'origine (72mm) : la page a de la
        // place libre en bas (une seule fiche par page, pas de contenu variable en dessous),
        // autant l'utiliser plutôt que de laisser du blanc.
        double mediaH = Units.MmToPt(82);
        DrawMapBlock(g, MarginL, y, leftW, mediaH, row, mapProvider, defaultZoom);
        DrawPhotoBlock(g, MarginL + leftW + blockGap, y, rightW, mediaH, row);
        y += mediaH + Units.MmToPt(5);

        // ===== Observations =====
        double obsH = Units.MmToPt(30);
        DrawBlockFrame(g, MarginL, y, contentW, obsH, "OBSERVATIONS");
        var obsText = First(GetStr(row, "observations"), "Aucune observation renseignée.");
        g.DrawString(obsText, NovatlasTheme.FontBody(9), XBrushes.Black,
            new XRect(MarginL + Units.MmToPt(3), y + Units.MmToPt(7), contentW - Units.MmToPt(6), obsH - Units.MmToPt(9)),
            XStringFormats.TopLeft);
        y += obsH + Units.MmToPt(5);

        // ===== Tableau des révisions =====
        // Collé au bas de page plutôt qu'au fil de la mise en page : hauteur compacte, mais
        // positionné à partir du bas (voir DrawRevisionsTable), pour rester "collé" même
        // quand de nouveaux indices s'ajoutent au fil du temps (table plus haute -> son bord
        // haut remonte, son bord bas reste au même endroit).
        double footerTop = page.Height.Point - Units.MmToPt(20);
        DrawRevisionsTable(g, MarginL, contentW, row, y, footerTop);
    }

    // forcedTotalHeight : permet d'aligner la hauteur du cadre sur celle d'un autre bloc
    // (ex. "Informations générales" aligné sur "Géoréférencement"). Ne réduit jamais en
    // dessous de la hauteur naturelle du contenu (Math.Max) - l'espace en trop reste blanc
    // en bas du cadre plutôt que de tronquer une ligne.
    private static double DrawInfoBlock(XGraphics g, double x, double y, double w, string title, (string Label, string? Value)[] rows, double? forcedTotalHeight = null)
    {
        double titleH = Units.MmToPt(6);
        double rowH = Units.MmToPt(4.6);
        double naturalTotalH = titleH + rowH * rows.Length + Units.MmToPt(2);
        double totalH = forcedTotalHeight.HasValue ? Math.Max(forcedTotalHeight.Value, naturalTotalH) : naturalTotalH;

        // Bandeau orange (couleur secondaire NOVATLAS), comme les en-têtes de section des
        // autres fiches PDF de l'appli (ex. StationReportRenderer.DrawBar) - remplace le gris
        // utilisé initialement ici.
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveOrange(_currentRoot)), x, y, w, titleH);
        g.DrawRectangle(new XPen(LineGray, 0.6), x, y, w, totalH);
        g.DrawLine(new XPen(LineGray, 0.6), x, y + titleH, x + w, y + titleH);
        g.DrawString(title, NovatlasTheme.FontBold(8.5), XBrushes.White,
            new XRect(x + Units.MmToPt(2), y, w - Units.MmToPt(4), titleH), XStringFormats.CenterLeft);

        double ry = y + titleH + Units.MmToPt(1);
        double labelW = Units.MmToPt(38);
        foreach (var (label, value) in rows)
        {
            // Contrairement à une première version de ce renderer, une valeur vide n'efface
            // plus la ligne : le modèle d'origine (kv-grid) affiche toujours le label, y
            // compris pour un champ non renseigné (ex. Département vide avant géocodage).
            g.DrawString(label, NovatlasTheme.FontBold(8), XBrushes.Black,
                new XRect(x + Units.MmToPt(2), ry, labelW, rowH), XStringFormats.CenterLeft);
            g.DrawString(value ?? "", NovatlasTheme.FontBody(8), XBrushes.Black,
                new XRect(x + labelW + Units.MmToPt(2), ry, w - labelW - Units.MmToPt(4), rowH), XStringFormats.CenterLeft);
            ry += rowH;
        }

        return y + totalH;
    }

    private static double DrawGeoreferencingBlock(XGraphics g, double x, double y, double w, JsonElement row)
    {
        var geoRows = new (string, string?)[]
        {
            ("Système plani", GetStr(row, "systemePlani")),
            ("Système alti", GetStr(row, "systemeAlti")),
            ("EPSG source", GetStr(row, "epsgSource")),
            ("PPM", First(GetStr(row, "ppm"), "Non renseigné")),
            ("Coord. WGS84", FormatLonLat(row)),
        };
        double afterKv = DrawInfoBlock(g, x, y, w, "GÉORÉFÉRENCEMENT", geoRows);

        // Empilées verticalement (une par ligne, pleine largeur) comme le modèle d'origine
        // (.coords : grid-template-columns 1fr) - trois colonnes côte à côte laissaient trop peu
        // de largeur à chaque valeur et la tronquaient.
        double boxTop = afterKv + Units.MmToPt(2);
        double boxH = Units.MmToPt(11);
        double gap = Units.MmToPt(2);
        DrawCoordBox(g, x, boxTop, w, boxH, "X", GetNum(row, "x"));
        DrawCoordBox(g, x, boxTop + boxH + gap, w, boxH, "Y", GetNum(row, "y"));
        DrawCoordBox(g, x, boxTop + (boxH + gap) * 2, w, boxH, "Z", GetNum(row, "z"));

        return boxTop + boxH * 3 + gap * 2;
    }

    // Valeur en gros/gras/bleu, comme le modèle d'origine (.coord-box .value : 15px, bleu,
    // font-weight 800) - c'est la donnée la plus consultée sur le terrain, elle doit rester
    // lisible de loin, contrairement au reste du bloc (libellés en petit).
    private static void DrawCoordBox(XGraphics g, double x, double y, double w, double h, string label, double? value)
    {
        g.DrawRectangle(new XPen(LineGray, 0.6), new XSolidBrush(XColor.FromArgb(250, 251, 255)), x, y, w, h);
        string text = value.HasValue ? value.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "—";
        double labelW = Units.MmToPt(7);
        g.DrawString(label, NovatlasTheme.FontBody(9), new XSolidBrush(XColor.FromArgb(110, 120, 130)),
            new XRect(x + Units.MmToPt(2.5), y, labelW, h), XStringFormats.CenterLeft);
        g.DrawString(text, NovatlasTheme.FontBold(13), new XSolidBrush(NovatlasTheme.ResolveBlue(_currentRoot)),
            new XRect(x + labelW + Units.MmToPt(1.5), y, w - labelW - Units.MmToPt(4), h), XStringFormats.CenterLeft);
    }

    private static void DrawBlockFrame(XGraphics g, double x, double y, double w, double h, string title)
    {
        double titleH = Units.MmToPt(6);
        g.DrawRectangle(new XPen(LineGray, 0.6), x, y, w, h);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveOrange(_currentRoot)), x, y, w, titleH);
        g.DrawLine(new XPen(LineGray, 0.6), x, y + titleH, x + w, y + titleH);
        g.DrawString(title, NovatlasTheme.FontBold(8.5), XBrushes.White,
            new XRect(x + Units.MmToPt(2), y, w - Units.MmToPt(4), titleH), XStringFormats.CenterLeft);
    }

    private static void DrawMapBlock(XGraphics g, double x, double y, double w, double h, JsonElement row, string mapProvider, int defaultZoom)
    {
        DrawBlockFrame(g, x, y, w, h, "SITUATION / FOND DE PLAN");
        double titleH = Units.MmToPt(6);
        var inner = new XRect(x, y + titleH, w, h - titleH);

        double? lon = GetNum(row, "lon");
        double? lat = GetNum(row, "lat");
        if (!lon.HasValue || !lat.HasValue)
        {
            DrawPlaceholder(g, inner, "Aucune coordonnée WGS84 exploitable.");
            return;
        }

        int zoom = Math.Clamp((int)(GetNum(row, "zoom") ?? defaultZoom), 10, 19);

        try
        {
            double cx = MapTileFetcher.LonToTileX(lon.Value, zoom);
            double cy = MapTileFetcher.LatToTileY(lat.Value, zoom);
            int txCenter = (int)Math.Floor(cx);
            int tyCenter = (int)Math.Floor(cy);
            int txMin = txCenter - 1, txMax = txCenter + 1;
            int tyMin = tyCenter - 1, tyMax = tyCenter + 1;

            // Task.Run : voir StationPlanRenderer, même précaution anti-deadlock sync-over-async
            // (AppendFromPayload tourne sur le thread UI WinForms qui a un SynchronizationContext).
            using var bitmap = Task.Run(() => MapTileFetcher.FetchAndStitchTilesAsync(txMin, tyMin, txMax, tyMax, zoom, mapProvider))
                .GetAwaiter().GetResult();
            if (bitmap == null)
            {
                DrawPlaceholder(g, inner, "Fond de carte indisponible.");
                return;
            }

            double bitmapW = (txMax - txMin + 1) * 256.0;
            double bitmapH = (tyMax - tyMin + 1) * 256.0;
            double scale = Math.Max(inner.Width / bitmapW, inner.Height / bitmapH);
            double usedW = bitmapW * scale, usedH = bitmapH * scale;
            double offX = inner.X - (usedW - inner.Width) / 2.0;
            double offY = inner.Y - (usedH - inner.Height) / 2.0;

            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                using var ximg = XImage.FromStream(ms);
                var saveState = g.Save();
                g.IntersectClip(inner);
                g.DrawImage(ximg, new XRect(offX, offY, usedW, usedH));
                g.Restore(saveState);
            }

            // Position du marqueur dans l'espace bitmap (avant recadrage), puis conversion écran.
            double bx = (MapTileFetcher.LonToTileX(lon.Value, zoom) - txMin) * 256.0;
            double by = (MapTileFetcher.LatToTileY(lat.Value, zoom) - tyMin) * 256.0;
            double px = offX + bx * scale;
            double py = offY + by * scale;

            double r = 4.2;
            g.DrawEllipse(new XPen(XColors.White, 1.4), new XSolidBrush(NovatlasTheme.ResolveBlue(_currentRoot)),
                px - r, py - r, r * 2, r * 2);
        }
        catch
        {
            DrawPlaceholder(g, inner, "Fond de carte indisponible.");
        }
    }

    private static void DrawPhotoBlock(XGraphics g, double x, double y, double w, double h, JsonElement row)
    {
        DrawBlockFrame(g, x, y, w, h, "PHOTO");
        double titleH = Units.MmToPt(6);
        var inner = new XRect(x, y + titleH, w, h - titleH);

        var photo = GetStr(row, "photoImageData");
        if (string.IsNullOrWhiteSpace(photo))
        {
            DrawPlaceholder(g, inner, "Aucune photo associée.");
            return;
        }

        DrawImage(g, photo!, inner);
    }

    private static void DrawPlaceholder(XGraphics g, XRect box, string text)
    {
        g.DrawString(text, NovatlasTheme.FontBody(8.5), new XSolidBrush(XColor.FromArgb(110, 120, 130)),
            box, XStringFormats.Center);
    }

    // Collé au bas de page (bord bas fixe à "footerTop"), jamais au-dessus de "minY" (juste
    // après Observations) : une fiche qui accumule des indices au fil des révisions voit son
    // tableau grandir vers le HAUT (bord bas inchangé), jusqu'à, dans le cas extrême de
    // beaucoup de révisions, buter sur Observations plutôt que de la chevaucher.
    private static void DrawRevisionsTable(XGraphics g, double x, double w, JsonElement row, double minY, double footerTop)
    {
        var revisions = new List<(string Indice, string Date, string Description, string Auteur)>();
        if (row.TryGetProperty("revisions", out var revArr) && revArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in revArr.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                revisions.Add((GetStr(r, "indice") ?? "", GetStr(r, "date") ?? "", GetStr(r, "description") ?? "", GetStr(r, "auteur") ?? ""));
            }
        }
        if (revisions.Count == 0)
        {
            revisions.Add((GetStr(row, "indice") ?? "", GetStr(row, "dateEdition") ?? "", "Création de la fiche", ""));
        }

        double headerH = Units.MmToPt(6);
        double rowH = Units.MmToPt(5.2);
        double totalH = headerH + rowH * revisions.Count;
        double y = Math.Max(minY, footerTop - totalH);
        double c1 = w * 0.14, c2 = w * 0.20, c4 = w * 0.20, c3 = w - c1 - c2 - c4;

        var pen = new XPen(LineGray, 0.6);
        g.DrawRectangle(pen, x, y, w, totalH);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveOrange(_currentRoot)), x, y, w, headerH);
        g.DrawLine(pen, x, y + headerH, x + w, y + headerH);
        g.DrawLine(pen, x + c1, y, x + c1, y + totalH);
        g.DrawLine(pen, x + c1 + c2, y, x + c1 + c2, y + totalH);
        g.DrawLine(pen, x + c1 + c2 + c3, y, x + c1 + c2 + c3, y + totalH);

        var headFont = NovatlasTheme.FontBold(8);
        g.DrawString("Indice", headFont, XBrushes.White, new XRect(x + Units.MmToPt(1.5), y, c1, headerH), XStringFormats.CenterLeft);
        g.DrawString("Date", headFont, XBrushes.White, new XRect(x + c1 + Units.MmToPt(1.5), y, c2, headerH), XStringFormats.CenterLeft);
        g.DrawString("Description", headFont, XBrushes.White, new XRect(x + c1 + c2 + Units.MmToPt(1.5), y, c3, headerH), XStringFormats.CenterLeft);
        g.DrawString("Dessiné par", headFont, XBrushes.White, new XRect(x + c1 + c2 + c3 + Units.MmToPt(1.5), y, c4, headerH), XStringFormats.CenterLeft);

        double ry = y + headerH;
        var cellFont = NovatlasTheme.FontBody(8);
        foreach (var rev in revisions)
        {
            g.DrawLine(pen, x, ry, x + w, ry);
            g.DrawString(rev.Indice, cellFont, XBrushes.Black, new XRect(x + Units.MmToPt(1.5), ry, c1, rowH), XStringFormats.CenterLeft);
            g.DrawString(rev.Date, cellFont, XBrushes.Black, new XRect(x + c1 + Units.MmToPt(1.5), ry, c2, rowH), XStringFormats.CenterLeft);
            g.DrawString(rev.Description, cellFont, XBrushes.Black, new XRect(x + c1 + c2 + Units.MmToPt(1.5), ry, c3 - Units.MmToPt(3), rowH), XStringFormats.CenterLeft);
            g.DrawString(rev.Auteur, cellFont, XBrushes.Black, new XRect(x + c1 + c2 + c3 + Units.MmToPt(1.5), ry, c4, rowH), XStringFormats.CenterLeft);
            ry += rowH;
        }
    }

    private static string FormatLonLat(JsonElement row)
    {
        var lon = GetNum(row, "lon");
        var lat = GetNum(row, "lat");
        if (!lon.HasValue || !lat.HasValue) return "Non disponible";
        return lat.Value.ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture) + ", "
             + lon.Value.ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void DrawImage(XGraphics g, string dataUrl, XRect box)
    {
        try
        {
            var bytes = DecodeDataUrl(dataUrl);
            if (bytes == null || bytes.Length == 0)
            {
                DrawPlaceholder(g, box, "Photo illisible.");
                return;
            }
            using var ms = new MemoryStream(bytes);
            using var img = XImage.FromStream(ms);
            if (img.PixelWidth <= 0 || img.PixelHeight <= 0) return;
            double s = Math.Min(box.Width / img.PixelWidth, box.Height / img.PixelHeight);
            double w = img.PixelWidth * s, h = img.PixelHeight * s;
            double px = box.Left + (box.Width - w) / 2.0;
            double py = box.Top + (box.Height - h) / 2.0;
            g.DrawImage(img, px, py, w, h);
        }
        catch
        {
            DrawPlaceholder(g, box, "Photo illisible.");
        }
    }

    private static byte[]? DecodeDataUrl(string s)
    {
        try
        {
            s = (s ?? "").Trim();
            int idx = s.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) s = s[(idx + "base64,".Length)..];
            return Convert.FromBase64String(s);
        }
        catch { return null; }
    }

    // Deuxième passe (comme PhotoAppendixRenderer/StationReportRenderer) : la pagination totale
    // n'est connue qu'une fois toutes les fiches ajoutées, donc le pied de page (adresse + build +
    // "Page N / total") est réécrit après coup sur les pages ajoutées par cet appel uniquement
    // (de "firstPageIndex" à la fin), sans toucher aux pages déjà présentes dans le document
    // (ex: page de garde ajoutée par un autre renderer avant cet appel).
    private static void RestampFooters(PdfDocument doc, string buildFooter, int firstPageIndex)
    {
        int total = doc.PageCount - firstPageIndex;
        for (int i = firstPageIndex; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            double yLine = page.Height.Point - Units.MmToPt(14);
            double wipeY = yLine - Units.MmToPt(1);
            g.DrawRectangle(XBrushes.White, new XRect(0, wipeY, page.Width.Point, page.Height.Point - wipeY));
            g.DrawLine(new XPen(LineGray, 0.4), MarginL, yLine, page.Width.Point - MarginR, yLine);

            g.DrawString(NovatlasTheme.ResolveFooterAddress(_currentRoot), NovatlasTheme.FontBody(9), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                XStringFormats.Center);

            if (!string.IsNullOrWhiteSpace(buildFooter))
            {
                g.DrawString(buildFooter, NovatlasTheme.FontBody(8), XBrushes.Black,
                    new XRect(MarginL, yLine + Units.MmToPt(6.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(4.5)),
                    XStringFormats.Center);
            }

            g.DrawString($"Page {i - firstPageIndex + 1} / {total}", NovatlasTheme.FontBody(9), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                XStringFormats.CenterRight);
        }
    }

    private static string? GetStr(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined) return null;
        return v.ToString();
    }

    private static double? GetNum(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
    }

    private static string First(params string?[] vals)
    {
        foreach (var v in vals)
            if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return "";
    }
}
