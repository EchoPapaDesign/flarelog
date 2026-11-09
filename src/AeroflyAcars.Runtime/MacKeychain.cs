using System.Diagnostics;

namespace AeroflyAcars.Runtime;

/// <summary>
/// Schlüsselbund von macOS über das mitgelieferte Programm „security“ – dort liegt der API-Schlüssel der Airline
/// so sicher wie unter Windows mit DPAPI. Nur unter macOS verwenden.
/// </summary>
internal static class MacKeychain
{
    private const string Service = "Flarelog";
    private const string Account = "airline-api-key";

    public static bool Save(string secret)
    {
        // -U: vorhandenen Eintrag ersetzen
        return Run(out _, "add-generic-password", "-U", "-s", Service, "-a", Account, "-w", secret) == 0;
    }

    public static string? Load()
    {
        return Run(out var output, "find-generic-password", "-s", Service, "-a", Account, "-w") == 0
            ? output.TrimEnd('\n', '\r')
            : null;
    }

    public static void Delete() => Run(out _, "delete-generic-password", "-s", Service, "-a", Account);

    private static int Run(out string output, params string[] args)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return -1;
            output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            return p.WaitForExit(10_000) ? p.ExitCode : -1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return -1;
        }
    }
}
