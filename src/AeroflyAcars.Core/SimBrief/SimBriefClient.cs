namespace AeroflyAcars.Core.SimBrief;

/// <summary>Holt den zuletzt erzeugten OFP eines SimBrief-Nutzers.</summary>
public sealed class SimBriefClient
{
    private readonly HttpClient _http;

    public SimBriefClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
    }

    /// <param name="user">SimBrief-Benutzername oder die numerische Pilot-ID.</param>
    public async Task<FlightPlan> FetchLatestAsync(string user, CancellationToken cancellationToken = default)
    {
        user = user.Trim();
        if (user.Length == 0) throw new SimBriefException(Lang.T("Kein SimBrief-Benutzer angegeben.", "No SimBrief user entered."));

        string key = user.All(char.IsDigit) ? "userid" : "username";
        var url = $"https://www.simbrief.com/api/xml.fetcher.php?{key}={Uri.EscapeDataString(user)}&json=1";

        string body;
        int status;
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SimBriefException(Lang.T($"SimBrief ist nicht erreichbar: {ex.Message}", $"SimBrief is not reachable: {ex.Message}"), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SimBriefException(Lang.T("SimBrief hat nicht rechtzeitig geantwortet.", "SimBrief did not respond in time."), ex);
        }

        // Auch bei Fehlern (HTTP 400) steht der Grund als JSON im Body – der Parser wertet ihn aus.
        try
        {
            return SimBriefParser.Parse(body);
        }
        catch (SimBriefException) when (status >= 400 && !body.TrimStart().StartsWith('{'))
        {
            throw new SimBriefException(Lang.T($"SimBrief antwortet mit HTTP {status}.", $"SimBrief responds with HTTP {status}."));
        }
    }
}
