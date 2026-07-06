using System.Drawing;
using System.Windows.Forms;

namespace TopoRapportWin.Licensing;

/// <summary>
/// Écran d'activation bloquant affiché avant MainForm si aucune licence valide n'est
/// installée. Codée à la main (pas de designer/resx), cohérent avec MainForm.cs.
/// </summary>
public sealed class LicenseForm : Form
{
    private readonly Label _statusLabel;
    private readonly TextBox _machineIdBox;
    private readonly Button _continueBtn;
    private LicenseValidationResult _lastResult;

    public LicenseForm(LicenseValidationResult initialResult)
    {
        _lastResult = initialResult;

        Text = "Nova-Fiches — Activation";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 300);
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "Activation requise",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18)
        };
        Controls.Add(title);

        _statusLabel = new Label
        {
            Text = initialResult.Message,
            ForeColor = StatusColor(initialResult),
            Location = new Point(20, 52),
            Size = new Size(440, 40),
            AutoSize = false
        };
        Controls.Add(_statusLabel);

        var machineLabel = new Label
        {
            Text = "Identifiant de ce poste (à transmettre à Novatlas si besoin) :",
            Location = new Point(20, 100),
            AutoSize = true
        };
        Controls.Add(machineLabel);

        _machineIdBox = new TextBox
        {
            Text = LicenseService.GetCurrentMachineId(),
            ReadOnly = true,
            Location = new Point(20, 123),
            Size = new Size(340, 24)
        };
        Controls.Add(_machineIdBox);

        var copyBtn = new Button
        {
            Text = "Copier",
            Location = new Point(368, 122),
            Size = new Size(90, 26)
        };
        copyBtn.Click += (_, _) =>
        {
            try { Clipboard.SetText(_machineIdBox.Text); }
            catch { /* presse-papier indisponible : sans conséquence */ }
        };
        Controls.Add(copyBtn);

        var selectBtn = new Button
        {
            Text = "Sélectionner un fichier de licence…",
            Location = new Point(20, 168),
            Size = new Size(438, 38),
            BackColor = Color.FromArgb(18, 103, 243),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        selectBtn.FlatAppearance.BorderSize = 0;
        selectBtn.Click += SelectLicenseFile;
        Controls.Add(selectBtn);

        var quitBtn = new Button
        {
            Text = "Quitter",
            Location = new Point(20, 232),
            Size = new Size(110, 32),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(quitBtn);
        CancelButton = quitBtn;

        _continueBtn = new Button
        {
            Text = "Continuer",
            Location = new Point(348, 232),
            Size = new Size(110, 32),
            DialogResult = DialogResult.OK,
            Enabled = initialResult.IsValid
        };
        Controls.Add(_continueBtn);
        AcceptButton = _continueBtn;
    }

    private void SelectLicenseFile(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Sélectionner un fichier de licence",
            Filter = "Licence Nova-Fiches (*.json;*.lic)|*.json;*.lic|Tous les fichiers (*.*)|*.*",
            CheckFileExists = true
        };

        if (ofd.ShowDialog(this) != DialogResult.OK)
            return;

        var result = LicenseService.TryInstall(ofd.FileName);
        _lastResult = result;
        _statusLabel.Text = result.Message;
        _statusLabel.ForeColor = StatusColor(result);
        _continueBtn.Enabled = result.IsValid;

        if (result.IsValid)
        {
            MessageBox.Show(this, "Licence activée avec succès.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, result.Message, "Activation échouée",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Color StatusColor(LicenseValidationResult result) =>
        result.IsValid ? Color.FromArgb(10, 122, 69) : Color.FromArgb(185, 28, 28);
}
