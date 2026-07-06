using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NovaFiches.PdfSharpEngine;

internal static class ControlsCounter
{
    internal readonly struct Counts
    {
        public readonly int PointsMesures;
        public readonly int Valides;
        public readonly int Refuses;
        public readonly int NonEval;

        public Counts(int pointsMesures, int valides, int refuses, int nonEval)
        {
            PointsMesures = pointsMesures;
            Valides = valides;
            Refuses = refuses;
            NonEval = nonEval;
        }
    }

    // Implantation / Points topo: prefer implantationByStation[*].rows, else root.rows
    internal static Counts ComputeForImplantation(in JsonElement root)
    {
        // Special case: "LEVÉ" (points topo) payloads reuse the Implantation renderer but
        // expose measured points under topoStations[*].results. Those points do not have
        // validation status in this report, so we count them as "Non évalués".
        if (root.TryGetProperty("topoStations", out var topoStations) && topoStations.ValueKind == JsonValueKind.Array)
        {
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var st in topoStations.EnumerateArray())
            {
                if (st.ValueKind != JsonValueKind.Object) continue;
                if (!st.TryGetProperty("results", out var res) || res.ValueKind != JsonValueKind.Array) continue;

                foreach (var p in res.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var id = GetStringAny(p, "id", "ID", "pointId", "pointID");
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
                }
            }

            if (ids.Count > 0)
            {
                int pm = ids.Count;
                return new Counts(pm, 0, 0, pm);
            }
        }

        // implantationByStation: [{stationId, rows:[[...]]}]
        if (root.TryGetProperty("implantationByStation", out var byStation) && byStation.ValueKind == JsonValueKind.Array)
        {
            int pm = 0, va = 0, rf = 0, ne = 0;
            foreach (var st in byStation.EnumerateArray())
            {
                if (!st.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) continue;
                AccumulateFromRowArrays(rows, ref pm, ref va, ref rf, ref ne);
            }
            if (pm > 0) return new Counts(pm, va, rf, ne);
        }

        if (root.TryGetProperty("rows", out var topRows) && topRows.ValueKind == JsonValueKind.Array)
        {
            int pm = 0, va = 0, rf = 0, ne = 0;
            AccumulateFromRowArrays(topRows, ref pm, ref va, ref rf, ref ne);
            return new Counts(pm, va, rf, ne);
        }

        return new Counts(0, 0, 0, 0);
    }

    // Ligne de référence: root.ligneRef contains line blocks with rabPoints[] in JS.
    // Legacy payloads may still provide flat row arrays. Support both shapes.
    internal static Counts ComputeForLigneRef(in JsonElement root)
    {
        if (root.TryGetProperty("ligneRef", out var lr) && lr.ValueKind == JsonValueKind.Array)
        {
            int pm = 0, va = 0, rf = 0, ne = 0;

            foreach (var el in lr.EnumerateArray())
            {
                // Legacy flat row
                if (el.ValueKind == JsonValueKind.Array)
                {
                    pm++;
                    CountStatutFromRowArray(el, ref va, ref rf, ref ne);
                    continue;
                }

                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                // Current shape: one line object with rabPoints[]
                if (el.TryGetProperty("rabPoints", out var pts) && pts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pts.EnumerateArray())
                    {
                        if (p.ValueKind == JsonValueKind.Array)
                        {
                            pm++;
                            CountStatutFromRowArray(p, ref va, ref rf, ref ne);
                            continue;
                        }

                        if (p.ValueKind != JsonValueKind.Object)
                            continue;

                        pm++;
                        var statut = GetStringAny(p, "statut", "STATUT", "status", "Status");
                        CountStatutFromString(statut, ref va, ref rf, ref ne);
                    }
                    continue;
                }

                // Fallback: object row already flattened
                pm++;
                var rowStatut = GetStringAny(el, "statut", "STATUT", "status", "Status");
                CountStatutFromString(rowStatut, ref va, ref rf, ref ne);
            }

            return new Counts(pm, va, rf, ne);
        }

        // Fallback: sometimes payload uses ligneReference.rows
        if (root.TryGetProperty("ligneReference", out var lrObj) && lrObj.ValueKind == JsonValueKind.Object &&
            lrObj.TryGetProperty("rows", out var rows2) && rows2.ValueKind == JsonValueKind.Array)
        {
            int pm = 0, va = 0, rf = 0, ne = 0;
            AccumulateFromRowArrays(rows2, ref pm, ref va, ref rf, ref ne);
            return new Counts(pm, va, rf, ne);
        }

        return new Counts(0, 0, 0, 0);
    }

    // Station: based on stationLibreRuns[*].residuals (or stationLibre.residuals). Status is often absent.
    internal static Counts ComputeForStation(in JsonElement root)
    {
        int pm = 0, va = 0, rf = 0, ne = 0;

        // Preferred: stationLibreRuns
        if (root.TryGetProperty("stationLibreRuns", out var runs) && runs.ValueKind == JsonValueKind.Array)
        {
            foreach (var run in runs.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Object) continue;
                if (!run.TryGetProperty("residuals", out var residuals) || residuals.ValueKind != JsonValueKind.Array) continue;
                AccumulateFromResidualObjects(residuals, ref pm, ref va, ref rf, ref ne);
            }
            if (pm > 0) return new Counts(pm, va, rf, ne);
        }

        // Fallback: stationLibre.residuals
        if (root.TryGetProperty("stationLibre", out var st) && st.ValueKind == JsonValueKind.Object &&
            st.TryGetProperty("residuals", out var residuals2) && residuals2.ValueKind == JsonValueKind.Array)
        {
            AccumulateFromResidualObjects(residuals2, ref pm, ref va, ref rf, ref ne);
            return new Counts(pm, va, rf, ne);
        }

        return new Counts(0, 0, 0, 0);
    }

    private static void AccumulateFromRowArrays(JsonElement rows, ref int pm, ref int va, ref int rf, ref int ne)
    {
        foreach (var r in rows.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Array)
                continue;

            pm++;
            CountStatutFromRowArray(r, ref va, ref rf, ref ne);
        }
    }

    private static void CountStatutFromRowArray(JsonElement rowArray, ref int va, ref int rf, ref int ne)
    {
        // last element is expected to be STATUT
        string statut = "";
        JsonElement last = default;
        foreach (var el in rowArray.EnumerateArray()) last = el;
        if (last.ValueKind == JsonValueKind.String) statut = last.GetString() ?? "";
        else if (last.ValueKind != JsonValueKind.Undefined && last.ValueKind != JsonValueKind.Null) statut = last.ToString();

        CountStatutFromString(statut, ref va, ref rf, ref ne);
    }

    private static void AccumulateFromResidualObjects(JsonElement residuals, ref int pm, ref int va, ref int rf, ref int ne)
    {
        foreach (var r in residuals.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;

            // Respect "used" (AppLog semantics): used == "Non" means excluded.
            var used = GetStringAny(r, "used", "Used", "utilise", "Utilisé");
            if (string.Equals(used, "non", StringComparison.OrdinalIgnoreCase))
                continue;

            pm++;

            // If a statut is present, use it; else count as non-evaluated.
            var statut = GetStringAny(r, "statut", "STATUT", "status", "Status");
            if (!string.IsNullOrWhiteSpace(statut))
                CountStatutFromString(statut, ref va, ref rf, ref ne);
            else
                ne++;
        }
    }

    private static void CountStatutFromString(string? statut, ref int va, ref int rf, ref int ne)
    {
        var s = NormalizeStatut(statut);
        if (s.Length == 0)
        {
            ne++;
            return;
        }

        // Accept accented variants (e.g., "REFUSÉ"), and tolerate extra wording.
        // We normalize diacritics and compare on a relaxed basis.
        if (s.Contains("VALID", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "OUI", StringComparison.OrdinalIgnoreCase))
        {
            va++;
            return;
        }

        if (s.Contains("REFUS", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "NON", StringComparison.OrdinalIgnoreCase))
        {
            rf++;
            return;
        }

        ne++;
    }

    private static string NormalizeStatut(string? statut)
    {
        var s = (statut ?? "").Trim();
        if (s.Length == 0) return "";

        // Uppercase + remove diacritics so "REFUSÉ" becomes "REFUSE".
        s = s.ToUpperInvariant();
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string GetStringAny(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
                if (v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined) return v.ToString();
            }
        }
        return "";
    }
}
