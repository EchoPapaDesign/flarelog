using System.Collections.Concurrent;
using System.Reflection;

namespace AeroflyAcars.Runtime;

/// <summary>
/// Sprachausgabe über die in Windows eingebaute Sprach-API (SAPI, COM "SAPI.SpVoice").
/// Kein NuGet nötig. Bevorzugt eine englische Stimme, damit "Touchdown ..." richtig klingt.
/// Läuft auf einem eigenen Thread, damit der UDP-Empfang nie blockiert.
/// </summary>
public sealed class SpeechAnnouncer : IDisposable
{
    private const int SpeakAsyncFlag = 0;      // synchron auf dem eigenen Thread
    private const int SpeakPurgeBefore = 2;    // laufende Ansage abbrechen

    private readonly BlockingCollection<string> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;

    private SpeechAnnouncer() { }

    public bool Available { get; private set; }
    public string? VoiceName { get; private set; }
    public string? Problem { get; private set; }

    public static SpeechAnnouncer Create()
    {
        var announcer = new SpeechAnnouncer();
        if (!OperatingSystem.IsWindows())
        {
            announcer.Problem = "Sprachausgabe gibt es nur unter Windows.";
            return announcer;
        }

        announcer.Available = true;
        announcer._thread = new Thread(announcer.Run) { IsBackground = true, Name = "Sprachausgabe" };
        announcer._thread.SetApartmentState(ApartmentState.STA);
        announcer._thread.Start();
        announcer._ready.Wait(TimeSpan.FromSeconds(5));
        return announcer;
    }

    public void Say(string text)
    {
        if (Available && !_queue.IsAddingCompleted) _queue.Add(text);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void Run()
    {
        object? voice = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice")
                       ?? throw new InvalidOperationException("Windows-Sprachausgabe (SAPI) nicht gefunden.");
            voice = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException("Windows-Sprachausgabe konnte nicht gestartet werden.");
            SelectEnglishVoice(voice);
        }
        catch (Exception ex)
        {
            Available = false;
            Problem = ex.Message;
        }
        finally
        {
            _ready.Set();
        }

        if (voice is null) return;

        foreach (var text in _queue.GetConsumingEnumerable())
        {
            try
            {
                Invoke(voice, "Speak", text, SpeakAsyncFlag | SpeakPurgeBefore);
            }
            catch (Exception)
            {
                // Eine fehlgeschlagene Ansage soll das Programm nicht stören.
            }
        }
    }

    private void SelectEnglishVoice(object voice)
    {
        // Sprach-IDs: 409 = Englisch (USA), 809 = Englisch (GB)
        foreach (var lang in new[] { "Language=409", "Language=809" })
        {
            try
            {
                var tokens = Invoke(voice, "GetVoices", lang, "");
                if (tokens is null) continue;
                var count = Convert.ToInt32(Get(tokens, "Count"));
                if (count == 0) continue;
                var token = Invoke(tokens, "Item", 0);
                if (token is null) continue;
                Set(voice, "Voice", token);
                VoiceName = Invoke(token, "GetDescription", 0) as string;
                return;
            }
            catch (Exception)
            {
                // nächste Sprache probieren
            }
        }

        try
        {
            var current = Get(voice, "Voice");
            var name = current is null ? null : Invoke(current, "GetDescription", 0) as string;
            VoiceName = name is null ? "Standardstimme" : $"{name} (keine englische Stimme installiert)";
        }
        catch (Exception)
        {
            VoiceName = "Standardstimme";
        }
    }

    private static object? Invoke(object target, string method, params object[] args) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string property) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string property, object value) =>
        target.GetType().InvokeMember(property, BindingFlags.PutRefDispProperty, null, target, new[] { value });

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread?.Join(TimeSpan.FromSeconds(3));
    }
}
