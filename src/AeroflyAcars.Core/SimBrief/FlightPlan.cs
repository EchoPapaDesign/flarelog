namespace AeroflyAcars.Core.SimBrief;

/// <param name="SidStar">Punkt gehört zu SID oder STAR (SimBrief "is_sid_star").</param>
public sealed record Waypoint(string Ident, double Latitude, double Longitude, double? AltitudeFt, bool SidStar = false);

/// <summary>Die für ACARS relevanten Teile eines SimBrief-OFP.</summary>
public sealed record FlightPlan
{
    public string? Callsign { get; init; }
    public string? Airline { get; init; }
    public string? FlightNumber { get; init; }
    public string? AircraftType { get; init; }
    /// <summary>Kennzeichen aus SimBrief, z. B. "D-AXVA".</summary>
    public string? AircraftRegistration { get; init; }
    public string? Airac { get; init; }

    public string OriginIcao { get; init; } = "";
    public string? OriginName { get; init; }
    public double? OriginLatitude { get; init; }
    public double? OriginLongitude { get; init; }
    public double? OriginElevationFt { get; init; }
    /// <summary>METAR des Abflugplatzes beim Erzeugen des OFP.</summary>
    public string? OriginMetar { get; init; }
    /// <summary>Geplante Startbahn, z. B. "35L".</summary>
    public string? OriginRunway { get; init; }

    public string DestinationIcao { get; init; } = "";
    public string? DestinationName { get; init; }
    public double? DestinationLatitude { get; init; }
    public double? DestinationLongitude { get; init; }
    public double? DestinationElevationFt { get; init; }
    /// <summary>Geplante Landebahn.</summary>
    public string? DestinationRunway { get; init; }

    /// <summary>SID und STAR samt Transition (z. B. "RTT2J"), falls geplant.</summary>
    public string? Sid { get; init; }
    public string? SidTransition { get; init; }
    public string? Star { get; init; }
    public string? StarTransition { get; init; }

    public double? CruiseAltitudeFt { get; init; }
    public double? RouteDistanceNm { get; init; }
    public string? Route { get; init; }
    public TimeSpan? PlannedBlockTime { get; init; }
    public TimeSpan? PlannedAirTime { get; init; }

    public IReadOnlyList<Waypoint> Waypoints { get; init; } = Array.Empty<Waypoint>();

    /// <summary>Wegpunkte der Strecke ohne Flughäfen, TOC/TOD und ohne SID-/STAR-Punkte.</summary>
    public IEnumerable<Waypoint> EnrouteWaypoints
    {
        get
        {
            var inner = Waypoints.Where(w => !w.Ident.Equals(OriginIcao, StringComparison.OrdinalIgnoreCase) &&
                                             !w.Ident.Equals(DestinationIcao, StringComparison.OrdinalIgnoreCase) &&
                                             w.Ident is not ("TOC" or "TOD")).ToList();
            return inner.Where(w => !w.SidStar);
        }
    }

    public string DisplayName =>
        Callsign ?? (Airline is not null && FlightNumber is not null ? Airline + FlightNumber : Lang.T("Flug", "Flight"));

    public string Summary =>
        $"{DisplayName} {OriginIcao} → {DestinationIcao}" +
        (AircraftType is null ? "" : $", {AircraftType}") +
        $", {Waypoints.Count} " + Lang.T("Wegpunkte", "waypoints") +
        (Airac is null ? "" : $", AIRAC {Airac}");
}
