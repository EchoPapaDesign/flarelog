using System.Diagnostics;
using System.Net.Sockets;
using AeroflyAcars.Runtime;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AeroflyAcars.Desktop;

/// <summary>Hauptfenster: startet die ACARS-Logik und zeigt das Display in WebView2 an.</summary>
internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(12, 14, 17);

    private readonly AcarsEngine _engine;
    private readonly WebView2 _web;
    private readonly Label _status;

    public MainForm()
    {
        Text = "Flarelog – ACARS für Aerofly FS 4";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1500, 920);
        MinimumSize = new Size(1000, 640);
        BackColor = Background;
        try
        {
            if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch (Exception)
        {
            // Standardsymbol behalten
        }

        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(139, 147, 160),
            BackColor = Background,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11f),
            Text = "Flarelog wird gestartet …",
        };
        _web = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Background,
            Visible = false,
        };
        Controls.Add(_web);
        Controls.Add(_status);

        _engine = new AcarsEngine(AcarsSettings.Load());

        Shown += async (_, _) => await StartAsync();
        FormClosing += OnFormClosing;
    }

    private async Task StartAsync()
    {
        try
        {
            _engine.Start();
        }
        catch (SocketException ex)
        {
            MessageBox.Show(this,
                $"Der UDP-Port {_engine.Settings.UdpPort} ist belegt ({ex.Message}).\n\n" +
                "Läuft Flarelog schon, zum Beispiel noch die Konsolenversion?",
                "Flarelog", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var url = _engine.DisplayUrl;
        if (url is null)
        {
            _status.Text = "Das Display konnte nicht gestartet werden (Ports 8080–8089 belegt).";
            return;
        }

        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flarelog", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            _status.Text = "Die Anzeige-Komponente „Microsoft Edge WebView2“ konnte nicht gestartet werden.\n" +
                           $"({ex.Message})\n\nDas Display wird stattdessen im Browser geöffnet: {url}";
            OpenExternal(url);
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        // Links (z. B. OpenStreetMap im Kartenfuß) im normalen Browser öffnen, nicht im Programmfenster.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(url, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };

        _web.Source = new Uri(url);
        _status.Visible = false;
        _web.Visible = true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_engine.FlightInProgress)
        {
            var answer = MessageBox.Show(this,
                "Es wird gerade ein Flug aufgezeichnet.\n\nProgramm beenden und den bisherigen Flug als Teilbericht speichern?",
                "Flarelog", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _engine.Dispose();
    }

    private static void OpenExternal(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // nichts zu tun
        }
    }
}
