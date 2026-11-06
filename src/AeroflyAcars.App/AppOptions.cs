namespace AeroflyAcars.App;

internal sealed class AppOptions
{
    public string? SimBriefUser { get; private set; }
    public int? UdpPort { get; private set; }
    public int? MapPort { get; private set; }
    public bool OpenBrowser { get; private set; } = true;
    public bool AppWindow { get; private set; } = true;
    public bool Voice { get; private set; } = true;
    public bool VoiceTest { get; private set; }
    public double? DestinationElevationFt { get; private set; }
    public string? OutputDirectory { get; private set; }

    public const string Usage = """
        Flarelog – Konsolenversion

        Aufruf:  dotnet run --project src\AeroflyAcars.App -- [Optionen]
        Ohne Optionen gelten die Einstellungen aus dem Programm (%APPDATA%\Flarelog\settings.json).

          --simbrief <name|id>   Letzten SimBrief-Flugplan laden (Benutzername oder Pilot-ID)
          --port <n>             UDP-Port für Aerofly (Standard 49002)
          --map-port <n>         Port des Displays (Standard 8080, 0 = aus)
          --no-browser           Display nicht automatisch öffnen
          --browser              Display im normalen Browser statt als eigenes Fenster
          --no-voice             Keine Sprachansage nach dem Aufsetzen
          --voice-test           Sprachansage einmal testen und beenden
          --dest-elev <ft>       Elevation des Zielflughafens (normalerweise nicht nötig,
                                 sie kommt aus SimBrief oder der Flughafenliste)
          --out <ordner>         Ordner für die Flugberichte (Standard: Dokumente\Flarelog\Fluege)
          --help                 Diese Hilfe

        Beispiel:  dotnet run --project src\AeroflyAcars.App -- --simbrief meinname
        """;

    /// <summary>Gibt null zurück, wenn die Hilfe angezeigt werden soll; wirft bei ungültigen Werten.</summary>
    public static AppOptions? Parse(string[] args)
    {
        var o = new AppOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--simbrief":
                    o.SimBriefUser = Next(args, ref i, arg);
                    break;
                case "--port":
                    o.UdpPort = Port(Next(args, ref i, arg), arg, allowZero: false);
                    break;
                case "--map-port":
                    o.MapPort = Port(Next(args, ref i, arg), arg, allowZero: true);
                    break;
                case "--no-browser":
                    o.OpenBrowser = false;
                    break;
                case "--browser":
                    o.AppWindow = false;
                    break;
                case "--no-voice":
                    o.Voice = false;
                    break;
                case "--voice-test":
                    o.VoiceTest = true;
                    break;
                case "--dest-elev":
                    var v = Next(args, ref i, arg);
                    if (!double.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var elev))
                        throw new ArgumentException($"Ungültige Elevation: {v}");
                    o.DestinationElevationFt = elev;
                    break;
                case "--out":
                    o.OutputDirectory = Path.GetFullPath(Next(args, ref i, arg));
                    break;
                case "--help":
                case "-h":
                case "/?":
                    return null;
                default:
                    throw new ArgumentException($"Unbekannte Option: {arg}");
            }
        }
        return o;
    }

    private static string Next(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Für {name} fehlt ein Wert.");
        return args[++i];
    }

    private static int Port(string value, string name, bool allowZero)
    {
        if (!int.TryParse(value, out int port) || port < (allowZero ? 0 : 1) || port > 65535)
            throw new ArgumentException($"Ungültiger Port für {name}: {value}");
        return port;
    }
}
