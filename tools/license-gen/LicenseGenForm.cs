using System.Drawing;
using System.Windows.Forms;

namespace LicenseGen;

/// <summary>
/// Écran de génération de licence, alternative graphique aux commandes CLI "genkey"/"issue".
/// Outil interne Novatlas — jamais copié dans le publish de Nova-Fiches (voir Program.cs).
/// </summary>
public sealed class LicenseGenForm : Form
{
    // Modules complémentaires disponibles à la sélection. Ajouter une ligne ici suffit à
    // proposer un nouveau module manager dans l'interface (même liste de clés que
    // BuildFooterLicenseScript côté application, voir src/NovaFiches/MainForm.cs).
    private static readonly (string Key, string Label)[] KnownFeatures =
    {
        ("branding", "Paramètres (logo, pied de page, couleurs)"),
        ("fiches_signaletiques", "Fiches signalétiques (import CSV, export PDF)"),
        ("controle_doublons", "Contrôle de doublons (fusion de fichiers de points)"),
        ("controle_precision", "Contrôle classe de précision (arrêté du 16/09/2003)"),
    };

    private readonly TextBox _keyPathBox;
    private readonly TextBox _clientBox;
    private readonly TextBox _machineIdBox;
    private readonly CheckBox _expiresCheck;
    private readonly DateTimePicker _expiresPicker;
    private readonly CheckedListBox _featuresList;
    private readonly Label _statusLabel;
    private readonly Button _openFolderBtn;
    private string? _lastGeneratedFolder;

    public LicenseGenForm()
    {
        Text = "Nova-Fiches — Génération de licence";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* icone par defaut si indisponible */ }
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 480);
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "Génération de licence",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 16)
        };
        Controls.Add(title);

        var y = 56;

        var keyLabel = new Label { Text = "Clé privée :", Location = new Point(20, y), AutoSize = true };
        Controls.Add(keyLabel);
        y += 22;

        _keyPathBox = new TextBox { Location = new Point(20, y), Size = new Size(370, 24) };
        Controls.Add(_keyPathBox);

        var keyBrowseBtn = new Button { Text = "Parcourir…", Location = new Point(398, y - 1), Size = new Size(100, 26) };
        keyBrowseBtn.Click += BrowseKey;
        Controls.Add(keyBrowseBtn);
        y += 34;

        PrefillKeyPath();

        var clientLabel = new Label { Text = "Nom du client :", Location = new Point(20, y), AutoSize = true };
        Controls.Add(clientLabel);
        y += 22;

        _clientBox = new TextBox { Location = new Point(20, y), Size = new Size(478, 24) };
        Controls.Add(_clientBox);
        y += 36;

        var machineLabel = new Label { Text = "Identifiant machine (optionnel — laisser vide pour une licence portable) :", Location = new Point(20, y), AutoSize = true };
        Controls.Add(machineLabel);
        y += 22;

        _machineIdBox = new TextBox { Location = new Point(20, y), Size = new Size(370, 24) };
        Controls.Add(_machineIdBox);

        var thisMachineBtn = new Button { Text = "Ce poste", Location = new Point(398, y - 1), Size = new Size(100, 26) };
        thisMachineBtn.Click += (_, __) => _machineIdBox.Text = MachineId.GetCurrentMachineIdHash();
        Controls.Add(thisMachineBtn);
        y += 40;

        _expiresCheck = new CheckBox { Text = "Date d'expiration :", Location = new Point(20, y), AutoSize = true };
        Controls.Add(_expiresCheck);

        _expiresPicker = new DateTimePicker
        {
            Location = new Point(170, y - 3),
            Size = new Size(180, 24),
            Format = DateTimePickerFormat.Short,
            MinDate = DateTime.Today,
            Value = DateTime.Today.AddYears(1),
            Enabled = false
        };
        _expiresCheck.CheckedChanged += (_, __) => _expiresPicker.Enabled = _expiresCheck.Checked;
        Controls.Add(_expiresPicker);
        y += 40;

        var featuresLabel = new Label { Text = "Modules complémentaires :", Location = new Point(20, y), AutoSize = true };
        Controls.Add(featuresLabel);
        y += 22;

        _featuresList = new CheckedListBox
        {
            Location = new Point(20, y),
            Size = new Size(478, 80),
            CheckOnClick = true
        };
        foreach (var (_, label) in KnownFeatures)
            _featuresList.Items.Add(label);
        Controls.Add(_featuresList);
        y += 96;

        var generateBtn = new Button
        {
            Text = "Générer la licence…",
            Location = new Point(20, y),
            Size = new Size(478, 38),
            BackColor = Color.FromArgb(18, 103, 243),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        generateBtn.FlatAppearance.BorderSize = 0;
        generateBtn.Click += GenerateLicense;
        Controls.Add(generateBtn);
        y += 48;

        _statusLabel = new Label
        {
            Location = new Point(20, y),
            Size = new Size(478, 44),
            AutoSize = false,
            ForeColor = Color.FromArgb(60, 60, 60)
        };
        Controls.Add(_statusLabel);
        y += 48;

        _openFolderBtn = new Button
        {
            Text = "Ouvrir le dossier",
            Location = new Point(20, y),
            Size = new Size(150, 28),
            Visible = false
        };
        _openFolderBtn.Click += (_, __) =>
        {
            if (_lastGeneratedFolder is not null)
                System.Diagnostics.Process.Start("explorer.exe", $"\"{_lastGeneratedFolder}\"");
        };
        Controls.Add(_openFolderBtn);
    }

    private void PrefillKeyPath()
    {
        // Cas d'usage courant : outil lancé depuis la racine du dépôt (dotnet run) ou
        // en double-clic depuis tools/license-gen/bin/<Config>/<TFM>/license-gen.exe. Dans ce
        // second cas, AppContext.BaseDirectory ne contient PAS "keys" directement (c'est un
        // frère de tools/license-gen, pas de bin/.../) : on remonte l'arborescence depuis le
        // dossier de l'exe jusqu'à trouver un "keys/private.key" à côté, plutôt que de
        // supposer une profondeur de build output fixe.
        var candidates = new List<string>
        {
            Path.Combine(Environment.CurrentDirectory, "tools", "license-gen", "keys", "private.key"),
            Path.Combine(Environment.CurrentDirectory, "keys", "private.key"),
        };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "keys", "private.key"));

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null)
            _keyPathBox.Text = found;
    }

    private void BrowseKey(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Sélectionner la clé privée",
            Filter = "Clé privée (*.key;*.txt)|*.key;*.txt|Tous les fichiers (*.*)|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrEmpty(_keyPathBox.Text) && File.Exists(_keyPathBox.Text))
            ofd.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_keyPathBox.Text));

        if (ofd.ShowDialog(this) == DialogResult.OK)
            _keyPathBox.Text = ofd.FileName;
    }

    private void GenerateLicense(object? sender, EventArgs e)
    {
        _statusLabel.ForeColor = Color.FromArgb(185, 28, 28);
        _openFolderBtn.Visible = false;

        if (string.IsNullOrWhiteSpace(_keyPathBox.Text) || !File.Exists(_keyPathBox.Text))
        {
            _statusLabel.Text = "Sélectionne un fichier de clé privée valide.";
            return;
        }
        var client = _clientBox.Text.Trim();
        if (client.Length == 0)
        {
            _statusLabel.Text = "Le nom du client est obligatoire.";
            return;
        }

        var machineId = _machineIdBox.Text.Trim();
        var expiresAtUtc = _expiresCheck.Checked ? _expiresPicker.Value.Date.ToUniversalTime() : (DateTime?)null;
        var features = KnownFeatures
            .Where((_, i) => _featuresList.GetItemChecked(i))
            .Select(f => f.Key)
            .ToArray();

        using var sfd = new SaveFileDialog
        {
            Title = "Enregistrer la licence sous…",
            Filter = "Licence Nova-Fiches (*.json)|*.json",
            FileName = $"license-{SanitizeFileName(client)}.json"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var request = new IssueLicenseRequest(
                _keyPathBox.Text,
                client,
                expiresAtUtc,
                machineId.Length > 0 ? machineId : null,
                features);
            var result = Program.IssueLicense(request);
            File.WriteAllText(sfd.FileName, result.LicenseJson);

            _lastGeneratedFolder = Path.GetDirectoryName(Path.GetFullPath(sfd.FileName));
            _openFolderBtn.Visible = true;

            _statusLabel.ForeColor = Color.FromArgb(10, 122, 69);
            _statusLabel.Text =
                $"Licence générée pour {client}." +
                $" Expiration : {(expiresAtUtc.HasValue ? expiresAtUtc.Value.ToString("yyyy-MM-dd") : "jamais")}." +
                $" Modules : {(features.Length > 0 ? string.Join(", ", features) : "aucun")}.";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.FromArgb(185, 28, 28);
            _statusLabel.Text = "Échec de la génération : " + ex.Message;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars).Trim().Replace(' ', '-');
    }
}
