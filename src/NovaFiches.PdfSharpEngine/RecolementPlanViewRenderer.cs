using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Additional page for "Récolement de pieux": plan view of all theoretical TXT points,
/// with highlight around the controlled pieu.
/// Pure rendering (no business logic / no parsing changes).
/// </summary>
public static class RecolementPlanViewRenderer
{
    private const double MarginL = 36;
    private const double MarginR = 36;

    private static readonly XColor BrandBlue = XColor.FromArgb(18, 103, 243);
    private static readonly XColor LineGray  = XColor.FromArgb(200, 200, 200);
    // NOVATLAS orange (from brand guidelines)
    private static readonly XColor NovatlasOrange = XColor.FromArgb(255, 90, 23);

    private static PdfPage AddPage(PdfDocument doc)
    {
        var p = doc.AddPage();
        p.Size = PdfSharp.PageSize.A4;
        return p;
    }

    private static string GetStr(JsonElement root, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (root.ValueKind != JsonValueKind.Object) return "";
            if (!root.TryGetProperty(k, out root)) return "";
        }
        return root.ValueKind == JsonValueKind.String ? (root.GetString() ?? "") : (root.ToString() ?? "");
    }

    private static void DrawHeader(XGraphics g, PdfPage page, JsonElement root)
    {
        double y = Units.MmToPt(6);
        double boxH = Units.MmToPt(22);
        double contentW = page.Width.Point - MarginL - MarginR;
        double gap = Units.MmToPt(6);
        double leftW = Units.MmToPt(65);
        if (leftW > contentW * 0.45) leftW = contentW * 0.45;
        double rightW = contentW - leftW - gap;
        if (rightW < Units.MmToPt(60))
        {
            leftW = contentW * 0.35;
            rightW = contentW - leftW - gap;
        }

        var penBox = new XPen(XColors.Black, 0.8);
        var rectLeft = new XRect(MarginL, y, leftW, boxH);
        var rectRight = new XRect(MarginL + leftW + gap, y, rightW, boxH);
        g.DrawRectangle(penBox, rectLeft);
        g.DrawRectangle(penBox, rectRight);

        // Logo
        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double pad = Units.MmToPt(4);
            double maxW = rectLeft.Width - 2 * pad;
            double maxH = rectLeft.Height - 2 * pad;
            double w = maxW, h = maxH;
            try
            {
                double ar = (double)logo.PixelWidth / (double)logo.PixelHeight;
                w = maxW;
                h = w / ar;
                if (h > maxH) { h = maxH; w = h * ar; }
            }
            catch { }
            double x = rectLeft.X + (rectLeft.Width - w) / 2.0;
            double yy = rectLeft.Y + (rectLeft.Height - h) / 2.0;
            g.DrawImage(logo, x, yy, w, h);
        }

        // Right box: Ville / Adresse / CHA
        string ville = (GetStr(root, "ville") ?? "").Trim();
        string adr = (GetStr(root, "adresseChantier") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(adr)) adr = (GetStr(root, "adresse") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(adr)) adr = (GetStr(root, "siteAddress") ?? "").Trim();
        string cha = (GetStr(root, "cha") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(cha) && !cha.StartsWith("CHA", StringComparison.OrdinalIgnoreCase))
            cha = "CHA" + cha;

        var fontVille = NovatlasTheme.FontBold(12);
        var fontAdr = NovatlasTheme.FontBold(11);
        var fontCha = NovatlasTheme.FontBody(10);

        double pad2 = Units.MmToPt(3);
        var inner = new XRect(rectRight.X + pad2, rectRight.Y + pad2, rectRight.Width - 2 * pad2, rectRight.Height - 2 * pad2);

        // 3 centered lines
        double hVille = g.MeasureString("Ag", fontVille).Height;
        double hAdr = g.MeasureString("Ag", fontAdr).Height;
        double hCha = g.MeasureString("Ag", fontCha).Height;
        double totalH = hVille + hAdr + hCha + 2;
        double y0 = inner.Y + (inner.Height - totalH) / 2.0;

        g.DrawString(ville, fontVille, XBrushes.Black, new XRect(inner.X, y0, inner.Width, hVille), XStringFormats.Center);
        g.DrawString(adr, fontAdr, XBrushes.Black, new XRect(inner.X, y0 + hVille, inner.Width, hAdr), XStringFormats.Center);
        g.DrawString(cha, fontCha, XBrushes.Black, new XRect(inner.X, y0 + hVille + hAdr, inner.Width, hCha), XStringFormats.Center);
    }

    private static void DrawTitleBar(XGraphics g, PdfPage page, ref double y, string title)
    {
        double barH = Units.MmToPt(10);
        double w = page.Width.Point - MarginL - MarginR;
        var rect = new XRect(MarginL, y, w, barH);
        g.DrawRectangle(new XSolidBrush(BrandBlue), rect);
        g.DrawString(title, NovatlasTheme.FontBold(12), XBrushes.White, rect, XStringFormats.Center);
        y += barH + Units.MmToPt(4);
    }

    private static double NiceScaleLength(double target)
    {
        // World units are meters in typical projects.
        double[] nice = new[] { 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500 };
        double best = nice[0];
        foreach (var v in nice)
        {
            if (v <= target) best = v;
            else break;
        }
        return best;
    }

    private static bool GetBool(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el)) return false;
        return el.ValueKind == JsonValueKind.True
            || (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b);
    }

    private static string GetStringProp(JsonElement obj, string key)
    {
        return obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : "";
    }

    private static void DrawCross(XGraphics g, XPen pen, XPoint p, double r)
    {
        g.DrawLine(pen, p.X - r, p.Y, p.X + r, p.Y);
        g.DrawLine(pen, p.X, p.Y - r, p.X, p.Y + r);
    }

    private static void DrawNorthArrow(XGraphics g, XRect inner, double dxPage, double dyPage)
    {
        double len = Units.MmToPt(16);
        double mag = Math.Sqrt(dxPage * dxPage + dyPage * dyPage);
        if (mag <= 1e-9) { dxPage = 0; dyPage = -1; mag = 1; }
        double ux = dxPage / mag;
        double uy = dyPage / mag;

        var c = new XPoint(inner.Right - Units.MmToPt(16), inner.Y + Units.MmToPt(17));
        var a = new XPoint(c.X - ux * len / 2.0, c.Y - uy * len / 2.0);
        var b = new XPoint(c.X + ux * len / 2.0, c.Y + uy * len / 2.0);
        var pen = new XPen(XColors.Black, 1.2);
        g.DrawLine(pen, a, b);

        double ah = Units.MmToPt(3.2);
        double px = -uy;
        double py = ux;
        var h1 = new XPoint(b.X - ux * ah + px * ah * 0.55, b.Y - uy * ah + py * ah * 0.55);
        var h2 = new XPoint(b.X - ux * ah - px * ah * 0.55, b.Y - uy * ah - py * ah * 0.55);
        g.DrawLine(pen, b, h1);
        g.DrawLine(pen, b, h2);
        g.DrawString("N", NovatlasTheme.FontBold(9), XBrushes.Black,
            new XRect(b.X - Units.MmToPt(4), b.Y - Units.MmToPt(7), Units.MmToPt(8), Units.MmToPt(5)),
            XStringFormats.Center);
    }

    public static void Render(PdfDocument doc, JsonElement root, JsonElement planView)
    {
        // Parse points
        List<(string id, double x, double y, double? b, string key)> ParsePts(JsonElement parent, string prop)
        {
            var list = new List<(string id, double x, double y, double? b, string key)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (parent.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in arr.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    string id = it.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                    string key = it.TryGetProperty("key", out var keyEl) ? (keyEl.GetString() ?? "") : "";
                    double x = it.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xd) ? xd : double.NaN;
                    double y = it.TryGetProperty("y", out var yEl) && yEl.TryGetDouble(out var yd) ? yd : double.NaN;
                    double? b = null;
                    if (it.TryGetProperty("base", out var bEl) && bEl.TryGetDouble(out var bd)) b = bd;
                    if (string.IsNullOrWhiteSpace(id)) id = !string.IsNullOrWhiteSpace(key) ? key : (b?.ToString() ?? "");
                    if (!double.IsFinite(x) || !double.IsFinite(y)) continue;
                    string identity = !string.IsNullOrWhiteSpace(key)
                        ? $"K:{key}"
                        : $"P:{id}|{Math.Round(x, 6)}|{Math.Round(y, 6)}";
                    if (!seen.Add(identity)) continue;
                    list.Add((id, x, y, b, key));
                }
            }
                        return list;
        }

        List<(double ax, double ay, double bx, double by, double cx, double cy)> ParseDxfTriangles(JsonElement parent)
        {
            var list = new List<(double ax, double ay, double bx, double by, double cx, double cy)>();
            if (!parent.TryGetProperty("dxfTriangles", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;

            static bool ReadPt(JsonElement item, string key, out double x, out double y)
            {
                x = double.NaN;
                y = double.NaN;
                if (!item.TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.Object) return false;
                if (!p.TryGetProperty("x", out var xEl) || !xEl.TryGetDouble(out x)) return false;
                if (!p.TryGetProperty("y", out var yEl) || !yEl.TryGetDouble(out y)) return false;
                return double.IsFinite(x) && double.IsFinite(y);
            }

            foreach (var it in arr.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;
                if (!ReadPt(it, "a", out var ax, out var ay)) continue;
                if (!ReadPt(it, "b", out var bx, out var by)) continue;
                if (!ReadPt(it, "c", out var cx, out var cy)) continue;
                list.Add((ax, ay, bx, by, cx, cy));
            }
            return list;
        }

        // pointsAll: tous les points théoriques (TXT) affichés en points
        // pointsImplanted: sous-ensemble implanté/validé (cerclé + étiqueté)
        var ptsAll = planView.TryGetProperty("pointsAll", out _) ? ParsePts(planView, "pointsAll") : ParsePts(planView, "points");
        var dxfTriangles = ParseDxfTriangles(planView);
        string style = GetStringProp(planView, "style");
        bool mntStyle = string.Equals(style, "mnt", StringComparison.OrdinalIgnoreCase);
        bool hasPointsImplanted = planView.TryGetProperty("pointsImplanted", out var implantedEl)
            && implantedEl.ValueKind == JsonValueKind.Array;
        var ptsImplanted = ParsePts(planView, "pointsImplanted");
        int implantedCount = ptsImplanted.Count;
        if (ptsImplanted.Count == 0 && !mntStyle && !hasPointsImplanted)
        {
            ptsImplanted = ptsAll;
            implantedCount = ptsAll.Count;
        }

        string controlledKey = planView.TryGetProperty("controlledKey", out var ckEl) && ckEl.ValueKind == JsonValueKind.String ? (ckEl.GetString() ?? "") : "";
        double? controlledBase = null;
        if (planView.TryGetProperty("controlledBase", out var cEl))
        {
            if (cEl.ValueKind == JsonValueKind.Number && cEl.TryGetDouble(out var cd)) controlledBase = cd;
        }

        var page = AddPage(doc);
        using var g = XGraphics.FromPdfPage(page);

        // Header + title
        DrawHeader(g, page, root);
        double yTop = Units.MmToPt(6) + Units.MmToPt(22) + Units.MmToPt(6);
        DrawTitleBar(g, page, ref yTop, planView.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "VUE EN PLAN") : "VUE EN PLAN");

        // Plan frame area (leave space for footer)
        double footerSafe = Units.MmToPt(26);
        double frameX = MarginL;
        double frameY = yTop;
        double frameW = page.Width.Point - MarginL - MarginR;
        double frameH = page.Height.Point - frameY - footerSafe;

        var frame = new XRect(frameX, frameY, frameW, frameH);
        g.DrawRectangle(new XPen(LineGray, 0.8), frame);

        if (ptsAll.Count == 0)
        {
            g.DrawString("Aucun point théorique (TXT) à afficher.", NovatlasTheme.FontBody(10), XBrushes.Black,
                new XRect(frame.X, frame.Y + Units.MmToPt(10), frame.Width, Units.MmToPt(10)), XStringFormats.Center);
            return;
        }

        bool hideLabels = mntStyle || GetBool(planView, "hideLabels");
        bool hideImplantedRings = mntStyle || GetBool(planView, "hideImplantedRings");
        bool showNorthArrow = mntStyle || GetBool(planView, "showNorthArrow");
        bool avoidRingOverlap = GetBool(planView, "avoidRingOverlap");
        bool emphasizeControlled = !planView.TryGetProperty("emphasizeControlled", out _)
            || GetBool(planView, "emphasizeControlled");
        string markerShape = GetStringProp(planView, "markerShape");

        var extentXs = ptsAll.Select(p => p.x).ToList();
        var extentYs = ptsAll.Select(p => p.y).ToList();
        foreach (var t in dxfTriangles)
        {
            extentXs.Add(t.ax); extentXs.Add(t.bx); extentXs.Add(t.cx);
            extentYs.Add(t.ay); extentYs.Add(t.by); extentYs.Add(t.cy);
        }

        double rawMinX = extentXs.Min();
        double rawMaxX = extentXs.Max();
        double rawMinY = extentYs.Min();
        double rawMaxY = extentYs.Max();
        double rawDx = Math.Max(0.001, rawMaxX - rawMinX);
        double rawDy = Math.Max(0.001, rawMaxY - rawMinY);
        bool rotateLongAxisVertical = GetBool(planView, "rotateLongAxisVertical") && rawDx > rawDy;

        (double u, double v) Project(double x, double y)
        {
            // Normal: north is page-up. Rotated: long east-west axis becomes page-vertical,
            // so north points page-right and is drawn by the north arrow.
            return rotateLongAxisVertical ? (y, -x) : (x, y);
        }

        var projected = ptsAll.Select(p => Project(p.x, p.y)).ToList();
        foreach (var t in dxfTriangles)
        {
            projected.Add(Project(t.ax, t.ay));
            projected.Add(Project(t.bx, t.by));
            projected.Add(Project(t.cx, t.cy));
        }
        double minU = projected.Min(p => p.u);
        double maxU = projected.Max(p => p.u);
        double minV = projected.Min(p => p.v);
        double maxV = projected.Max(p => p.v);

        double du = Math.Max(0.001, maxU - minU);
        double dv = Math.Max(0.001, maxV - minV);
        double eu = du * 0.05;
        double ev = dv * 0.05;
        minU -= eu; maxU += eu;
        minV -= ev; maxV += ev;
        du = Math.Max(0.001, maxU - minU);
        dv = Math.Max(0.001, maxV - minV);

        // Inner padding in frame
        double pad = Units.MmToPt(6);
        var inner = new XRect(frame.X + pad, frame.Y + pad, frame.Width - 2 * pad, frame.Height - 2 * pad);

        double scale = Math.Min(inner.Width / du, inner.Height / dv);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        double usedW = du * scale;
        double usedH = dv * scale;
        double offsetX = Math.Max(0, (inner.Width - usedW) / 2.0);
        double offsetY = Math.Max(0, (inner.Height - usedH) / 2.0);

        // Projected world -> page transform, centered in the available frame.
        XPoint Map(double x, double y)
        {
            var q = Project(x, y);
            double px = inner.X + offsetX + (q.u - minU) * scale;
            double py = inner.Y + offsetY + usedH - (q.v - minV) * scale;
            return new XPoint(px, py);
        }

        // Draw points
        var penPoint = new XPen(mntStyle ? BrandBlue : XColors.Black, 0.8);
        var brushPoint = mntStyle ? new XSolidBrush(BrandBlue) : XBrushes.Black;
        // Highlight (Option B): orange outline only (no fill) to avoid darker overlap zones when points are close.
        var penHi = new XPen(NovatlasOrange, 1.1);

        double r = 1.7;
        double rHi = 5.0;

        // Labels: 4 candidates, avoid overlaps
        var fontLbl = NovatlasTheme.FontBody(8);
        var occupied = new List<XRect>();

        bool IsFree(XRect rect)
        {
            foreach (var o in occupied)
            {
                if (o.IntersectsWith(rect)) return false;
            }
            return true;
        }

        // Draw all points. MNT mode uses small crosses, not circles.
        foreach (var p in ptsAll)
        {
            var pt = Map(p.x, p.y);
            if (mntStyle || string.Equals(markerShape, "cross", StringComparison.OrdinalIgnoreCase))
                DrawCross(g, penPoint, pt, Units.MmToPt(1.2));
            else
                g.DrawEllipse(penPoint, brushPoint, pt.X - r, pt.Y - r, 2 * r, 2 * r);
        }

        // Highlight + label only implanted/valid points
        foreach (var p in ptsImplanted)
        {
            var pt = Map(p.x, p.y);
            double ringRadius = rHi;
            if (avoidRingOverlap && ptsImplanted.Count > 1)
            {
                double nearest = double.PositiveInfinity;
                foreach (var other in ptsImplanted)
                {
                    if (string.Equals(other.key, p.key, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(other.x - p.x) < 1e-9
                        && Math.Abs(other.y - p.y) < 1e-9) continue;
                    var op = Map(other.x, other.y);
                    nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(op.X - pt.X, 2) + Math.Pow(op.Y - pt.Y, 2)));
                }
                if (double.IsFinite(nearest))
                    ringRadius = Math.Max(2.8, Math.Min(rHi, nearest * 0.36));
            }

            // Outline around implanted points (no fill). Hidden for MNT plan views.
            if(!hideImplantedRings) g.DrawEllipse(penHi, pt.X - ringRadius, pt.Y - ringRadius, 2 * ringRadius, 2 * ringRadius);

            // Extra emphasis for the controlled pieu (if provided)
            bool isControlled = (!string.IsNullOrWhiteSpace(controlledKey) && string.Equals(p.key, controlledKey, StringComparison.OrdinalIgnoreCase))
                || (controlledBase != null && p.b != null && Math.Abs(p.b.Value - controlledBase.Value) < 0.0001);
            if (isControlled && emphasizeControlled)
            {
                // Extra emphasis for the selected/controlled pieu (stronger outline)
                if(!hideImplantedRings) g.DrawEllipse(new XPen(NovatlasOrange, 2.2), pt.X - (ringRadius + 2), pt.Y - (ringRadius + 2), 2 * (ringRadius + 2), 2 * (ringRadius + 2));
            }

            // Label placement (only implanted)
            // Goal: ALWAYS draw all implanted labels.
            // Rule:
            //  1) Try non-overlapping positions around the point (spiral offsets, 8 directions)
            //  2) If still blocked, reduce font size down to 6pt
            //  3) If still blocked, place it clamped inside the frame even if it overlaps (last resort)
            if (hideLabels) continue;
            var text = p.id;
            if (string.IsNullOrWhiteSpace(text)) continue;

            XRect chosen = default;
            XFont chosenFont = fontLbl;
            bool ok = false;

            // directions: NE, NW, SE, SW, E, W, N, S
            var dir = new (int dx, int dy)[]
            {
                ( 1, -1), (-1, -1), ( 1,  1), (-1,  1),
                ( 1,  0), (-1,  0), ( 0, -1), ( 0,  1),
            };

            for (int fs = 9; fs >= 6 && !ok; fs--)
            {
                var fnt = NovatlasTheme.FontBody(fs);
                var size = g.MeasureString(text, fnt);
                double w = size.Width;
                double h = size.Height;

                for (int step = 0; step <= 6 && !ok; step++)
                {
                    double off = 3 + step * 6;
                    foreach (var d in dir)
                    {
                        double x = pt.X + d.dx * off;
                        double y = pt.Y + d.dy * off;

                        // Anchor depends on quadrant so the label sits away from the point.
                        if (d.dx < 0) x -= w;
                        if (d.dy < 0) y -= h;

                        var rr = new XRect(x, y, w, h);
                        if (!inner.Contains(rr)) continue;
                        if (IsFree(rr))
                        {
                            chosen = rr;
                            chosenFont = fnt;
                            ok = true;
                            break;
                        }
                    }
                }
            }

            // Last resort: clamp inside frame even if overlaps
            if (!ok)
            {
                var size = g.MeasureString(text, chosenFont);
                double w = size.Width;
                double h = size.Height;
                double x = pt.X + 6;
                double y = pt.Y - h - 6;
                // clamp
                x = Math.Max(inner.X, Math.Min(x, inner.Right - w));
                y = Math.Max(inner.Y, Math.Min(y, inner.Bottom - h));
                chosen = new XRect(x, y, w, h);
                ok = true;
            }

            if (ok)
            {
                // Small white halo to keep text readable over the orange pochage
                var halo = XColor.FromArgb(180, 255, 255, 255);
                g.DrawRectangle(new XSolidBrush(halo), chosen);
                g.DrawString(text, chosenFont, XBrushes.Black, chosen, XStringFormats.TopLeft);
                occupied.Add(chosen);
            }
        }

// Scale bar (bottom-left inside frame)
        if (showNorthArrow)
        {
            // Page direction of geographic north (increase in source Y/N).
            double nx = rotateLongAxisVertical ? 1 : 0;
            double ny = rotateLongAxisVertical ? 0 : -1;
            DrawNorthArrow(g, inner, nx, ny);
        }

        double targetWorld = Math.Max(du, dv) / 5.0;
        double barWorld = NiceScaleLength(targetWorld);
        double barPx = barWorld * scale;
        double sx = inner.X + Units.MmToPt(4);
        double sy = inner.Y + inner.Height - Units.MmToPt(6);
        var penScale = new XPen(XColors.Black, 1.2);
        g.DrawLine(penScale, sx, sy, sx + barPx, sy);
        g.DrawLine(penScale, sx, sy - 4, sx, sy + 4);
        g.DrawLine(penScale, sx + barPx, sy - 4, sx + barPx, sy + 4);
        g.DrawString($"{barWorld:g} m", NovatlasTheme.FontBody(9), XBrushes.Black,
            new XRect(sx, sy - Units.MmToPt(8), barPx + Units.MmToPt(20), Units.MmToPt(6)), XStringFormats.TopLeft);
        // Small caption (top-right in frame): keep it minimal. Hidden for MNT plan views.
        if (!mntStyle)
        {
            string cap = $"Points contrôlés : {implantedCount}";
            g.DrawString(cap, NovatlasTheme.FontBody(9), XBrushes.Black,
                new XRect(inner.X, inner.Y - Units.MmToPt(4), inner.Width, Units.MmToPt(6)), XStringFormats.CenterRight);
        }
    }
}
