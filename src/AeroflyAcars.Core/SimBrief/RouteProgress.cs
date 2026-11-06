namespace AeroflyAcars.Core.SimBrief;

/// <param name="Next">Nächster Wegpunkt.</param>
/// <param name="DistanceToNextNm">Direkte Entfernung zum nächsten Wegpunkt.</param>
/// <param name="DistanceToGoNm">Restdistanz entlang der Route bis zum Ziel.</param>
/// <param name="DistanceToTodNm">Restdistanz bis zum Top of Descent (null, wenn keiner im Plan oder schon passiert).</param>
public sealed record RouteProgressInfo(Waypoint Next, double DistanceToNextNm, double DistanceToGoNm, double? DistanceToTodNm);

public static class RouteProgress
{
    /// <summary>
    /// Bestimmt den aktuellen Streckenabschnitt: der Abschnitt, bei dem der Umweg über die Position
    /// (Abstand zu Anfang + Abstand zu Ende − Abschnittslänge) am kleinsten ist.
    /// </summary>
    public static RouteProgressInfo? Compute(IReadOnlyList<Waypoint> route, double latitude, double longitude)
    {
        if (route.Count < 2) return null;

        int bestLeg = 0;
        double bestScore = double.MaxValue;
        for (int i = 0; i < route.Count - 1; i++)
        {
            var a = route[i];
            var b = route[i + 1];
            double leg = Geo.DistanceNm(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            double score = Geo.DistanceNm(latitude, longitude, a.Latitude, a.Longitude)
                         + Geo.DistanceNm(latitude, longitude, b.Latitude, b.Longitude) - leg;
            if (score < bestScore)
            {
                bestScore = score;
                bestLeg = i;
            }
        }

        int next = bestLeg + 1;
        double toNext = Geo.DistanceNm(latitude, longitude, route[next].Latitude, route[next].Longitude);

        double toGo = toNext;
        double? toTod = null;
        if (route[next].Ident.Equals("TOD", StringComparison.OrdinalIgnoreCase)) toTod = toNext;
        for (int i = next; i < route.Count - 1; i++)
        {
            toGo += Geo.DistanceNm(route[i].Latitude, route[i].Longitude, route[i + 1].Latitude, route[i + 1].Longitude);
            if (toTod is null && route[i + 1].Ident.Equals("TOD", StringComparison.OrdinalIgnoreCase))
                toTod = toGo;
        }

        return new RouteProgressInfo(route[next], toNext, toGo, toTod);
    }
}
