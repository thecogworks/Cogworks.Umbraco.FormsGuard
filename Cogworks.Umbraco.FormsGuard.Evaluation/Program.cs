using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Cogworks.Umbraco.FormsGuard.Evaluation;
using Cogworks.Umbraco.FormsGuard.Settings;

string csvPath = "demo/spam-corpus.csv";
string? providerArg = null;
var prices = new Dictionary<string, TokenPrice>(StringComparer.OrdinalIgnoreCase)
{
    // Jev's published price; override with --price jev=<in>[/<out>].
    ["jev"] = new TokenPrice(0.042, 0),
};
string url = "https://localhost:44326";

for (var i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    try
    {
        switch (args[i])
        {
            case "--csv": csvPath = Next(); break;
            case "--provider": providerArg = Next(); break;
            case "--price":
                var (alias, price) = TokenPrice.Parse(Next());
                prices[alias] = price;
                break;
            case "--url": url = Next(); break;
            default:
                Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                return Usage();
        }
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return Usage();
    }
}

var providers = (providerArg ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
if (providers.Count == 0)
{
    Console.Error.WriteLine("--provider is required.");
    return Usage();
}

if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
{
    Console.Error.WriteLine($"'{url}' is not an absolute URL.");
    return 2;
}

IReadOnlyList<CorpusRow> rows;
try
{
    rows = CsvReader.ReadCorpus(await File.ReadAllTextAsync(csvPath));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"Could not read '{csvPath}': {ex.Message}");
    return 1;
}

using var handler = new HttpClientHandler
{
    // Accept the ASP.NET Core dev certificate, but only for loopback hosts.
    ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
        errors == System.Net.Security.SslPolicyErrors.None || (request.RequestUri?.IsLoopback ?? false),
};
using var http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(2) };
var endpoint = new Uri(baseUri, "/api/forms-guard/evaluate");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var runs = new List<ProviderRun>();
foreach (var provider in providers)
{
    var results = new List<RowResult>();
    foreach (var row in rows)
    {
        var label = providers.Count > 1 ? $"{provider} " : string.Empty;
        Console.Error.Write($"\rScoring {label}{results.Count + 1}/{rows.Count}...");
        try
        {
            using var response = await http.PostAsJsonAsync(
                endpoint, new { provider, name = row.Name, email = row.Email, message = row.Message }, json);
            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode == HttpStatusCode.NotFound ? " (is the site running in Development?)" : string.Empty;
                results.Add(new RowResult(row, false, $"HTTP {(int)response.StatusCode}{hint}", null, null));
                continue;
            }

            var body = await response.Content.ReadFromJsonAsync<EvaluateResponse>(json);
            results.Add(body is null
                ? new RowResult(row, false, "Empty response.", null, null)
                : new RowResult(
                    row, body.Success, body.Error, body.SpamProbability, body.GenuineProbability, body.ElapsedMs,
                    body.InputTokens, body.OutputTokens));
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException || ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Could not connect to {endpoint}. Is the test site running? ({ex.Message})");
            return 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        {
            results.Add(new RowResult(row, false, $"{ex.GetType().Name}: {ex.Message}", null, null));
        }
    }

    Console.Error.WriteLine();
    runs.Add(new ProviderRun(provider, results, prices.GetValueOrDefault(provider)));
}

for (var i = 0; i < runs.Count; i++)
{
    if (i > 0)
    {
        Console.WriteLine();
    }

    Console.WriteLine(EvaluationReport.Render(runs[i].Alias, runs[i].Results, DefaultFormSettings.Thresholds));
}

if (runs.Count > 1)
{
    Console.WriteLine();
    Console.WriteLine(ComparisonReport.Render(runs, DefaultFormSettings.Thresholds));
}

return 0;

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet run --project Cogworks.Umbraco.FormsGuard.Evaluation -- --provider <alias>[,<alias>...] [--price <alias>=<input usd per million>[/<output usd per million>]]... [--csv demo/spam-corpus.csv] [--url https://localhost:44326]");
    return 2;
}

internal sealed record EvaluateResponse(
    bool Success,
    string? Error,
    string? Band,
    double? SpamProbability,
    double? GenuineProbability,
    long ElapsedMs,
    long? InputTokens,
    long? OutputTokens);
