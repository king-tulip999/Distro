using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Core.LiveryShop;

internal static partial class PaintTemplates
{
    public const string Server = "https://ir-core-sites.iracing.com/members/member_images/";
    private const long MaxZip = 200L * 1024 * 1024, MaxPsd = 600L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();
    private static readonly Lazy<Dictionary<string, string>> CarFiles = new(() => Parse(ReadList()));

    [GeneratedRegex(@"^\d{1,6}_template_[A-Za-z0-9_-]{1,40}\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex ZipName();

    internal static Dictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string[] parts = raw.TrimEnd('\r').Split('\t');
            if (parts.Length != 2) continue;
            string car = parts[0].Trim(), file = parts[1].Trim();
            if (car.Length == 0 || !IracingPaint.IsCarDir(car) || !ZipName().IsMatch(file)) continue;
            map.TryAdd(car, file);
        }
        return map;
    }

    private static string ReadList()
    {
        using var stream = typeof(PaintTemplates).Assembly.GetManifestResourceStream("livery-car-templates.txt");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string? Url(string kind, string? car) => kind switch
    {
        "helmet" => Server + "misctemplates/helmet.zip",
        "suit" => Server + "misctemplates/driver_suit.zip",
        _ => car is not null && CarFiles.Value.TryGetValue(car.Trim(), out var file) ? Server + "cars/car_templates/" + file : null,
    };

    public static async Task<string> GetAsync(string url, string folder)
    {
        string key = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
        string dir = Path.Combine(folder, key);
        string? cached = Cached(dir);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cached is not null) request.Headers.IfModifiedSince = File.GetLastWriteTimeUtc(cached);
        try
        {
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (cached is not null && response.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return cached;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                throw new InvalidOperationException("iRacing doesn't have this template anymore. Download it in iRacing: My Content → Car Manager → pick the car → Download Template.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxZip) throw new InvalidOperationException("iRacing's template file is too big.");
            Directory.CreateDirectory(folder);
            string zip = Path.Combine(folder, key + ".zip.tmp");
            try
            {
                await using (var file = File.Create(zip))
                {
                    await using var body = await response.Content.ReadAsStreamAsync();
                    await body.CopyToAsync(file);
                }
                try { return Unpack(zip, dir, response.Content.Headers.LastModified?.UtcDateTime ?? DateTime.UtcNow); }
                catch (InvalidDataException ex) { throw new InvalidOperationException("iRacing's template download was damaged. Try again.", ex); }
            }
            finally { TryDelete(zip); }
        }
        catch (Exception ex) when (cached is not null && ex is HttpRequestException or TaskCanceledException)
        {
            return cached;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Couldn't reach iRacing to download the template. Check your internet connection and try again.", ex);
        }
    }

    private static string? Cached(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.psd").FirstOrDefault() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string Unpack(string zip, string dir, DateTime modified)
    {
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.Entries
            .Where(e => e.Name.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) && e.Length is > 0 and < MaxPsd)
            .MaxBy(e => e.Length)
            ?? throw new InvalidOperationException("iRacing's template download has no .psd file in it.");
        var invalid = Path.GetInvalidFileNameChars();
        string name = new string(entry.Name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        if (name.Length <= 4) name = "Template.psd";
        string fresh = dir + ".new";
        if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
        Directory.CreateDirectory(fresh);
        string target = Path.Combine(fresh, name);
        entry.ExtractToFile(target);
        File.SetLastWriteTimeUtc(target, modified);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(fresh, dir);
        return Path.Combine(dir, name);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.Name + "/" + Services.UpdateService.Current);
        return client;
    }
}
