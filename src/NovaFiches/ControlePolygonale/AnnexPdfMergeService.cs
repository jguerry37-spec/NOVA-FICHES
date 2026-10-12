using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TopoRapportWin.ControlePolygonale;

// Fusionne des fichiers PDF quelconques choisis par l'utilisateur (étape "10. Annexes" du Rapport
// de polygonale) à la fin du rapport déjà généré sur disque - même mécanique de fusion que
// NgfFicheMergeService (PdfReader.Open en mode Modify sur le fichier déjà écrit, Import sur
// chaque PDF source, AddPage par AddPage), mais sans recherche/téléchargement réseau : les octets
// sont déjà en main côté appelant (lus en pur JS via FileReader côté formulaire, transmis en
// base64 dans le payload d'export).
internal static class AnnexPdfMergeService
{
    public static int MergeRawPdfs(string pdfPath, IReadOnlyList<byte[]> annexPdfBytes)
    {
        var valid = annexPdfBytes.Where(b => b != null && b.Length > 0).ToList();
        if (valid.Count == 0) return 0;

        int appended = 0;
        try
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            foreach (var bytes in valid)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var annexDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < annexDoc.PageCount; i++)
                    {
                        doc.AddPage(annexDoc.Pages[i]);
                        appended++;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Annexe PDF non intégrée (fichier illisible ou corrompu)", ex);
                }
            }
            if (appended > 0) doc.Save(pdfPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Annexes PDF : ouverture/sauvegarde du rapport pour fusion échouée", ex);
            return 0;
        }
        return appended;
    }
}
