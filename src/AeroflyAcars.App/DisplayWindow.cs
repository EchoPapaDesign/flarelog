using System.Diagnostics;

namespace AeroflyAcars.App;

/// <summary>
/// Öffnet das Display. Bevorzugt Microsoft Edge im App-Modus: ein eigenes Fenster ohne Adressleiste und Tabs.
/// Edge ist auf jedem Windows 10/11 vorhanden. Fallback: Standardbrowser.
/// </summary>
internal static class DisplayWindow
{
    public static void Open(string url, bool appWindow)
    {
        if (appWindow && OperatingSystem.IsWindows() && TryStart("msedge", $"--app={url} --window-size=1500,920"))
            return;

        if (!TryStart(url, null))
            Console.WriteLine($"Browser konnte nicht geöffnet werden – bitte {url} selbst öffnen.");
    }

    private static bool TryStart(string fileName, string? arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName) { UseShellExecute = true };
            if (arguments is not null) psi.Arguments = arguments;
            Process.Start(psi);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
