namespace AeroflyAcars.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Nur eine Instanz: zwei Programme könnten nicht beide auf dem UDP-Port lauschen.
        using var mutex = new Mutex(true, @"Local\Flarelog.Desktop", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show(AeroflyAcars.Core.Lang.SystemIsGerman() ? "Flarelog läuft bereits." : "Flarelog is already running.", "Flarelog",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
