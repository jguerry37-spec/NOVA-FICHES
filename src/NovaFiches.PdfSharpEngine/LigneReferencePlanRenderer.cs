using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Annexe graphique optionnelle du module "Ligne de référence" : dessine chaque ligne de contrôle
/// (root.ligneRef[]) - la ligne théorique, chaque point contrôlé (mesuré + sa position théorique
/// sur la ligne) et sa cotation (chaînage/décalage). Purement additif - LigneReferenceReportRenderer.cs
/// (tableau existant) n'est pas modifié.
///
/// Deux régimes selon l'étendue de la ligne (extent, en mètres) :
///   - "compact" (contrôle ponctuel court, ex. percement/joint/fissure) : regroupées par proximité
///     spatiale réelle en "zones" (cercles de recherche de ClusterRadiusMeters), une zone = une page
///     A3 paysage à une échelle normalisée réelle (1:N), pas des vignettes indépendantes rééchelonnées
///     une par une - constaté sur un cas réel qu'une grille de vignettes perdait la position relative
///     réelle des points entre eux, ce que l'utilisateur veut au contraire pouvoir lire.
///   - "pleine page" (ligne longue, ex. façade avec plusieurs points implantés le long du linéaire) :
///     une page A4 dédiée, avec rotation paysage/portrait auto (régime inchangé depuis la Phase 1).
///
/// Chaque page porte en plus un encart "Situation" (locator) qui repositionne sa zone au sein de
/// l'ensemble des zones du document, pour se repérer sans dérouler tout le rapport.
///
/// N'ajoute rien au document si le payload ne porte pas includeGraphicalPlan=true (case à cocher
/// optionnelle côté UI, m03c_pdf_reports_export.js) - même principe opt-in que stationPlanView
/// pour StationPlanRenderer.
///
/// Ne dessine pas son propre pied de page : appelé AVANT PhotoAppendixRenderer.AppendFromPayload
/// dans PdfSharpReports.GenerateLigneReferenceFromJson, dont le RestampFooters(doc, buildFooter)
/// final retamponne toutes les pages du document (y compris celles-ci).
/// </summary>
public static class LigneReferencePlanRenderer
{
    private const double MarginL = 36;
    private const double MarginR = 36;
    private const double MarginT = 36;

    // Une ligne dont l'étendue (ligne + tous ses points, chaînage compris) dépasse ce seuil est
    // considérée "longue" et garde sa propre page A4 ; en dessous, elle rejoint le regroupement
    // spatial par zones.
    private const double CompactExtentThresholdMeters = 3.0;

    // Rayon de recherche du regroupement glouton par zones : deux lignes compactes dont les centres
    // sont à moins de ClusterRadiusMeters l'un de l'autre finissent sur la même page de zone.
    private const double ClusterRadiusMeters = 10.0;

    // Au-delà de ce nombre de lignes, une zone est encore trop dense pour rester lisible sur une
    // seule page A3 (retour utilisateur après vérification visuelle) : on la scinde en 2.
    private const int MaxLinesPerZone = 15;

    // Échelles "normalisées" candidates (1:N) pour les pages de zone - convention des plans
    // techniques usuels, étendue vers le bas pour les zones très resserrées (quelques mètres).
    private static readonly int[] NiceScaleDenominators = { 5, 10, 20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000, 1500, 2000, 2500, 5000 };

    private static readonly XColor LineGray = XColor.FromArgb(200, 200, 200);
    private static readonly XColor MeasuredColor = XColor.FromArgb(37, 99, 235);
    private static readonly XColor TheoColor = XColor.FromArgb(120, 120, 120);
    private static readonly XColor ProjectorColor = XColor.FromArgb(150, 150, 150);
    private static readonly XColor ZoneRingColor = XColor.FromArgb(190, 37, 99, 235);

    private static string GetStr(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static double? GetNum(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return ds;
        return null;
    }

    private static JsonElement? GetObj(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : (JsonElement?)null;

    private static bool GetBool(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return false;
        return v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b);
    }

    // Snap sur une longueur "ronde" pour la barre d'échelle - étendu vers le bas (cm) car les lignes
    // de contrôle ponctuel (percements, joints) mesurent souvent moins d'un mètre.
    private static double NiceScaleLength(double target)
    {
        double[] nice = { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500 };
        double best = nice[0];
        foreach (var v in nice) { if (v <= target) best = v; else break; }
        return best;
    }

    private static string FormatWorldLength(double meters) =>
        meters < 1 ? $"{Math.Round(meters * 100)} cm" : $"{meters:0.##} m";

    // Choisit la plus petite échelle normalisée (1:N, la plus "zoomée") dont le contenu (déjà
    // marge comprise) tient encore dans le cadre disponible - même principe de snap que
    // NiceScaleLength, appliqué au ratio d'échelle plutôt qu'à une longueur.
    private static int ChooseScaleDenominator(double worldWMeters, double worldHMeters, double frameWPt, double frameHPt)
    {
        double reqPtPerMeter = Math.Min(frameWPt / Math.Max(0.1, worldWMeters), frameHPt / Math.Max(0.1, worldHMeters));
        foreach (var n in NiceScaleDenominators)
        {
            double ptPerMeter = Units.MmToPt(1000.0 / n);
            if (ptPerMeter <= reqPtPerMeter) return n;
        }
        return NiceScaleDenominators[^1];
    }

    // Positions candidates (8 directions) pour l'anti-collision des étiquettes - même principe que
    // RecolementPlanViewRenderer : essentiel ici car des points contrôlés à quelques centimètres les
    // uns des autres (plusieurs visées d'un même repère) se retrouvent à quelques millimètres l'un
    // de l'autre une fois à l'échelle réelle (1:N), sous peine d'étiquettes totalement superposées.
    private static readonly (int dx, int dy)[] LabelDirections =
    {
        ( 1, -1), (-1, -1), ( 1,  1), (-1,  1),
        ( 1,  0), (-1,  0), ( 0, -1), ( 0,  1),
    };

    // Étiquette "Zone N — p.X" de la page de vue générale : cherche une position libre autour du
    // cercle de la zone (8 directions, rayon croissant à partir du bord du cercle) parmi les
    // étiquettes déjà posées, pour qu'un site aux zones rapprochées ne fasse pas chevaucher les
    // étiquettes entre elles (ce qui donnait l'impression que certaines zones manquaient).
    private static void PlaceZoneLabel(XGraphics g, XPoint center, double ringPx, string text, XFont font, XBrush brush, List<XRect> occupied, XRect bounds)
    {
        var size = g.MeasureString(text, font);
        double w = size.Width, h = size.Height;

        bool IsFree(XRect rect)
        {
            foreach (var o in occupied) if (o.IntersectsWith(rect)) return false;
            return true;
        }

        void Draw(XRect rect)
        {
            var halo = XColor.FromArgb(200, 255, 255, 255);
            g.DrawRectangle(new XSolidBrush(halo), rect);
            g.DrawString(text, font, brush, rect, XStringFormats.TopLeft);
            occupied.Add(rect);
        }

        for (int step = 0; step <= 8; step++)
        {
            double off = ringPx + 4 + step * 7;
            foreach (var dir in LabelDirections)
            {
                double x = center.X + dir.dx * off;
                double y = center.Y + dir.dy * off;
                if (dir.dx < 0) x -= w;
                if (dir.dy < 0) y -= h;
                var rr = new XRect(x, y, w, h);
                if (!bounds.Contains(rr)) continue;
                if (IsFree(rr))
                {
                    Draw(rr);
                    return;
                }
            }
        }

        Draw(new XRect(center.X - w / 2, center.Y - ringPx - h - 3, w, h));
    }

    private static double Cross(XPoint o, XPoint a, XPoint b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static bool SegmentsIntersect(XPoint p1, XPoint p2, XPoint p3, XPoint p4)
    {
        double d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2);
        double d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    // Un rectangle d'étiquette est considéré "sur le graphisme" s'il contient une extrémité d'un
    // segment (ligne, extension, projecteur) ou si ce segment traverse un de ses 4 côtés.
    private static bool RectIntersectsSegment(XRect r, XPoint a, XPoint b)
    {
        if (r.Contains(a) || r.Contains(b)) return true;
        var tl = new XPoint(r.X, r.Y); var tr = new XPoint(r.Right, r.Y);
        var br = new XPoint(r.Right, r.Bottom); var bl = new XPoint(r.X, r.Bottom);
        return SegmentsIntersect(a, b, tl, tr) || SegmentsIntersect(a, b, tr, br)
            || SegmentsIntersect(a, b, br, bl) || SegmentsIntersect(a, b, bl, tl);
    }

    private static double PointToSegmentDist(XPoint p, XPoint a, XPoint b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 1e-9) return Dist(p.X, p.Y, a.X, a.Y);
        double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2;
        t = Math.Max(0, Math.Min(1, t));
        return Dist(p.X, p.Y, a.X + t * abx, a.Y + t * aby);
    }

    // Distance minimale entre 2 segments (0 s'ils se croisent).
    private static double SegmentToSegmentDist(XPoint a1, XPoint a2, XPoint b1, XPoint b2)
    {
        if (SegmentsIntersect(a1, a2, b1, b2)) return 0;
        return Math.Min(
            Math.Min(PointToSegmentDist(a1, b1, b2), PointToSegmentDist(a2, b1, b2)),
            Math.Min(PointToSegmentDist(b1, a1, a2), PointToSegmentDist(b2, a1, a2)));
    }

    // Distance minimale entre la ligne de rappel (point -> étiquette) et tout le graphisme déjà
    // tracé - une ligne de rappel qui PASSE PRÈS d'un autre segment (sans forcément le toucher) donne
    // l'illusion visuelle qu'elle prolonge cette autre ligne, repéré sur un motif de plusieurs lignes
    // voisines à orientation quasi identique (percements alignés) où un décalage "de biais" par
    // rapport à SA PROPRE ligne peut malgré tout finir presque colinéaire avec la ligne adjacente.
    // Utilisée comme SCORE (pas un filtre dur) : voir PlaceCallout, qui garde toujours le meilleur
    // candidat sans chevauchement plutôt que d'exiger un dégagement impossible à tenir dans un amas
    // très dense (sinon on force un repli vers un placement non anti-collision, pire que le défaut).
    private static double MinLeaderClearance(XPoint from, XPoint to, List<(XPoint a, XPoint b)> avoidSegments)
    {
        double min = double.MaxValue;
        foreach (var seg in avoidSegments)
        {
            // Ignore les segments directement rattachés à CE point (typiquement son propre
            // projecteur mesuré->théorique) : la ligne de rappel démarre AU point, donc elle "touche"
            // toujours son propre projecteur à distance 0 - ça ne crée pas l'illusion visuelle visée
            // (confusion avec une AUTRE ligne), juste le fait normal qu'ils partagent une extrémité.
            if (Dist(seg.a.X, seg.a.Y, from.X, from.Y) < 0.5 || Dist(seg.b.X, seg.b.Y, from.X, from.Y) < 0.5) continue;
            double d = SegmentToSegmentDist(from, to, seg.a, seg.b);
            if (d < min) min = d;
        }
        return min;
    }

    // Étiquette groupée d'un point contrôlé : n° de point (noir) + cotation dL/dT (orange) empilés
    // dans un même encart, systématiquement relié au point implanté par une ligne de rappel - même
    // s'il est tout proche - pour que le lien "cet encart appartient à ce point" reste toujours
    // explicite (retour utilisateur). Cherche une position libre autour de pt (8 directions, rayons
    // croissants, taille de police décroissante en dernier recours) qui n'empiète ni sur une
    // étiquette déjà posée, ni sur le graphisme (ligne, extension, projecteurs) déjà tracé sur la
    // page ; si aucune position libre n'est trouvée, colle l'encart près du point quitte à
    // chevaucher (on préfère toujours afficher l'info plutôt que la perdre).
    // Angles (en degrés, relatifs à la direction de la ligne) essayés pour poser l'encart - toujours
    // de biais (diagonales d'abord, puis quasi-perpendiculaires) et JAMAIS 0°/180°, qui aligneraient
    // la ligne de rappel dans le prolongement de la ligne de référence (confusion signalée).
    private static readonly double[] CalloutAngleDegrees = { 45, 135, 225, 315, 60, 120, 240, 300, 90, 270 };

    private static (double dx, double dy)[] ObliqueDirections((double ux, double uy) lineDir)
    {
        var dirs = new (double, double)[CalloutAngleDegrees.Length];
        for (int i = 0; i < CalloutAngleDegrees.Length; i++)
        {
            double rad = CalloutAngleDegrees[i] * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            dirs[i] = (lineDir.ux * cos - lineDir.uy * sin, lineDir.ux * sin + lineDir.uy * cos);
        }
        return dirs;
    }

    private static void PlaceCallout(XGraphics g, XPoint pt, string idText, string coteText, XBrush idBrush, XBrush coteBrush, XRect bounds, List<XRect> occupied, List<(XPoint a, XPoint b)> avoidSegments, (double ux, double uy) lineDir, double maxFontSize = 7.5, double minFontSize = 5.5)
    {
        if (string.IsNullOrWhiteSpace(idText)) return;
        bool hasCote = !string.IsNullOrWhiteSpace(coteText);
        var directions = ObliqueDirections(lineDir);

        bool IsFree(XRect rect)
        {
            foreach (var o in occupied) if (o.IntersectsWith(rect)) return false;
            foreach (var seg in avoidSegments) if (RectIntersectsSegment(rect, seg.a, seg.b)) return false;
            return true;
        }

        void Place(double x, double y, double w, double idH, double coteH, XFont idFont, XFont coteFont)
        {
            var union = new XRect(x, y, w, idH + (hasCote ? coteH + 1 : 0));
            double cx = union.X + union.Width / 2.0, cy = union.Y + union.Height / 2.0;
            // Ligne de rappel étiquette -> point : simple repère visuel (pas une donnée), en
            // pointillés fins et clairs pour ne pas se confondre avec le projecteur (mesuré ->
            // théorique) ou une ligne de référence.
            g.DrawLine(new XPen(XColor.FromArgb(180, 180, 180), 0.35) { DashStyle = XDashStyle.Dot }, pt, new XPoint(cx, cy));
            var halo = XColor.FromArgb(190, 255, 255, 255);
            g.DrawRectangle(new XSolidBrush(halo), union);
            g.DrawString(idText, idFont, idBrush, new XRect(x, y, w, idH), XStringFormats.TopLeft);
            if (hasCote)
                g.DrawString(coteText, coteFont, coteBrush, new XRect(x, y + idH + 1, w, coteH), XStringFormats.TopLeft);
            occupied.Add(union);
        }

        // Parmi les positions sans chevauchement (IsFree), on préfère celle dont la ligne de rappel
        // reste le plus dégagée des autres traits (évite qu'elle semble prolonger une ligne voisine
        // de même orientation - motif fréquent sur des contrôles alignés). Mais l'absence de
        // chevauchement (IsFree) prime toujours : si aucune position n'atteint le dégagement visé,
        // on garde la MEILLEURE trouvée plutôt que de basculer sur le secours non anti-collision, qui
        // recréerait des chevauchements dans les amas très denses.
        const double GoodEnoughClearancePt = 2.5;
        XRect? bestRect = null; XFont? bestIdFont = null; XFont? bestCoteFont = null;
        double bestIdH = 0, bestCoteH = 0, bestClearance = -1;

        for (double fs = maxFontSize; fs >= minFontSize; fs -= 0.75)
        {
            var idFont = NovatlasTheme.FontBodyBold(fs);
            var coteFont = NovatlasTheme.FontBodyBold(Math.Max(minFontSize - 0.5, fs - 0.5));
            var idSize = g.MeasureString(idText, idFont);
            var coteSize = hasCote ? g.MeasureString(coteText, coteFont) : new XSize(0, 0);
            double w = Math.Max(idSize.Width, coteSize.Width);
            double h = idSize.Height + (hasCote ? coteSize.Height + 1 : 0);

            // Rayon de recherche large (jusqu'à ~19 cm sur le papier) : dans un amas très dense
            // (plusieurs points à quelques cm les uns des autres, chacun avec son propre encart
            // 2 lignes), un rayon court épuise vite les positions libres et bascule sur le
            // placement de secours (qui ne vérifie plus les collisions) - d'où des encarts
            // superposés malgré l'anti-collision. Aller chercher franchement plus loin, quitte à
            // une ligne de rappel plus longue, reste toujours plus lisible qu'un chevauchement.
            for (int step = 0; step <= 26; step++)
            {
                double off = 6 + step * 8;
                foreach (var dir in directions)
                {
                    double x = pt.X + dir.dx * off;
                    double y = pt.Y + dir.dy * off;
                    if (dir.dx < 0) x -= w;
                    if (dir.dy < 0) y -= h;
                    var rr = new XRect(x, y, w, h);
                    if (!bounds.Contains(rr)) continue;
                    if (!IsFree(rr)) continue;

                    var rrCenter = new XPoint(rr.X + rr.Width / 2.0, rr.Y + rr.Height / 2.0);
                    double clearance = MinLeaderClearance(pt, rrCenter, avoidSegments);
                    if (clearance >= GoodEnoughClearancePt)
                    {
                        Place(x, y, w, idSize.Height, coteSize.Height, idFont, coteFont);
                        return;
                    }
                    if (clearance > bestClearance)
                    {
                        bestClearance = clearance;
                        bestRect = rr; bestIdFont = idFont; bestCoteFont = coteFont;
                        bestIdH = idSize.Height; bestCoteH = coteSize.Height;
                    }
                }
            }
        }

        if (bestRect != null)
        {
            Place(bestRect.Value.X, bestRect.Value.Y, bestRect.Value.Width, bestIdH, bestCoteH, bestIdFont!, bestCoteFont!);
            return;
        }

        var lastIdFont = NovatlasTheme.FontBodyBold(minFontSize);
        var lastCoteFont = NovatlasTheme.FontBodyBold(minFontSize - 0.5);
        var lastIdSize = g.MeasureString(idText, lastIdFont);
        var lastCoteSize = hasCote ? g.MeasureString(coteText, lastCoteFont) : new XSize(0, 0);
        double lw = Math.Max(lastIdSize.Width, lastCoteSize.Width);
        double lh = lastIdSize.Height + (hasCote ? lastCoteSize.Height + 1 : 0);
        // Dernier recours : décalage perpendiculaire à la ligne (pas un offset fixe en repère page)
        // pour rester cohérent avec la règle "toujours de biais" même dans ce cas rare.
        double perpX = -lineDir.uy, perpY = lineDir.ux;
        double lx = Math.Max(bounds.X, Math.Min(pt.X + perpX * 10 + 4, bounds.Right - lw));
        double ly = Math.Max(bounds.Y, Math.Min(pt.Y + perpY * 10 - lh - 2, bounds.Bottom - lh));
        Place(lx, ly, lw, lastIdSize.Height, lastCoteSize.Height, lastIdFont, lastCoteFont);
    }

    // Étiquette réduite au seul n° de point, utilisée sur les pages "zone groupée" à la place de
    // l'encart id+cotation (PlaceCallout) : les valeurs Dl/Dt sont renvoyées dans un tableau à part
    // (DrawPointTable) plutôt qu'imprimées à côté de chaque point. Retour utilisateur : dans les
    // amas très denses, caser un encart de 2 lignes + sa ligne de rappel pour CHAQUE point reste
    // géométriquement insoluble (5004/5005 encore visuellement "alignés", rappel de 5008 qui
    // recoupe une ligne voisine) - un simple numéro (2-5 caractères) élimine le problème plutôt que
    // de continuer à l'ajuster. Cherche une position libre tout près du point (rayon croissant
    // court) ; ne trace une ligne de rappel que si le numéro a dû s'écarter du point (au premier
    // essai, sans, l'adjacence visuelle suffit à lever toute ambiguïté).
    private static void PlaceIdBadge(XGraphics g, XPoint pt, string idText, XRect bounds, List<XRect> occupied, List<(XPoint a, XPoint b)> avoidSegments, (double ux, double uy) lineDir)
    {
        if (string.IsNullOrWhiteSpace(idText)) return;
        var font = NovatlasTheme.FontBodyBold(7.5);
        var size = g.MeasureString(idText, font);
        double w = size.Width, h = size.Height;
        var directions = ObliqueDirections(lineDir);

        bool IsFree(XRect rect)
        {
            foreach (var o in occupied) if (o.IntersectsWith(rect)) return false;
            foreach (var seg in avoidSegments) if (RectIntersectsSegment(rect, seg.a, seg.b)) return false;
            return true;
        }

        void Place(double x, double y, bool leader)
        {
            var rect = new XRect(x, y, w, h);
            if (leader)
                g.DrawLine(new XPen(XColor.FromArgb(180, 180, 180), 0.35) { DashStyle = XDashStyle.Dot }, pt, new XPoint(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0));
            var halo = XColor.FromArgb(190, 255, 255, 255);
            g.DrawRectangle(new XSolidBrush(halo), rect);
            g.DrawString(idText, font, XBrushes.Black, rect, XStringFormats.TopLeft);
            occupied.Add(rect);
        }

        // Comme PlaceCallout : garde le meilleur candidat (le moins de chevauchements) rencontré
        // pendant toute la recherche plutôt que de retomber sur un repli non vérifié dès que le
        // rayon de recherche est épuisé - retour d'un cas réel (TS0032/TS0033, deux points à
        // quelques cm l'un de l'autre, encadrés par leurs propres projecteurs) où le repli aveugle
        // plaçait les deux numéros exactement l'un sur l'autre.
        int OverlapCount(XRect rect)
        {
            int n = 0;
            foreach (var o in occupied) if (o.IntersectsWith(rect)) n++;
            foreach (var seg in avoidSegments) if (RectIntersectsSegment(rect, seg.a, seg.b)) n++;
            return n;
        }

        XRect? bestRect = null;
        int bestOverlap = int.MaxValue;

        for (int step = 0; step <= 20; step++)
        {
            double off = 3 + step * 6;
            foreach (var dir in directions)
            {
                double x = pt.X + dir.dx * off;
                double y = pt.Y + dir.dy * off;
                if (dir.dx < 0) x -= w;
                if (dir.dy < 0) y -= h;
                var rr = new XRect(x, y, w, h);
                if (!bounds.Contains(rr)) continue;
                if (IsFree(rr))
                {
                    Place(x, y, step > 0);
                    return;
                }
                int ov = OverlapCount(rr);
                if (ov < bestOverlap) { bestOverlap = ov; bestRect = rr; }
            }
        }

        if (bestRect != null)
        {
            Place(bestRect.Value.X, bestRect.Value.Y, true);
            return;
        }

        double perpX2 = -lineDir.uy, perpY2 = lineDir.ux;
        double lx2 = Math.Max(bounds.X, Math.Min(pt.X + perpX2 * 8 + 3, bounds.Right - w));
        double ly2 = Math.Max(bounds.Y, Math.Min(pt.Y + perpY2 * 8 - h - 1, bounds.Bottom - h));
        Place(lx2, ly2, true);
    }

    // Tri "numérique si possible" des n° de point (5002 < 5010, pas l'ordre alphabétique 5002 <
    // 5010 par hasard mais 5002 < 500 en pur texte) - partagé par le tri de chaque mini-tableau.
    private static int CompareIdNumeric(string a, string b)
    {
        bool an = double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var av);
        bool bn = double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var bv);
        if (an && bn) return av.CompareTo(bv);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // Cherche une position libre pour un bloc de taille fixe (mini-tableau) à proximité d'un point
    // d'ancrage - même principe que PlaceZoneLabel (anneaux de directions, rayon croissant), réutilisé
    // ici pour un rectangle de taille arbitraire plutôt qu'une étiquette mesurée dynamiquement.
    private static void PlaceBlock(XPoint anchor, double w, double h, XRect bounds, List<XRect> occupied, List<(XPoint a, XPoint b)> avoidSegments, Action<XRect> draw)
    {
        bool IsFree(XRect rect)
        {
            foreach (var o in occupied) if (o.IntersectsWith(rect)) return false;
            foreach (var seg in avoidSegments) if (RectIntersectsSegment(rect, seg.a, seg.b)) return false;
            return true;
        }

        // Décalage de départ généreux (retour utilisateur, deux fois : les cadres restaient trop
        // collés aux amas) pour aérer nettement le mini-tableau par rapport à ses points, même
        // quand une position proche était techniquement libre.
        for (int step = 0; step <= 30; step++)
        {
            double off = 60 + step * 14;
            foreach (var dir in LabelDirections)
            {
                double x = anchor.X + dir.dx * off;
                double y = anchor.Y + dir.dy * off;
                if (dir.dx < 0) x -= w;
                if (dir.dy < 0) y -= h;
                var rr = new XRect(x, y, w, h);
                if (!bounds.Contains(rr)) continue;
                if (!IsFree(rr)) continue;
                draw(rr);
                occupied.Add(rr);
                return;
            }
        }

        var fallback = new XRect(
            Math.Max(bounds.X, Math.Min(anchor.X + 50, bounds.Right - w)),
            Math.Max(bounds.Y, Math.Min(anchor.Y - h - 50, bounds.Bottom - h)),
            w, h);
        draw(fallback);
        occupied.Add(fallback);
    }

    // Mini-tableau "Pt / Dl / Dt" posé à côté d'un amas de points, DANS le cadre du plan (retour
    // utilisateur : le tableau doit rester dans la présentation, pas à part) - un par amas visuel
    // plutôt qu'un seul tableau global pour toute la zone (voir RenderZonePage, regroupement par
    // proximité réelle), pour rester juste à côté des points qu'il documente.
    private static void DrawMiniPointTable(XGraphics g, XRect rect, List<(string id, string dL, string dT)> rows)
    {
        var headFont = NovatlasTheme.FontBodyBold(6.5);
        var idFont = NovatlasTheme.FontBodyBold(6.5);
        var valFont = NovatlasTheme.FontBody(6.5);
        var coteBrush = new XSolidBrush(NovatlasTheme.ResolveOrange(_currentRoot));
        double rowH = Units.MmToPt(3.6);
        double headerH = Units.MmToPt(4.2);
        double pad = Units.MmToPt(1.5);

        var halo = XColor.FromArgb(235, 255, 255, 255);
        g.DrawRectangle(new XSolidBrush(halo), rect);
        g.DrawRectangle(new XPen(LineGray, 0.6), rect);

        double idColW = rect.Width * 0.34, dlColW = rect.Width * 0.33, dtColW = rect.Width * 0.33;
        double cx = rect.X + pad, hy = rect.Y + pad;
        g.DrawString("Pt", headFont, XBrushes.Black, new XRect(cx, hy, idColW, headerH), XStringFormats.TopLeft);
        g.DrawString("Dl", headFont, coteBrush, new XRect(cx + idColW, hy, dlColW, headerH), XStringFormats.TopLeft);
        g.DrawString("Dt", headFont, coteBrush, new XRect(cx + idColW + dlColW, hy, dtColW, headerH), XStringFormats.TopLeft);
        g.DrawLine(new XPen(LineGray, 0.5), rect.X + pad, hy + headerH - 1, rect.Right - pad, hy + headerH - 1);

        for (int i = 0; i < rows.Count; i++)
        {
            double ry = hy + headerH + i * rowH;
            var (id, dl, dt) = rows[i];
            g.DrawString(id, idFont, XBrushes.Black, new XRect(cx, ry, idColW, rowH), XStringFormats.TopLeft);
            g.DrawString(dl, valFont, coteBrush, new XRect(cx + idColW, ry, dlColW, rowH), XStringFormats.TopLeft);
            g.DrawString(dt, valFont, coteBrush, new XRect(cx + idColW + dlColW, ry, dtColW, rowH), XStringFormats.TopLeft);
        }
    }

    // dxPage/dyPage : direction de "nord" en repère page (0,-1 = vers le haut, non tourné).
    private static void DrawNorthArrow(XGraphics g, XRect area, double dxPage, double dyPage, double lenPt)
    {
        double mag = Math.Sqrt(dxPage * dxPage + dyPage * dyPage);
        if (mag <= 1e-9) { dxPage = 0; dyPage = -1; mag = 1; }
        double ux = dxPage / mag, uy = dyPage / mag;

        var c = new XPoint(area.Right - lenPt * 0.9, area.Y + lenPt * 0.9);
        var a = new XPoint(c.X - ux * lenPt / 2.0, c.Y - uy * lenPt / 2.0);
        var b = new XPoint(c.X + ux * lenPt / 2.0, c.Y + uy * lenPt / 2.0);
        var pen = new XPen(XColors.Black, 1.1);
        g.DrawLine(pen, a, b);
        double ah = lenPt * 0.22;
        double px = -uy, py = ux;
        g.DrawLine(pen, b, new XPoint(b.X - ux * ah + px * ah * 0.55, b.Y - uy * ah + py * ah * 0.55));
        g.DrawLine(pen, b, new XPoint(b.X - ux * ah - px * ah * 0.55, b.Y - uy * ah - py * ah * 0.55));
        g.DrawString("N", NovatlasTheme.FontBold(8), XBrushes.Black,
            new XRect(b.X - Units.MmToPt(4), b.Y - Units.MmToPt(7), Units.MmToPt(8), Units.MmToPt(5)), XStringFormats.Center);
    }

    // Toutes les données d'une ligne extraites une seule fois (JSON parsé), utilisées ensuite à la
    // fois pour le regroupement en zones et pour le dessin.
    private sealed class LineData
    {
        public string LineId = "Ligne";
        public string StartId = "";
        public string EndId = "";
        public double SE, SN, EE, EN;
        public List<JsonElement> RabPoints = new();
        public double Extent;
        public double MinX, MaxX, MinY, MaxY;
        public double RefE, RefN;
    }

    private static LineData? ExtractLineData(JsonElement lr)
    {
        var startEl = GetObj(lr, "start");
        var endEl = GetObj(lr, "end");
        if (startEl == null || endEl == null) return null;
        double? sE = GetNum(startEl.Value, "E"), sN = GetNum(startEl.Value, "N");
        double? eE = GetNum(endEl.Value, "E"), eN = GetNum(endEl.Value, "N");
        if (sE == null || sN == null || eE == null || eN == null) return null;

        var rabPoints = new List<JsonElement>();
        if (lr.TryGetProperty("rabPoints", out var rpArr) && rpArr.ValueKind == JsonValueKind.Array)
            foreach (var p in rpArr.EnumerateArray())
                if (p.ValueKind == JsonValueKind.Object) rabPoints.Add(p);
        if (rabPoints.Count == 0) return null;

        string lineId = GetStr(lr, "lineId");
        if (string.IsNullOrWhiteSpace(lineId)) lineId = "Ligne";

        var data = new LineData
        {
            LineId = lineId,
            StartId = GetStr(startEl.Value, "id"),
            EndId = GetStr(endEl.Value, "id"),
            SE = sE.Value, SN = sN.Value, EE = eE.Value, EN = eN.Value,
            RabPoints = rabPoints
        };

        var (rawMinX, rawMaxX, rawMinY, rawMaxY, _, _, _, _) = ComputeExtendedBbox(data);
        data.Extent = Math.Max(rawMaxX - rawMinX, rawMaxY - rawMinY);
        data.MinX = rawMinX; data.MaxX = rawMaxX; data.MinY = rawMinY; data.MaxY = rawMaxY;
        data.RefE = (rawMinX + rawMaxX) / 2.0;
        data.RefN = (rawMinY + rawMaxY) / 2.0;
        return data;
    }

    // Direction/chaînage/bbox étendue de la ligne (au-delà de start/end si le chaînage contrôlé
    // sort de [0, longueur] - cas courant pour une base courte comme Pt_23/Pt_24).
    private static (double minX, double maxX, double minY, double maxY, double duxw, double duyw, XPoint extStart, XPoint extEnd) ComputeExtendedBbox(LineData d)
    {
        double lineDxw = d.EE - d.SE, lineDyw = d.EN - d.SN;
        double lineLenW = Math.Sqrt(lineDxw * lineDxw + lineDyw * lineDyw);
        if (lineLenW < 1e-9) lineLenW = 1e-9;
        double duxw = lineDxw / lineLenW, duyw = lineDyw / lineLenW;

        double ChainageOf(JsonElement rp, JsonElement? calc)
        {
            var ec = GetObj(rp, "ec");
            var dL = ec != null ? GetNum(ec.Value, "dL") : null;
            if (dL != null) return dL.Value;
            if (calc == null) return 0;
            var cx = GetNum(calc.Value, "E"); var cy = GetNum(calc.Value, "N");
            if (cx == null || cy == null) return 0;
            return (cx.Value - d.SE) * duxw + (cy.Value - d.SN) * duyw;
        }

        double minChain = 0, maxChain = lineLenW;
        var xs = new List<double> { d.SE, d.EE };
        var ys = new List<double> { d.SN, d.EN };
        foreach (var rp in d.RabPoints)
        {
            var mes = GetObj(rp, "mes");
            var calc = GetObj(rp, "calc");
            if (mes != null) { var mx = GetNum(mes.Value, "E"); var my = GetNum(mes.Value, "N"); if (mx != null && my != null) { xs.Add(mx.Value); ys.Add(my.Value); } }
            if (calc != null) { var cx = GetNum(calc.Value, "E"); var cy = GetNum(calc.Value, "N"); if (cx != null && cy != null) { xs.Add(cx.Value); ys.Add(cy.Value); } }
            double chain = ChainageOf(rp, calc);
            minChain = Math.Min(minChain, chain);
            maxChain = Math.Max(maxChain, chain);
        }
        var extStart = new XPoint(d.SE + duxw * minChain, d.SN + duyw * minChain);
        var extEnd = new XPoint(d.SE + duxw * maxChain, d.SN + duyw * maxChain);
        xs.Add(extStart.X); xs.Add(extEnd.X);
        ys.Add(extStart.Y); ys.Add(extEnd.Y);

        return (xs.Min(), xs.Max(), ys.Min(), ys.Max(), duxw, duyw, extStart, extEnd);
    }

    // Une page du document : soit une "zone" groupée (plusieurs lignes compactes proches), soit une
    // ligne longue seule en pleine page. Les deux portent un numéro d'ordre (Index) et une bbox,
    // utilisés par l'encart "Situation" pour repositionner la page dans l'ensemble du document.
    private sealed class Zone
    {
        public List<LineData> Lines = new();
        public bool IsFullPage;
        public int Index;
        public int PageNumber;
        public double CenterE, CenterN;
        public double MinX, MaxX, MinY, MaxY;
    }

    private static double Dist(double x1, double y1, double x2, double y2)
    {
        double dx = x1 - x2, dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Rayon d'affichage d'une zone sur les vues d'ensemble (vue générale + encart Situation) :
    // l'étendue RÉELLE des points de la zone (avec une petite marge), pas le rayon de RECHERCHE fixe
    // (ClusterRadiusMeters) utilisé pour le regroupement - sinon, sur un site compact où les zones
    // ne remplissent qu'une fraction de ce rayon, les cercles se chevauchent tous et deviennent
    // illisibles.
    private static double ZoneDisplayRadiusMeters(Zone z) =>
        Math.Max(1.5, Math.Max(z.MaxX - z.MinX, z.MaxY - z.MinY) / 2.0 * 1.15);

    private static void RecomputeZoneBounds(Zone z)
    {
        z.MinX = z.Lines.Min(l => l.MinX); z.MaxX = z.Lines.Max(l => l.MaxX);
        z.MinY = z.Lines.Min(l => l.MinY); z.MaxY = z.Lines.Max(l => l.MaxY);
        z.CenterE = z.Lines.Average(l => l.RefE);
        z.CenterN = z.Lines.Average(l => l.RefN);
    }

    // Une zone regroupée par cercle peut rester trop dense pour tenir sur une seule page A3 lisible
    // (retour utilisateur : "je diviserais les plans A3 encore en 2"). On la scinde alors en 2 le
    // long de son axe le plus étendu, en coupant au plus grand "vide" trouvé près du milieu plutôt
    // qu'à un index fixe - pour ne pas couper au travers d'un petit groupe de points très serrés
    // (plusieurs visées d'un même repère), qui doit rester entier sur une seule des deux pages.
    // Récursif : une moitié encore trop dense est scindée à son tour, jusqu'à ce que toutes les
    // zones respectent MaxLinesPerZone.
    private static List<Zone> SplitOversizedZones(List<Zone> zones)
    {
        var result = new List<Zone>();
        foreach (var z in zones)
        {
            RecomputeZoneBounds(z);
            if (z.IsFullPage || z.Lines.Count <= MaxLinesPerZone) { result.Add(z); continue; }

            var (a, b) = SplitZoneInTwo(z);
            result.AddRange(SplitOversizedZones(new List<Zone> { a, b }));
        }
        return result;
    }

    private static (Zone, Zone) SplitZoneInTwo(Zone z)
    {
        bool alongE = (z.MaxX - z.MinX) >= (z.MaxY - z.MinY);
        double Pos(LineData l) => alongE ? l.RefE : l.RefN;
        var ordered = z.Lines.OrderBy(Pos).ToList();

        int n = ordered.Count;
        int mid = n / 2;
        int span = Math.Max(1, n / 4);
        int cutIdx = mid;
        double bestGap = -1;
        for (int i = Math.Max(1, mid - span); i <= Math.Min(n - 1, mid + span); i++)
        {
            double gap = Pos(ordered[i]) - Pos(ordered[i - 1]);
            if (gap > bestGap) { bestGap = gap; cutIdx = i; }
        }
        if (cutIdx <= 0 || cutIdx >= n) cutIdx = mid;

        return (
            new Zone { Lines = ordered.Take(cutIdx).ToList(), IsFullPage = false },
            new Zone { Lines = ordered.Skip(cutIdx).ToList(), IsFullPage = false }
        );
    }

    // Regroupement glouton par cercles de ClusterRadiusMeters : on part de la ligne compacte non
    // encore assignée la plus au "nord-ouest" (balayage haut->bas puis gauche->droite), on absorbe
    // dans son cercle toutes les lignes compactes restantes dont le centre est à moins de
    // ClusterRadiusMeters, puis on recommence avec la ligne suivante non assignée - le cercle se
    // décale donc naturellement d'une zone à l'autre, comme demandé.
    private static List<Zone> BuildZones(List<LineData> allLines)
    {
        var zones = new List<Zone>();

        foreach (var l in allLines.Where(l => l.Extent >= CompactExtentThresholdMeters))
            zones.Add(new Zone { Lines = { l }, IsFullPage = true });

        var unassigned = allLines.Where(l => l.Extent < CompactExtentThresholdMeters)
            .OrderByDescending(l => l.RefN).ThenBy(l => l.RefE).ToList();

        while (unassigned.Count > 0)
        {
            var seed = unassigned[0];
            var members = unassigned.Where(l => Dist(l.RefE, l.RefN, seed.RefE, seed.RefN) <= ClusterRadiusMeters).ToList();
            foreach (var m in members) unassigned.Remove(m);
            zones.Add(new Zone { Lines = members, IsFullPage = false });
        }

        foreach (var z in zones) RecomputeZoneBounds(z);
        zones = SplitOversizedZones(zones);
        foreach (var z in zones) RecomputeZoneBounds(z);

        var ordered = zones.OrderByDescending(z => z.CenterN).ThenBy(z => z.CenterE).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Index = i + 1;
        return ordered;
    }

    public static void AppendFromPayload(PdfDocument doc, string payloadJson, string buildFooter)
    {
        JsonElement root;
        try
        {
            using var jd = JsonDocument.Parse(payloadJson);
            root = jd.RootElement.Clone();
        }
        catch { return; }

        if (!GetBool(root, "includeGraphicalPlan")) return;
        if (!root.TryGetProperty("ligneRef", out var lrArr) || lrArr.ValueKind != JsonValueKind.Array) return;

        _currentRoot = root;

        var allLines = new List<LineData>();
        foreach (var lr in lrArr.EnumerateArray())
        {
            if (lr.ValueKind != JsonValueKind.Object) continue;
            var d = ExtractLineData(lr);
            if (d != null) allLines.Add(d);
        }
        if (allLines.Count == 0) return;

        var zones = BuildZones(allLines);

        // Numéro de page réel de chaque zone, calculé à l'avance (chaque zone occupe toujours
        // exactement 1 page) pour pouvoir l'annoncer sur la page de transition "vue générale" créée
        // juste avant, sans avoir à rendre les pages de zone d'abord.
        int nextPage = doc.PageCount + 1 + 1; // +1 pour la page de transition elle-même
        foreach (var zone in zones) zone.PageNumber = nextPage++;

        RenderOverviewPage(doc, zones);
        foreach (var zone in zones)
        {
            if (zone.IsFullPage) RenderFullPage(doc, zone, zones);
            else RenderZonePage(doc, zone, zones);
        }
    }

    // ===== Page de transition "vue générale" (entre le tableau et le détail par zone) =====
    // Un seul plan A3 paysage regroupant TOUS les points de contrôle du document, avec le cercle de
    // chaque zone repéré et étiqueté par son numéro de page - pour se repérer avant de dérouler le
    // détail zone par zone. Échelle adaptée automatiquement pour que l'ensemble du site tienne sur
    // la page (ChooseScaleDenominator, même principe que les pages de zone).
    private static void RenderOverviewPage(PdfDocument doc, List<Zone> zones)
    {
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A3;
        page.Orientation = PdfSharp.PageOrientation.Landscape;
        using var g = XGraphics.FromPdfPage(page);
        double pageW = page.Width.Point, pageH = page.Height.Point;
        double contentW = pageW - MarginL - MarginR;
        double y = MarginT;

        double barH = Units.MmToPt(10);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveBlue(_currentRoot)), new XRect(MarginL, y, contentW, barH));
        g.DrawString("Plan graphique — Vue générale (situation des zones)", NovatlasTheme.FontBold(13), XBrushes.White,
            new XRect(MarginL, y, contentW, barH), XStringFormats.Center);
        y += barH + Units.MmToPt(3);

        int totalLines = zones.Sum(z => z.Lines.Count);
        g.DrawString($"{totalLines} ligne(s) de contrôle réparties en {zones.Count} zone(s) - voir le détail de chaque zone à la page indiquée",
            NovatlasTheme.FontBody(9), XBrushes.Gray, new XRect(MarginL, y, contentW, 14), XStringFormats.TopLeft);
        y += 18;

        double footerSafe = Units.MmToPt(22);
        var frame = new XRect(MarginL, y, contentW, pageH - y - footerSafe);
        g.DrawRectangle(new XPen(LineGray, 0.8), frame);

        double minX = zones.Min(z => z.MinX), maxX = zones.Max(z => z.MaxX);
        double minY = zones.Min(z => z.MinY), maxY = zones.Max(z => z.MaxY);
        double du0 = Math.Max(1.0, maxX - minX), dv0 = Math.Max(1.0, maxY - minY);
        // Marge proportionnée aux cercles de zone réellement dessinés (pas au rayon de recherche fixe)
        // pour que le plus grand d'entre eux ne déborde jamais du cadre.
        double maxRing = zones.Count > 0 ? zones.Max(ZoneDisplayRadiusMeters) : 1.0;
        double padX = du0 * 0.08 + maxRing * 0.3, padY = dv0 * 0.08 + maxRing * 0.3;
        minX -= padX; maxX += padX; minY -= padY; maxY += padY;
        double du = maxX - minX, dv = maxY - minY;

        double legendH = Units.MmToPt(8);
        var inner = new XRect(frame.X, frame.Y, frame.Width, frame.Height - legendH);
        int denom = ChooseScaleDenominator(du, dv, inner.Width, inner.Height);
        double scale = Units.MmToPt(1000.0 / denom);
        double usedW = du * scale, usedH = dv * scale;
        double offX = Math.Max(0, (inner.Width - usedW) / 2.0);
        double offY = Math.Max(0, (inner.Height - usedH) / 2.0);
        XPoint Map(double x, double yy) => new XPoint(inner.X + offX + (x - minX) * scale, inner.Y + offY + usedH - (yy - minY) * scale);

        // Tous les points de contrôle (marqueurs seuls, sans étiquette individuelle - simple repérage
        // de la densité et de l'implantation générale, le détail est dans les pages de zone). En noir
        // pour laisser le bleu identifier uniquement les zones (cercles + étiquettes) - retour
        // utilisateur.
        var dotBrush = new XSolidBrush(XColors.Black);
        foreach (var z in zones)
            foreach (var d in z.Lines)
                foreach (var rp in d.RabPoints)
                {
                    var mes = GetObj(rp, "mes");
                    double? mx = mes != null ? GetNum(mes.Value, "E") : null;
                    double? my = mes != null ? GetNum(mes.Value, "N") : null;
                    if (mx == null || my == null) continue;
                    var p = Map(mx.Value, my.Value);
                    g.DrawEllipse(dotBrush, p.X - 1.2, p.Y - 1.2, 2.4, 2.4);
                }

        // Cercle + étiquette "Zone N — p.X" pour chaque zone. Les cercles se dessinent d'abord (tous),
        // puis les étiquettes en anti-collision (comme les points contrôlés dans les pages de zone) :
        // sur un site compact où les zones sont proches, une position fixe "au-dessus du centre" les
        // faisait se chevaucher, donnant l'impression que certaines manquaient.
        var ringPen = new XPen(ZoneRingColor, 1.0) { DashStyle = XDashStyle.Dash };
        var zoneLabelFont = NovatlasTheme.FontBodyBold(7.5);
        var zoneLabelBrush = new XSolidBrush(MeasuredColor);
        var zoneRings = new List<(XPoint center, double ringPx)>();
        foreach (var z in zones)
        {
            var c = Map(z.CenterE, z.CenterN);
            double ringPx = ZoneDisplayRadiusMeters(z) * scale;
            g.DrawEllipse(ringPen, c.X - ringPx, c.Y - ringPx, ringPx * 2, ringPx * 2);
            zoneRings.Add((c, ringPx));
        }

        var occupiedZoneLabels = new List<XRect>();
        for (int i = 0; i < zones.Count; i++)
        {
            string zlabel = $"Zone {zones[i].Index} — p.{zones[i].PageNumber}";
            PlaceZoneLabel(g, zoneRings[i].center, zoneRings[i].ringPx, zlabel, zoneLabelFont, zoneLabelBrush, occupiedZoneLabels, inner);
        }

        DrawNorthArrow(g, frame, 0, -1, Units.MmToPt(14));

        double targetWorld = Math.Max(du, dv) / 5.0;
        double barWorld = NiceScaleLength(targetWorld);
        double barPx = barWorld * scale;
        double barY = frame.Bottom - legendH + Units.MmToPt(3);
        double barX = frame.X + Units.MmToPt(6);
        var barPen = new XPen(XColors.Black, 1.2);
        g.DrawLine(barPen, barX, barY, barX + barPx, barY);
        g.DrawLine(barPen, barX, barY - 3, barX, barY + 3);
        g.DrawLine(barPen, barX + barPx, barY - 3, barX + barPx, barY + 3);
        g.DrawString($"{FormatWorldLength(barWorld)}   —   Échelle 1:{denom}", NovatlasTheme.FontBody(7), XBrushes.Black,
            new XRect(barX, barY + 3, 200, 10), XStringFormats.TopLeft);
    }

    // ===== Régime "pleine page" (ligne longue) =====

    private static void RenderFullPage(PdfDocument doc, Zone zone, List<Zone> allZones)
    {
        var d = zone.Lines[0];
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var g = XGraphics.FromPdfPage(page);
        double pageW = page.Width.Point, pageH = page.Height.Point;
        double contentW = pageW - MarginL - MarginR;
        double y = MarginT;

        double barH = Units.MmToPt(10);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveBlue(_currentRoot)), new XRect(MarginL, y, contentW, barH));
        g.DrawString($"Plan graphique — Ligne de référence : {d.LineId}", NovatlasTheme.FontBold(12), XBrushes.White,
            new XRect(MarginL, y, contentW, barH), XStringFormats.Center);
        y += barH + Units.MmToPt(3);

        g.DrawString($"Zone {zone.Index} / {allZones.Count} — Page {zone.PageNumber} — {d.RabPoints.Count} point(s) contrôlé(s) ({d.StartId} → {d.EndId})",
            NovatlasTheme.FontBody(9), XBrushes.Gray, new XRect(MarginL, y, contentW, 14), XStringFormats.TopLeft);
        y += 18;

        double footerSafe = Units.MmToPt(22);
        var frame = new XRect(MarginL, y, contentW, pageH - y - footerSafe);
        g.DrawRectangle(new XPen(LineGray, 0.8), frame);

        var (rawMinX, rawMaxX, rawMinY, rawMaxY, _, _, extStart, extEnd) = ComputeExtendedBbox(d);
        double rawDu = Math.Max(0.05, rawMaxX - rawMinX), rawDv = Math.Max(0.05, rawMaxY - rawMinY);

        // Rotation auto paysage/portrait (même principe que StationPlanRenderer/
        // RecolementPlanViewRenderer) : une ligne beaucoup plus large que haute occupe mieux une
        // zone plus haute que large, et inversement. Pertinent ici car il n'y a qu'une seule ligne
        // sur la page - contrairement au régime "zone" (plusieurs lignes, orientations mélangées),
        // où le nord reste toujours en haut.
        bool rotate = (rawDu > rawDv) != (frame.Width < frame.Height);
        (double u, double v) Project(double x, double y) => rotate ? (y, -x) : (x, y);

        var xs = new List<double> { d.SE, d.EE, extStart.X, extEnd.X };
        var ys = new List<double> { d.SN, d.EN, extStart.Y, extEnd.Y };
        foreach (var rp in d.RabPoints)
        {
            var mes = GetObj(rp, "mes");
            var calc = GetObj(rp, "calc");
            if (mes != null) { var mx = GetNum(mes.Value, "E"); var my = GetNum(mes.Value, "N"); if (mx != null && my != null) { xs.Add(mx.Value); ys.Add(my.Value); } }
            if (calc != null) { var cx = GetNum(calc.Value, "E"); var cy = GetNum(calc.Value, "N"); if (cx != null && cy != null) { xs.Add(cx.Value); ys.Add(cy.Value); } }
        }
        var xs2 = new List<double>(); var ys2 = new List<double>();
        for (int i = 0; i < xs.Count; i++) { var q = Project(xs[i], ys[i]); xs2.Add(q.u); ys2.Add(q.v); }
        double minX = xs2.Min(), maxX = xs2.Max(), minY = ys2.Min(), maxY = ys2.Max();
        double du0 = Math.Max(0.05, maxX - minX), dv0 = Math.Max(0.05, maxY - minY);
        double padX = du0 * 0.15, padY = dv0 * 0.15;
        minX -= padX; maxX += padX; minY -= padY; maxY += padY;
        double du = Math.Max(0.05, maxX - minX), dv = Math.Max(0.05, maxY - minY);

        double legendH = Units.MmToPt(10);
        var inner = new XRect(frame.X, frame.Y, frame.Width, frame.Height - legendH);
        double scale = Math.Min(inner.Width / du, inner.Height / dv);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        double usedW = du * scale, usedH = dv * scale;
        double offX = Math.Max(0, (inner.Width - usedW) / 2.0);
        double offY = Math.Max(0, (inner.Height - usedH) / 2.0);
        XPoint Map(double x, double y)
        {
            var q = Project(x, y);
            return new XPoint(inner.X + offX + (q.u - minX) * scale, inner.Y + offY + usedH - (q.v - minY) * scale);
        }

        var fullPageSegments = new List<(XPoint, XPoint)>();
        var fullPageOccupied = new List<XRect>();
        DrawLineStrokes(g, Map, d, fullPageSegments);
        DrawLineMarkers(g, Map, d, fullPageOccupied, fullPageSegments, null);
        PlaceLineBadges(g, Map, d, inner, fullPageOccupied, fullPageSegments, idOnly: false);
        var northProj = Project(0, 1);
        DrawScaleBarAndNorthAndLegend(g, frame, legendH, northProj.u, -northProj.v, du, dv, scale);

        // Encart "Situation" intégré dans le cadre (coin bas-droit, seul coin libre - la flèche nord
        // occupe le coin haut-droit, la barre d'échelle le coin bas-gauche) plutôt qu'à côté, pour un
        // rendu plus soigné (retour utilisateur).
        double locW = Units.MmToPt(42), locH = Units.MmToPt(32);
        var locatorBox = new XRect(frame.Right - locW - Units.MmToPt(4), frame.Bottom - locH - Units.MmToPt(4), locW, locH);
        DrawLocatorInset(g, locatorBox, allZones, zone.Index);
    }

    // ===== Régime "zone" (contrôles ponctuels regroupés par proximité réelle) =====

    private static void RenderZonePage(PdfDocument doc, Zone zone, List<Zone> allZones)
    {
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A3;
        page.Orientation = PdfSharp.PageOrientation.Landscape;
        using var g = XGraphics.FromPdfPage(page);
        double pageW = page.Width.Point, pageH = page.Height.Point;
        double contentW = pageW - MarginL - MarginR;
        double y = MarginT;

        double barH = Units.MmToPt(10);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.ResolveBlue(_currentRoot)), new XRect(MarginL, y, contentW, barH));
        g.DrawString("Plan graphique — Lignes de référence (zone groupée)", NovatlasTheme.FontBold(13), XBrushes.White,
            new XRect(MarginL, y, contentW, barH), XStringFormats.Center);
        y += barH + Units.MmToPt(3);

        double subtitleH = Units.MmToPt(9);
        double footerSafe = Units.MmToPt(22);
        var frame = new XRect(MarginL, y + subtitleH, contentW, pageH - y - subtitleH - footerSafe);
        g.DrawRectangle(new XPen(LineGray, 0.8), frame);

        // Bbox réelle du contenu de la zone (tous les points de toutes les lignes membres, chaînage
        // étendu compris), avec marge - le cercle de regroupement (10 m) n'est qu'un critère de
        // recherche, pas un cadrage imposé : on affiche au plus près du contenu réel pour ne pas
        // perdre d'échelle sur une zone plus petite que le rayon de recherche.
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var d in zone.Lines)
        {
            var (mnX, mxX, mnY, mxY, _, _, _, _) = ComputeExtendedBbox(d);
            xs.Add(mnX); xs.Add(mxX); ys.Add(mnY); ys.Add(mxY);
        }
        double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
        double du0 = Math.Max(0.3, maxX - minX), dv0 = Math.Max(0.3, maxY - minY);
        double padX = du0 * 0.2 + 0.3, padY = dv0 * 0.2 + 0.3;
        minX -= padX; maxX += padX; minY -= padY; maxY += padY;
        double du = maxX - minX, dv = maxY - minY;

        double legendH = Units.MmToPt(10);
        var inner = new XRect(frame.X, frame.Y, frame.Width, frame.Height - legendH);
        int denom = ChooseScaleDenominator(du, dv, inner.Width, inner.Height);
        double scale = Units.MmToPt(1000.0 / denom);

        double usedW = du * scale, usedH = dv * scale;
        double offX = Math.Max(0, (inner.Width - usedW) / 2.0);
        double offY = Math.Max(0, (inner.Height - usedH) / 2.0);
        XPoint Map(double x, double y) => new XPoint(inner.X + offX + (x - minX) * scale, inner.Y + offY + usedH - (y - minY) * scale);

        g.DrawString($"Zone {zone.Index} / {allZones.Count} — Page {zone.PageNumber} — {zone.Lines.Count} ligne(s) — Échelle 1:{denom}",
            NovatlasTheme.FontBody(9), XBrushes.Gray, new XRect(MarginL, y, contentW, subtitleH), XStringFormats.TopLeft);

        // Dessin en 2 passes : tout le graphisme (lignes/extensions/ticks) d'abord, puis tous les
        // points/étiquettes ensuite - pour qu'un point implanté ne se retrouve jamais recouvert par
        // le trait d'une ligne voisine dessinée après lui (cas fréquent en zone dense).
        var occupiedLabels = new List<XRect>();
        var avoidSegments = new List<(XPoint, XPoint)>();
        var tableRows = new List<(string id, string dL, string dT, double we, double wn)>();
        foreach (var d in zone.Lines)
            DrawLineStrokes(g, Map, d, avoidSegments);
        foreach (var d in zone.Lines)
            DrawLineMarkers(g, Map, d, occupiedLabels, avoidSegments, tableRows);
        foreach (var d in zone.Lines)
            PlaceLineBadges(g, Map, d, inner, occupiedLabels, avoidSegments, idOnly: true);

        // Réserve la zone de la flèche nord (coin haut-droit du cadre) et de l'encart "Situation"
        // (coin bas-droit), dessinés plus loin, pour qu'un mini-tableau ne s'y pose pas dessus.
        double naSize = Units.MmToPt(22);
        occupiedLabels.Add(new XRect(frame.Right - naSize, frame.Y, naSize, naSize));
        double locW = Units.MmToPt(52), locH = Units.MmToPt(40);
        var locatorBox = new XRect(frame.Right - locW - Units.MmToPt(4), frame.Bottom - locH - Units.MmToPt(4), locW, locH);
        occupiedLabels.Add(locatorBox);

        // Un mini-tableau "Pt / Dl / Dt" par amas visuel de points, posé DANS le cadre juste à côté
        // de l'amas concerné - retour utilisateur : le tableau doit rester dans la présentation
        // (pas à part) et ne lister que les points auxquels il se rapporte, plutôt qu'un seul grand
        // tableau pour toute la zone. Regroupement glouton par proximité réelle (rayon très inférieur
        // à celui du regroupement en zones) : vérifié sur des données réelles que les points d'un même
        // amas visuel restent à moins de 0.3 m les uns des autres, et l'amas voisin le plus proche à
        // plus de 1.3 m - un rayon de 0.8 m les sépare donc franchement sans jamais couper un amas.
        const double PointGroupRadiusMeters = 0.8;
        var unassignedPts = tableRows.OrderByDescending(r => r.wn).ThenBy(r => r.we).ToList();
        var pointGroups = new List<List<(string id, string dL, string dT, double we, double wn)>>();
        while (unassignedPts.Count > 0)
        {
            var seed = unassignedPts[0];
            var members = unassignedPts.Where(r => Dist(r.we, r.wn, seed.we, seed.wn) <= PointGroupRadiusMeters).ToList();
            foreach (var m in members) unassignedPts.Remove(m);
            pointGroups.Add(members);
        }

        double miniColW = Units.MmToPt(30), miniRowH = Units.MmToPt(3.6), miniHeaderH = Units.MmToPt(4.2), miniPad = Units.MmToPt(1.5);
        foreach (var group in pointGroups)
        {
            group.Sort((a, b) => CompareIdNumeric(a.id, b.id));
            var anchor = Map(group.Average(r => r.we), group.Average(r => r.wn));
            double h = miniHeaderH + group.Count * miniRowH + miniPad * 2;
            var rows = group.Select(r => (r.id, r.dL, r.dT)).ToList();
            PlaceBlock(anchor, miniColW, h, inner, occupiedLabels, avoidSegments, rect => DrawMiniPointTable(g, rect, rows));
        }

        DrawScaleBarAndNorthAndLegend(g, frame, legendH, 0, -1, du, dv, scale);
        DrawLocatorInset(g, locatorBox, allZones, zone.Index);
    }

    // ===== Cœur du dessin d'une ligne (ligne + points + cotation), partagé par les 2 régimes =====
    // Volontairement scindé en 2 passes (traits, puis points/étiquettes) - voir appelants : dessiner
    // TOUTES les lignes d'une zone avant TOUS les points garantit qu'un marqueur de point implanté
    // n'est jamais recouvert par le trait d'une ligne voisine dessinée juste après lui.

    private static void DrawLineStrokes(XGraphics g, Func<double, double, XPoint> map, LineData d, List<(XPoint a, XPoint b)> avoidSegments)
    {
        var (_, _, _, _, _, _, extStart, extEnd) = ComputeExtendedBbox(d);
        var pStart = map(d.SE, d.SN);
        var pEnd = map(d.EE, d.EN);
        var pExtStart = map(extStart.X, extStart.Y);
        var pExtEnd = map(extEnd.X, extEnd.Y);
        // Trait simple, sans amorces perpendiculaires aux extrémités (juste la ligne, un peu moins
        // épaisse - retour utilisateur) ; le segment étendu (hors chaînage réel) reste plus fin pour
        // rester visuellement distinct du segment réel start/end.
        var extLinePen = new XPen(XColors.Black, 0.5);
        var linePen = new XPen(XColors.Black, 1.1);
        g.DrawLine(extLinePen, pExtStart, pExtEnd);
        g.DrawLine(linePen, pStart, pEnd);
        // La ligne (segment étendu) est retenue comme "graphisme à éviter" pour que les étiquettes
        // ne s'y superposent pas.
        avoidSegments.Add((pExtStart, pExtEnd));
        // Les points théoriques d'extrémité de ligne (start/end) ne sont pas étiquetés : seuls les
        // points contrôlés (mesurés) sont identifiés, sur demande.

        // Flèche rouge indiquant le sens de la ligne (start -> end), posée sur le dernier point
        // (end) - retour utilisateur : remplace les anciens repères de sens (amorces/libellés
        // start/end retirés plus haut) par un indicateur visuel unique et discret.
        DrawDirectionArrow(g, pStart, pEnd);
    }

    private static readonly XColor DirectionArrowColor = XColor.FromArgb(210, 30, 30);

    private static void DrawDirectionArrow(XGraphics g, XPoint from, XPoint to)
    {
        double dxw = to.X - from.X, dyw = to.Y - from.Y;
        double lenPx = Math.Sqrt(dxw * dxw + dyw * dyw);
        if (lenPx < 1e-6) return;
        double ux = dxw / lenPx, uy = dyw / lenPx, px = -uy, py = ux;

        // La pointe recule d'un petit retrait par rapport à l'extrémité réelle de la ligne : sur les
        // lignes courtes (percements/joints), le point contrôlé coïncide souvent presque avec cette
        // extrémité, et la flèche se retrouvait plantée derrière le marqueur bleu (chevauchement
        // signalé). Le retrait et la taille de la pointe sont plafonnés pour rester nets même sur de
        // très courtes lignes.
        double pullback = Math.Min(Units.MmToPt(1.8), lenPx * 0.3);
        double ah = Math.Min(Units.MmToPt(2.2), lenPx * 0.35);
        double aw = ah * 0.5;
        var tip = new XPoint(to.X - ux * pullback, to.Y - uy * pullback);
        var baseC = new XPoint(tip.X - ux * ah, tip.Y - uy * ah);
        var baseL = new XPoint(baseC.X + px * aw, baseC.Y + py * aw);
        var baseR = new XPoint(baseC.X - px * aw, baseC.Y - py * aw);
        var arrowBrush = new XSolidBrush(DirectionArrowColor);
        g.DrawPolygon(arrowBrush, new[] { tip, baseL, baseR }, XFillMode.Winding);
    }

    // Dessine les marqueurs (mesuré + théorique + projecteur) d'une ligne, SANS poser les
    // étiquettes/badges - scindé de la pose des n°/encarts (PlaceLineBadges) pour que TOUS les
    // marqueurs de TOUTES les lignes d'une zone soient dessinés (et enregistrés comme obstacles)
    // avant qu'aucune étiquette ne cherche sa place. Cas réel corrigé : TS0032/TS0033, deux points
    // à ~0.12 m l'un de l'autre mais appartenant à 2 lignes différentes - tant que les marqueurs
    // étaient dessinés ligne par ligne EN MÊME TEMPS que la pose de leur propre n°, le n° de l'un ne
    // "voyait" pas encore le marqueur de l'autre (pas encore dessiné) et venait se poser dessus.
    private static void DrawLineMarkers(XGraphics g, Func<double, double, XPoint> map, LineData d, List<XRect> occupiedLabels, List<(XPoint a, XPoint b)> avoidSegments, List<(string id, string dL, string dT, double we, double wn)>? tableRows)
    {
        var dashPen = new XPen(ProjectorColor, 0.8) { DashStyle = XDashStyle.Dash };
        var theoPen = new XPen(TheoColor, 1.0);
        var measBrush = new XSolidBrush(MeasuredColor);
        var haloBrush = new XSolidBrush(XColors.White);
        const double rMes = 2.4, rTheo = 2.0;

        foreach (var rp in d.RabPoints)
        {
            var mes = GetObj(rp, "mes");
            var calc = GetObj(rp, "calc");
            var ec = GetObj(rp, "ec");
            double? mx = mes != null ? GetNum(mes.Value, "E") : null;
            double? my = mes != null ? GetNum(mes.Value, "N") : null;
            if (mx == null || my == null) continue;
            var pMes = map(mx.Value, my.Value);

            double? cx = calc != null ? GetNum(calc.Value, "E") : null;
            double? cy = calc != null ? GetNum(calc.Value, "N") : null;
            XPoint? pTheo = (cx != null && cy != null) ? map(cx.Value, cy.Value) : (XPoint?)null;

            if (pTheo != null)
            {
                g.DrawLine(dashPen, pMes, pTheo.Value);
                // Halo blanc pour que le marqueur théorique reste lisible même sur un trait noir.
                g.DrawEllipse(haloBrush, pTheo.Value.X - rTheo - 1, pTheo.Value.Y - rTheo - 1, (rTheo + 1) * 2, (rTheo + 1) * 2);
                g.DrawEllipse(theoPen, pTheo.Value.X - rTheo, pTheo.Value.Y - rTheo, rTheo * 2, rTheo * 2);
                avoidSegments.Add((pMes, pTheo.Value));
                // Marqueur théorique = obstacle pour les étiquettes (voir commentaire de méthode).
                occupiedLabels.Add(new XRect(pTheo.Value.X - rTheo - 1, pTheo.Value.Y - rTheo - 1, (rTheo + 1) * 2, (rTheo + 1) * 2));
            }
            // Idem pour le point mesuré (implanté) : halo blanc sous le disque bleu pour qu'il ne se
            // fonde pas dans les traits de lignes voisines qui passent par le même endroit.
            g.DrawEllipse(haloBrush, pMes.X - rMes - 1, pMes.Y - rMes - 1, (rMes + 1) * 2, (rMes + 1) * 2);
            g.DrawEllipse(measBrush, pMes.X - rMes, pMes.Y - rMes, rMes * 2, rMes * 2);
            occupiedLabels.Add(new XRect(pMes.X - rMes - 1, pMes.Y - rMes - 1, (rMes + 1) * 2, (rMes + 1) * 2));

            if (tableRows != null)
            {
                string ptId = GetStr(rp, "id");
                if (string.IsNullOrWhiteSpace(ptId)) ptId = GetStr(rp, "businessPointId");
                double? dL = ec != null ? GetNum(ec.Value, "dL") : null;
                double? dT = ec != null ? GetNum(ec.Value, "dT") : null;
                tableRows.Add((
                    ptId,
                    dL != null ? dL.Value.ToString("F3", CultureInfo.InvariantCulture) : "-",
                    dT != null ? dT.Value.ToString("F3", CultureInfo.InvariantCulture) : "-",
                    mx.Value, my.Value));
            }
        }
    }

    // Pose les étiquettes/badges d'une ligne (appelé APRÈS DrawLineMarkers pour toutes les lignes de
    // la zone - voir commentaire de méthode ci-dessus) : soit un simple n° (idOnly, zones groupées),
    // soit l'ancien encart id+cotation (PlaceCallout, régime "pleine page").
    private static void PlaceLineBadges(XGraphics g, Func<double, double, XPoint> map, LineData d, XRect bounds, List<XRect> occupiedLabels, List<(XPoint a, XPoint b)> avoidSegments, bool idOnly)
    {
        var coteBrush = new XSolidBrush(NovatlasTheme.ResolveOrange(_currentRoot));

        // Direction de la ligne (start -> end) en repère page : sert à orienter la recherche de
        // position des encarts (PlaceCallout/PlaceIdBadge) toujours de biais par rapport à la ligne,
        // jamais dans son prolongement (retour utilisateur : la ligne de rappel "traversait les
        // cotes" quand l'encart se retrouvait aligné avec la ligne de référence).
        var lp0 = map(d.SE, d.SN);
        var lp1 = map(d.EE, d.EN);
        double lineDx = lp1.X - lp0.X, lineDy = lp1.Y - lp0.Y;
        double lineLen = Math.Sqrt(lineDx * lineDx + lineDy * lineDy);
        (double ux, double uy) lineDir = lineLen > 1e-6 ? (lineDx / lineLen, lineDy / lineLen) : (1, 0);

        foreach (var rp in d.RabPoints)
        {
            var mes = GetObj(rp, "mes");
            var ec = GetObj(rp, "ec");
            double? mx = mes != null ? GetNum(mes.Value, "E") : null;
            double? my = mes != null ? GetNum(mes.Value, "N") : null;
            if (mx == null || my == null) continue;
            var pMes = map(mx.Value, my.Value);

            string ptId = GetStr(rp, "id");
            if (string.IsNullOrWhiteSpace(ptId)) ptId = GetStr(rp, "businessPointId");

            if (idOnly)
            {
                PlaceIdBadge(g, pMes, ptId, bounds, occupiedLabels, avoidSegments, lineDir);
            }
            else
            {
                double? dL = ec != null ? GetNum(ec.Value, "dL") : null;
                double? dT = ec != null ? GetNum(ec.Value, "dT") : null;
                string cote = (dL != null && dT != null) ? $"dL {dL.Value:F3}   dT {dT.Value:F3}" : "";
                PlaceCallout(g, pMes, ptId, cote, XBrushes.Black, coteBrush, bounds, occupiedLabels, avoidSegments, lineDir);
            }
        }
    }

    // Barre d'échelle + flèche nord + légende, partagées par les 2 régimes.
    private static void DrawScaleBarAndNorthAndLegend(XGraphics g, XRect area, double legendH, double dxNorthPage, double dyNorthPage, double duWorld, double dvWorld, double scale)
    {
        double targetWorld = Math.Max(duWorld, dvWorld) / 5.0;
        double barWorld = NiceScaleLength(targetWorld);
        double barPx = barWorld * scale;
        double barY = area.Bottom - legendH + Units.MmToPt(4);
        double barX = area.X + Units.MmToPt(6);
        var barPen = new XPen(XColors.Black, 1.2);
        g.DrawLine(barPen, barX, barY, barX + barPx, barY);
        g.DrawLine(barPen, barX, barY - 3, barX, barY + 3);
        g.DrawLine(barPen, barX + barPx, barY - 3, barX + barPx, barY + 3);
        g.DrawString(FormatWorldLength(barWorld), NovatlasTheme.FontBody(7), XBrushes.Black,
            new XRect(barX, barY + 3, 80, 10), XStringFormats.TopLeft);

        DrawNorthArrow(g, area, dxNorthPage, dyNorthPage, Units.MmToPt(14));

        var legendFont = NovatlasTheme.FontBody(7.5);
        var measBrush = new XSolidBrush(MeasuredColor);
        var theoPen = new XPen(TheoColor, 1.0);
        var coteLegendBrush = new XSolidBrush(NovatlasTheme.ResolveOrange(_currentRoot));

        double legY = area.Bottom + Units.MmToPt(2);
        double lx = area.X;
        g.DrawEllipse(measBrush, lx, legY + 3, 5, 5);
        g.DrawString("Point mesuré", legendFont, XBrushes.Black, new XRect(lx + 9, legY, 80, 10), XStringFormats.TopLeft);
        lx += 90;
        g.DrawEllipse(theoPen, lx, legY + 3, 5, 5);
        g.DrawString("Point théorique (sur ligne)", legendFont, XBrushes.Black, new XRect(lx + 9, legY, 140, 10), XStringFormats.TopLeft);
        lx += 150;
        g.DrawLine(new XPen(XColors.Black, 1.5), lx, legY + 5, lx + 16, legY + 5);
        g.DrawString("Ligne de référence", legendFont, XBrushes.Black, new XRect(lx + 20, legY, 100, 10), XStringFormats.TopLeft);

        // 2e rangée : convention de cotation (Dl/Dt) + sens de la ligne (flèche rouge) - ajoutés sur
        // demande utilisateur pour rendre les croquis auto-explicatifs.
        double legY2 = legY + Units.MmToPt(4.5);
        double lx2 = area.X;
        g.DrawString("Dl / Dt", NovatlasTheme.FontBodyBold(7.5), coteLegendBrush, new XRect(lx2, legY2, 40, 10), XStringFormats.TopLeft);
        g.DrawString(": écart longitudinal (le long) / transversal (perpendiculaire)", legendFont, XBrushes.Black,
            new XRect(lx2 + 32, legY2, 260, 10), XStringFormats.TopLeft);
        lx2 += 300;
        DrawDirectionArrow(g, new XPoint(lx2, legY2 + 5), new XPoint(lx2 + 16, legY2 + 5));
        g.DrawString("Sens de la ligne (start → end)", legendFont, XBrushes.Black, new XRect(lx2 + 20, legY2, 140, 10), XStringFormats.TopLeft);
    }

    // Encart "Situation" : repositionne la zone courante au sein de l'ensemble des zones du document
    // (toutes affichées en petit point numéroté), avec un cercle mettant en évidence la zone
    // courante (rayon = rayon de regroupement pour une zone groupée, demi-étendue pour une ligne
    // longue en pleine page) - permet de se repérer sans dérouler tout le rapport.
    private static void DrawLocatorInset(XGraphics g, XRect box, List<Zone> allZones, int currentIndex)
    {
        g.DrawRectangle(XBrushes.White, box);
        g.DrawRectangle(new XPen(LineGray, 0.8), box);
        g.DrawString("Situation", NovatlasTheme.FontBodyBold(7), XBrushes.Black, new XRect(box.X + 3, box.Y + 2, box.Width - 6, 10), XStringFormats.TopLeft);
        var inner = new XRect(box.X + 3, box.Y + 13, box.Width - 6, box.Height - 16);

        double minX = allZones.Min(z => z.MinX), maxX = allZones.Max(z => z.MaxX);
        double minY = allZones.Min(z => z.MinY), maxY = allZones.Max(z => z.MaxY);
        double du = Math.Max(1.0, maxX - minX), dv = Math.Max(1.0, maxY - minY);
        double maxRing = allZones.Count > 0 ? allZones.Max(ZoneDisplayRadiusMeters) : 1.0;
        double pad = Math.Max(du, dv) * 0.15 + maxRing * 0.3;
        minX -= pad; maxX += pad; minY -= pad; maxY += pad;
        du = maxX - minX; dv = maxY - minY;

        double scale = Math.Min(inner.Width / du, inner.Height / dv);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        double usedW = du * scale, usedH = dv * scale;
        double offX = Math.Max(0, (inner.Width - usedW) / 2.0);
        double offY = Math.Max(0, (inner.Height - usedH) / 2.0);
        XPoint Map(double x, double y) => new XPoint(inner.X + offX + (x - minX) * scale, inner.Y + offY + usedH - (y - minY) * scale);

        var dotBrush = new XSolidBrush(XColor.FromArgb(160, 160, 160));
        var idxFont = NovatlasTheme.FontBody(4.5);
        foreach (var z in allZones)
        {
            if (z.Index == currentIndex) continue;
            var c = Map(z.CenterE, z.CenterN);
            g.DrawEllipse(dotBrush, c.X - 1.1, c.Y - 1.1, 2.2, 2.2);
            g.DrawString(z.Index.ToString(CultureInfo.InvariantCulture), idxFont, XBrushes.Gray, new XRect(c.X + 1.5, c.Y - 4, 16, 8), XStringFormats.TopLeft);
        }

        var cur = allZones.First(z => z.Index == currentIndex);
        var cc = Map(cur.CenterE, cur.CenterN);
        double ringRadiusM = ZoneDisplayRadiusMeters(cur);
        double ringPx = ringRadiusM * scale;
        var ringPen = new XPen(ZoneRingColor, 1.1) { DashStyle = XDashStyle.Dash };
        g.DrawEllipse(ringPen, cc.X - ringPx, cc.Y - ringPx, ringPx * 2, ringPx * 2);
        g.DrawEllipse(new XSolidBrush(MeasuredColor), cc.X - 1.8, cc.Y - 1.8, 3.6, 3.6);
        g.DrawString($"Zone {currentIndex}", NovatlasTheme.FontBodyBold(6), new XSolidBrush(MeasuredColor),
            new XRect(cc.X + 3, cc.Y - 9, 40, 9), XStringFormats.TopLeft);
    }

    // Référence légère au root du payload pour la couleur de marque (ResolveBlue) - alimentée une
    // fois par AppendFromPayload avant l'itération des zones, même pattern que
    // LigneReferenceReportRenderer._currentRoot.
    private static JsonElement _currentRoot;
}
