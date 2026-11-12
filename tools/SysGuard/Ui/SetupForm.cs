using System.Drawing;
using System.Windows.Forms;

namespace SysGuard;

/// <summary>Janela do instalador/reparo. Roda elevada; sem desenho além do necessário.</summary>
internal sealed class SetupForm : Form
{
    private readonly bool _repair;
    private readonly bool _needsPassword;
    private readonly TextBox? _password;
    private readonly TextBox? _confirmation;
    private readonly Button _action;
    private readonly Button _cancel;
    private readonly Label _status;
    private bool _busy;

    public SetupForm(bool repair, bool needsPassword)
    {
        _repair = repair;
        _needsPassword = needsPassword;

        Text = "SysGuard";
        ClientSize = new Size(430, needsPassword ? 330 : 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = true;
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        AutoScaleMode = AutoScaleMode.Dpi;

        var heading = new Label
        {
            Text = repair ? "Reparar proteção" : "Ativar proteção",
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 12f, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
        };

        var description = new Label
        {
            Text = needsPassword
                ? "Crie a senha usada para desativar a proteção. Ela não pode ser recuperada: guarde-a bem."
                : "A proteção já está instalada. Reparar mantém a senha atual e reaplica as configurações do sistema.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 62,
        };

        int y = 14;
        heading.Location = new Point(16, y);
        heading.Width = ClientSize.Width - 32;
        description.Location = new Point(16, y + 32);
        description.Width = ClientSize.Width - 32;

        int nextY = 108;
        if (needsPassword)
        {
            var passwordLabel = new Label { Text = "Senha:", Location = new Point(16, nextY), AutoSize = true };
            _password = new TextBox
            {
                Location = new Point(16, nextY + 20),
                Width = ClientSize.Width - 32,
                UseSystemPasswordChar = true,
                MaxLength = 128,
            };
            var confirmationLabel = new Label { Text = "Confirme a senha:", Location = new Point(16, nextY + 52), AutoSize = true };
            _confirmation = new TextBox
            {
                Location = new Point(16, nextY + 72),
                Width = ClientSize.Width - 32,
                UseSystemPasswordChar = true,
                MaxLength = 128,
            };
            Controls.Add(passwordLabel);
            Controls.Add(_password);
            Controls.Add(confirmationLabel);
            Controls.Add(_confirmation);
            nextY += 104;
        }

        _status = new Label
        {
            Location = new Point(16, nextY),
            Width = ClientSize.Width - 32,
            Height = 28,
            ForeColor = Color.DimGray,
            Text = _repair ? "Pronto para reparar." : "Pronto para instalar.",
        };

        _action = new Button
        {
            Text = repair ? "Reparar" : "Ativar proteção",
            Width = 140,
            Height = 32,
            Location = new Point(ClientSize.Width - 156, ClientSize.Height - 50),
        };
        _cancel = new Button
        {
            Text = "Cancelar",
            Width = 100,
            Height = 32,
            Location = new Point(ClientSize.Width - 264, ClientSize.Height - 50),
            DialogResult = DialogResult.Cancel,
        };

        _action.Click += async (_, _) => await RunInstallAsync();
        CancelButton = _cancel;
        AcceptButton = _action;

        Controls.Add(heading);
        Controls.Add(description);
        Controls.Add(_status);
        Controls.Add(_action);
        Controls.Add(_cancel);

        Shown += (_, _) => (_password ?? (Control?)_action)?.Focus();
        FormClosing += (_, e) =>
        {
            if (!_busy) return;
            e.Cancel = true;
            _status.Text = "Aguarde a operação terminar...";
        };
    }

    private async Task RunInstallAsync()
    {
        if (_busy) return;

        string? password = null;
        if (_needsPassword)
        {
            password = _password!.Text;
            if (!PasswordHasher.IsAcceptable(password, out var error))
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _password.Focus();
                return;
            }

            if (!string.Equals(password, _confirmation!.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "As senhas não conferem.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _confirmation.Focus();
                return;
            }
        }

        SetBusy(true, _repair ? "Reparando..." : "Instalando...");
        string? exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var outcome = await Task.Run(() => ServiceInstaller.Install(exe, password));
        SetBusy(false, outcome.Success ? "Concluído." : "Falhou.");

        if (!outcome.Success)
        {
            MessageBox.Show(
                this,
                "Não foi possível ativar a proteção.\n\n" + outcome.Error,
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            _repair
                ? "Proteção reparada. O ícone fica na bandeja, nos ícones ocultos."
                : "Proteção ativada. O ícone fica na bandeja, nos ícones ocultos (seta para cima ao lado do relógio).",
            Text,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _action.Enabled = !busy;
        _cancel.Enabled = !busy;
        if (_password is not null) _password.Enabled = !busy;
        if (_confirmation is not null) _confirmation.Enabled = !busy;
        _status.Text = status;
    }
}
