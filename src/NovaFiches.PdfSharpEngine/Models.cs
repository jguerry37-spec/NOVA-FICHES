namespace NovaFiches.PdfSharpEngine;

public sealed class ImplantationTablePayload
{
    public string? Title { get; set; } // e.g. "IMPLANTATION"
    public string? SubTitle { get; set; } // optional
    public string[]? Header { get; set; } // 11 columns
    public List<string[]> Rows { get; set; } = new();
    public string? FooterLeft { get; set; }  // optional
    public string? FooterRight { get; set; } // optional
}

public sealed class RapportCompletPayload
{
    /// <summary>Simple key/value fields printed on cover page.</summary>
    public Dictionary<string, string> Info { get; set; } = new();

    /// <summary>IMPLANTATION table.</summary>
    public ImplantationTablePayload Implantation { get; set; } = new();

    /// <summary>MESURE SUR LIGNE table.</summary>
    public ImplantationTablePayload MesureSurLigne { get; set; } = new();
}
