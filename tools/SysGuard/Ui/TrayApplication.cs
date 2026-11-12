using System.Drawing;
using System.Windows.Forms;

namespace SysGuard;

/// <summary>
/// Processo da bandeja: ícone na área de notificação (por padrão, nos ícones ocultos), menu mínimo,
/// proteção da janela ativa e comunicação com o serviço. Não tem janela principal.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _changeItem;
    private readonly ToolStripMenuItem _uninstallItem;
    private readonly ToolStripMenuItem _repairItem;
    private readonly WindowGuard _windowGuard;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private int _refreshing;
    private bool _available;
    private bool _protected = true;
    private bool _degraded;
    private bool _degradedNotified;
    private bool _vpn;
    private bool _vpnNotified;

    public TrayApplication()
    {
        // Migração da 1.1: limpa o modo restrito do YouTube que a 1.0 gravava, inclusive no HKCU do
        // usuário (o serviço SYSTEM já faz HKLM/HKU; aqui garante o perfil da bandeja).
        LegacyPolicies.RemoveAll();

        _statusItem = new ToolStripMenuItem("Verificando...") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Desativar proteção...");
        _changeItem = new ToolStripMenuItem("Alterar senha...");
        _uninstallItem = new ToolStripMenuItem("Desinstalar...");
        _repairItem = new ToolStripMenuItem("Reparar instalação (administrador)...") { Visible = false };

        _toggleItem.Click += (_, _) => ToggleProtection();
        _changeItem.Click += (_, _) => ChangePassword();
        _uninstallItem.Click += (_, _) => Uninstall();
        _repairItem.Click += (_, _) => Repair();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(_changeItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_uninstallItem);
        _menu.Items.Add(_repairItem);
        _menu.Opening += (_, _) => RefreshStatusAsync();

        _icon = TrayIconFactory.Create();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = AppInfo.AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _menu.Show(Cursor.Position);
        };
        _notifyIcon.DoubleClick += (_, _) => _menu.Show(Cursor.Position);

        _windowGuard = new WindowGuard(() => _available && _protected);
        _windowGuard.Start();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => RefreshStatusAsync();
        _refreshTimer.Start();

        RefreshStatusAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _windowGuard.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void RefreshStatusAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            string response = await Task.Run(() => PipeClient.Send("STATUS", 2500));
            ApplyStatus(response);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao consultar o serviço.", ex);
            ApplyStatus("ERR UNAVAILABLE");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void ApplyStatus(string response)
    {
        bool vpn = response.Contains(" VPN", StringComparison.Ordinal);
        if (response.StartsWith("OK PROTECTED DEGRADED", StringComparison.Ordinal))
        {
            _available = true;
            _protected = true;
            _degraded = true;
            _vpn = vpn;
        }
        else if (response.StartsWith("OK PROTECTED", StringComparison.Ordinal))
        {
            _available = true;
            _protected = true;
            _degraded = false;
            _degradedNotified = false;
            _vpn = vpn;
        }
        else if (response.StartsWith("OK UNPROTECTED", StringComparison.Ordinal))
        {
            _available = true;
            _protected = false;
            _degraded = false;
            _degradedNotified = false;
            _vpn = false;
        }
        else
        {
            _available = false;
            _vpn = false;
        }

        if (_degraded && !_degradedNotified)
        {
            _degradedNotified = true;
            Notify("Proteção ativa, mas uma camada não pôde ser aplicada. Abra o menu e verifique.");
        }

        if (_vpn && !_vpnNotified)
        {
            _vpnNotified = true;
            Notify("VPN/túnel ativo neste computador: o filtro de DNS pode não valer para o tráfego dela.");
        }
        else if (!_vpn)
        {
            _vpnNotified = false;
        }

        UpdateMenu();
    }

    private void UpdateMenu()
    {
        if (!_available)
        {
            _statusItem.Text = "Serviço indisponível";
            _toggleItem.Visible = false;
            _changeItem.Visible = false;
            _uninstallItem.Visible = false;
            _repairItem.Visible = true;
            return;
        }

        _statusItem.Text = _protected
            ? (_degraded
                ? "Proteção ativa (parcial)"
                : _vpn ? "Proteção ativa — VPN ativa (DNS pode escapar)" : "Proteção ativa")
            : "Proteção desativada";
        _toggleItem.Text = _protected ? "Desativar proteção..." : "Ativar proteção";
        _toggleItem.Visible = true;
        _changeItem.Visible = true;
        _uninstallItem.Visible = true;
        _repairItem.Visible = false;
    }

    private void ToggleProtection()
    {
        if (!_available) return;

        if (_protected)
        {
            var password = PasswordDialog.AskPassword(
                null, AppInfo.AppName, "Digite a senha para desativar a proteção:");
            if (password is null) return;

            string response = PipeClient.SendEncoded("UNLOCK", 8000, password);
            if (HandleResponse(
                    response,
                    "Proteção desativada.",
                    "Proteção desativada, mas parte das configurações não pôde ser restaurada. Reinicie o computador."))
                ApplyStatus("OK UNPROTECTED");
        }
        else
        {
            string response = PipeClient.Send("LOCK");
            if (HandleResponse(response, "Proteção ativada."))
            {
                ApplyStatus("OK PROTECTED");
                if (response == "OK PARTIAL")
                    Notify("Proteção reativada parcialmente; o serviço tentará de novo.");
            }
        }

        RefreshStatusAsync();
    }

    private void ChangePassword()
    {
        if (!_available) return;

        var current = PasswordDialog.AskPassword(null, AppInfo.AppName, "Digite a senha atual:");
        if (current is null) return;

        var next = PasswordDialog.AskNewPassword(null, AppInfo.AppName, "Crie a nova senha:");
        if (next is null) return;

        string response = PipeClient.SendEncoded("CHANGE", 8000, current, next);
        if (response == "OK")
        {
            Notify("Senha alterada.");
            return;
        }

        HandleResponse(response, null);
    }

    private void Uninstall()
    {
        if (!_available) return;

        var confirm = MessageBox.Show(
            "Remover a proteção e desinstalar o SysGuard deste computador?",
            AppInfo.AppName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        var password = PasswordDialog.AskPassword(
            null, AppInfo.AppName, "Digite a senha para desinstalar:");
        if (password is null) return;

        string response = PipeClient.SendEncoded("UNINSTALL", 8000, password);
        if (response != "OK")
        {
            HandleResponse(response, null);
            return;
        }

        _notifyIcon.Visible = false;
        MessageBox.Show(
            "Proteção removida. O serviço será encerrado e a pasta de instalação será apagada. " +
            "O computador pode ser reiniciado para concluir.",
            AppInfo.AppName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        ExitThread();
    }

    private void Repair()
    {
        int code = InstallFlow.RunSetupElevated(repair: true);
        if (code == 0)
        {
            RefreshStatusAsync();
            return;
        }

        if (code != InstallFlow.Canceled)
            MessageBox.Show("Não foi possível reparar a instalação.", AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Trata "OK"/"OK PARTIAL"/"ERR ..." e mostra a mensagem adequada.</summary>
    private bool HandleResponse(string response, string? successMessage, string? partialMessage = null)
    {
        if (response is "OK" or "OK PARTIAL")
        {
            if (response == "OK PARTIAL")
                Notify(partialMessage ?? "Operação concluída parcialmente.");
            else if (successMessage is not null)
                Notify(successMessage);
            return true;
        }

        if (response.StartsWith("ERR AUTH", StringComparison.Ordinal))
        {
            int remaining = ParseTrailingInt(response);
            Notify(remaining > 0
                ? $"Senha incorreta. Tentativas restantes: {remaining}."
                : "Senha incorreta. Aguarde para tentar de novo.");
            return false;
        }

        if (response.StartsWith("ERR LOCKOUT", StringComparison.Ordinal))
        {
            int seconds = Math.Max(1, ParseTrailingInt(response));
            Notify($"Muitas tentativas. Aguarde {seconds} segundos.");
            return false;
        }

        if (response.StartsWith("ERR WEAK", StringComparison.Ordinal))
        {
            Notify("A nova senha precisa ter pelo menos 6 caracteres.");
            return false;
        }

        if (response.StartsWith("ERR UNAVAILABLE", StringComparison.Ordinal))
        {
            _available = false;
            UpdateMenu();
            Notify("Serviço de proteção indisponível.");
            return false;
        }

        if (response.StartsWith("ERR RESTORE", StringComparison.Ordinal))
        {
            Notify("Não foi possível restaurar o sistema; a proteção foi mantida.");
            RefreshStatusAsync();
            return false;
        }

        if (response.StartsWith("ERR CLEANUP", StringComparison.Ordinal))
        {
            Notify("Sistema restaurado, mas a limpeza não pôde ser agendada. Reinicie e tente desinstalar de novo.");
            return false;
        }

        if (response.StartsWith("ERR TIMEOUT", StringComparison.Ordinal))
        {
            Notify("O serviço demorou para responder. Verifique o status no menu.");
            RefreshStatusAsync();
            return false;
        }

        Notify("Não foi possível concluir a operação.");
        return false;
    }

    private void Notify(string message)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = AppInfo.AppName;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao mostrar notificação.", ex);
        }
    }

    private static int ParseTrailingInt(string response)
    {
        var space = response.LastIndexOf(' ');
        return space >= 0 && int.TryParse(response[(space + 1)..], out int value) ? value : 0;
    }
}
