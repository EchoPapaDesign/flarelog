namespace AeroflyAcars.Core.Tracking;

/// <summary>
/// Glättet die Spur am Boden für die Kartenanzeige. Aerofly sendet die Position nur auf 4 Nachkommastellen
/// (etwa 11 m Nord-Süd, 8 m Ost-West in Mitteleuropa) – beim Rollen mit 15 kt springt der Punkt deshalb von Raster
/// zu Raster, und die Linie wird zur Treppe. In der Luft fällt das bei den großen Abständen nicht auf.
/// Geglättet wird nur die Anzeige; Bericht und PIREP bekommen die gemessenen Punkte.
/// </summary>
public static class TrackSmoothing
{
    /// <summary>Darunter gilt ein Punkt als „am Boden“ (Rollen, Ausrollen).</summary>
    public const double GroundKt = 50;

    /// <summary>Halbe Fensterbreite des gleitenden Mittels.</summary>
    public static readonly TimeSpan HalfWindow = TimeSpan.FromSeconds(3);

    public static List<(double Lat, double Lon)> ForDisplay(IReadOnlyList<FlightSample> samples)
    {
        var result = new List<(double, double)>(samples.Count);
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            if (s.GroundSpeedKt >= GroundKt)
            {
                result.Add((s.Latitude, s.Longitude));
                continue;
            }
            double lat = 0, lon = 0;
            int n = 0;
            for (int j = i; j >= 0 && s.Time - samples[j].Time <= HalfWindow && samples[j].GroundSpeedKt < GroundKt; j--)
            { lat += samples[j].Latitude; lon += samples[j].Longitude; n++; }
            for (int j = i + 1; j < samples.Count && samples[j].Time - s.Time <= HalfWindow && samples[j].GroundSpeedKt < GroundKt; j++)
            { lat += samples[j].Latitude; lon += samples[j].Longitude; n++; }
            result.Add((lat / n, lon / n));
        }
        return result;
    }
}
