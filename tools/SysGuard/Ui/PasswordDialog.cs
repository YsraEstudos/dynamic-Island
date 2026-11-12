using System.Drawing;
using System.Windows.Forms;

namespace SysGuard;

/// <summary>
/// Caixas de senha pequenas e sem enfeites, no estilo do Windows. O mínimo de desenho possível:
/// um rótulo, um campo mascarado e OK/Cancelar.
/// </summary>
internal static class PasswordDialog
{
    /// <summary>Pede uma senha. Devolve null se o usuário cancelar.</summary>
    public static string? AskPassword(IWin32Window? owner, string caption, string prompt)
    {
        using var form = CreateBaseForm(caption, 380, 170);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label { Text = prompt, Dock = DockStyle.Fill, AutoSize = false };
        var input = new TextBox
        {
            Dock = DockStyle.Top,
            UseSystemPasswordChar = true,
            MaxLength = 128,
        };
        var (buttons, ok, cancel) = CreateButtonRow();

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
        form.Shown += (_, _) => input.Focus();

        var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
        return result == DialogResult.OK ? input.Text : null;
    }

    /// <summary>Pede uma senha nova, com confirmação e validação. Devolve null se cancelar.</summary>
    public static string? AskNewPassword(IWin32Window? owner, string caption, string prompt)
    {
        using var form = CreateBaseForm(caption, 400, 230);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label { Text = prompt, Dock = DockStyle.Fill, AutoSize = false };
        var first = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, MaxLength = 128 };
        var secondLabel = new Label { Text = "Confirme a senha:", Dock = DockStyle.Fill, AutoSize = false };
        var second = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, MaxLength = 128 };
        var (buttons, ok, cancel) = CreateButtonRow();

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(first, 0, 1);
        layout.Controls.Add(secondLabel, 0, 2);
        layout.Controls.Add(second, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            if (!PasswordHasher.IsAcceptable(first.Text, out var error))
            {
                MessageBox.Show(form, error, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                first.Focus();
                return;
            }

            if (!string.Equals(first.Text, second.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(form, "As senhas não conferem.", caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                second.Focus();
                return;
            }

            form.DialogResult = DialogResult.OK;
        };

        form.Shown += (_, _) => first.Focus();
        var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
        return result == DialogResult.OK ? first.Text : null;
    }

    private static Form CreateBaseForm(string caption, int width, int height) => new()
    {
        Text = caption,
        ClientSize = new Size(width, height),
        FormBorderStyle = FormBorderStyle.FixedDialog,
        StartPosition = FormStartPosition.CenterScreen,
        MaximizeBox = false,
        MinimizeBox = false,
        ShowIcon = false,
        ShowInTaskbar = false,
        TopMost = true,
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
        AutoScaleMode = AutoScaleMode.Dpi,
    };

    private static (Panel Panel, Button Ok, Button Cancel) CreateButtonRow()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 100, Height = 30 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.None, Width = 100, Height = 30 };

        panel.Controls.Add(cancel);
        panel.Controls.Add(ok);
        panel.Layout += (_, _) =>
        {
            int y = Math.Max(0, (panel.Height - ok.Height) / 2);
            ok.Location = new Point(Math.Max(0, panel.Width - ok.Width - 110), y);
            cancel.Location = new Point(Math.Max(0, panel.Width - cancel.Width), y);
        };

        return (panel, ok, cancel);
    }
}
