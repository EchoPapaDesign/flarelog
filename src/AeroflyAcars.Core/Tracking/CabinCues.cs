namespace AeroflyAcars.Core.Tracking;

/// <summary>
/// Entscheidet, wann welche Kabinenansage kommt. Die Dateinamen sind die des Flarelog-Soundpacks von AeRodri
/// und dürfen nicht geändert werden (spätere Erweiterungspakete bauen darauf auf).
/// Jede Ansage kommt höchstens einmal pro Flug; bei Platzrunden oder Touch-and-go nicht erneut.
/// </summary>
public sealed class CabinCues
{
    public const string WelcomeOnboard = "Welcome_Onboard.wav";
    public const string PushbackStart = "Pushback_Start.wav";
    public const string SafetyBriefing = "Safety_Briefing.wav";
    public const string ClimbingMessage = "Climbing_Message.wav";
    public const string CruiseAltitude = "Cruise_Altitude.wav";
    public const string DescendingStart = "Descending_Start.wav";
    public const string AfterTouchdown = "After_Touchdown.wav";

    public static readonly IReadOnlyList<string> All =
        [WelcomeOnboard, PushbackStart, SafetyBriefing, ClimbingMessage, CruiseAltitude, DescendingStart, AfterTouchdown];

    /// <summary>Bis zu dieser Groundspeed gilt das Flugzeug als stehend (Begrüßung am Gate).</summary>
    public const double ParkedKt = 1;

    private readonly HashSet<string> _played = new();
    private bool _tookOff;

    /// <summary>Erster Messpunkt eines Flugs: steht das Flugzeug, kommt die Begrüßung.</summary>
    public IReadOnlyList<string> OnSample(FlightSample sample, FlightPhase phase)
    {
        if (phase == FlightPhase.Preflight && sample.GroundSpeedKt <= ParkedKt && _played.Count == 0)
            return Play(WelcomeOnboard);
        return [];
    }

    public IReadOnlyList<string> OnEvent(FlightEvent e)
    {
        if (!e.PhaseChange) return [];
        switch (e.Phase)
        {
            case FlightPhase.TaxiOut:
                return Play(PushbackStart, SafetyBriefing);
            case FlightPhase.TakeoffRoll:
                _tookOff = true;
                return [];
            case FlightPhase.Climb when _tookOff:
                return Play(ClimbingMessage);
            case FlightPhase.Cruise when _tookOff:
                return Play(CruiseAltitude);
            // Kurze Flüge gehen manchmal direkt in den Anflug – dann kommt die Sinkflug-Ansage dort.
            case FlightPhase.Descent when _tookOff:
            case FlightPhase.Approach when _tookOff:
                return Play(DescendingStart);
            case FlightPhase.TaxiIn:
                return Play(AfterTouchdown);
            default:
                return [];
        }
    }

    private IReadOnlyList<string> Play(params string[] cues)
    {
        var result = new List<string>();
        foreach (var cue in cues)
            if (_played.Add(cue)) result.Add(cue);
        return result;
    }
}
