using System.Text.Json;

namespace AeroflyAcars.Core;

public sealed record UpdateInfo(string Version, string Url);

/// <summary>Fragt bei GitHub nach der neuesten veröffentlichten Version – Vorabversionen (Beta) eingeschlossen.</summary>
public static class UpdateChecker
{
    public static async Task<UpdateInfo?> CheckAsync(string repository, string currentVersion, HttpClient? http = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;
        var client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            // releases/latest liefert keine Vorabversionen – deshalb die Liste der letzten Releases.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=10");
            request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            UpdateInfo? best = null;
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
                var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                var url = r.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                if (tag is null || url is null || !IsNewer(tag, best?.Version ?? currentVersion)) continue;
                best = new UpdateInfo(tag.TrimStart('v', 'V'), url);
            }
            return best;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
        finally
        {
            if (http is null) client.Dispose();
        }
    }

    /// <summary>
    /// Vergleich nach den Regeln der Versionsnummern: "0.9.0" &gt; "0.9.0-beta.2" &gt; "0.9.0-beta.1" &gt; "0.8.0".
    /// </summary>
    public static bool IsNewer(string candidate, string current) =>
        Parse(candidate) is { } c && Parse(current) is { } cur && Compare(c, cur) > 0;

    private static (Version Number, string? Pre)? Parse(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        int plus = v.IndexOf('+');
        if (plus > 0) v = v[..plus];
        string? pre = null;
        int dash = v.IndexOf('-');
        if (dash > 0) { pre = v[(dash + 1)..]; v = v[..dash]; }
        return System.Version.TryParse(v, out var number) ? (number, string.IsNullOrEmpty(pre) ? null : pre) : null;
    }

    private static int Compare((Version Number, string? Pre) a, (Version Number, string? Pre) b)
    {
        int n = a.Number.CompareTo(b.Number);
        if (n != 0) return n;
        if (a.Pre is null || b.Pre is null) return (a.Pre is null ? 1 : 0) - (b.Pre is null ? 1 : 0);
        var pa = a.Pre.Split('.');
        var pb = b.Pre.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            if (i >= pa.Length) return -1;
            if (i >= pb.Length) return 1;
            int c = int.TryParse(pa[i], out var x) && int.TryParse(pb[i], out var y)
                ? x.CompareTo(y)
                : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return c;
        }
        return 0;
    }
}
