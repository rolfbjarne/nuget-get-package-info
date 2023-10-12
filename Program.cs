using System.Text.Json;

using Mono.Options;

public static class Program {
    static async Task<int> Main (string[] args)
    {
        var version = "";
        var packageid = "";
        var feed = "";

        var options = new OptionSet
        {
            { "h", v => Console.WriteLine ("Help") },
            { "feed=", v => feed = v },
            { "packageid=", v => packageid = v },
            { "version=", v => version = v },
        };
        var remaining = options.Parse(args);
        if (remaining.Any ()) {
            Console.WriteLine($"Unexpected command line arguments:\n\t{string.Join("\n\t", remaining)}");
            return 1;
        }

        if (string.IsNullOrEmpty (feed)) {
            Console.WriteLine($"Feed required");
            return 1;
        }
        
        if (string.IsNullOrEmpty (packageid)) {
            Console.WriteLine($"Package ID required");
            return 1;
        }

        if (string.IsNullOrEmpty (version)) {
            Console.WriteLine($"Version required");
            return 1;
        }

        var client = new HttpClient();
        var serviceIndex = await GetDocAsync(feed);
        string? registrationBaseUrl = null;
        foreach (var resource in serviceIndex.RootElement.GetProperty ("resources").EnumerateArray ()) {
            var type = resource.GetProperty("@type").GetString();
            if (type == "RegistrationsBaseUrl/3.6.0") {
                registrationBaseUrl = resource.GetProperty("@id").GetString();
                break;
            }
        }
        Console.WriteLine($"RegistrationsBaseUrl: {registrationBaseUrl}");

        var registry = await GetDocAsync(registrationBaseUrl + packageid + "/index.json");
        var suffix = "/" + packageid.ToLowerInvariant () + "/" + version + ".json";
        foreach (var firstLevelItem in registry.RootElement.GetProperty ("items").EnumerateArray ()) {
            foreach (var secondLevelItem in firstLevelItem.GetProperty("items").EnumerateArray ())
            {
                var id = secondLevelItem.GetProperty("@id").GetString();
                if (!id.EndsWith (suffix, StringComparison.Ordinal))
                    continue;
                Console.WriteLine($"Found package {packageid} with version {version}");
                var packageVersion = secondLevelItem.GetProperty("catalogEntry").GetProperty("version").GetString();
                var hashStart = packageVersion.LastIndexOf('+');
                var hash = packageVersion.Substring(hashStart + 1);
                Console.WriteLine($"Hash: {hash}");
                return 0;
            }
        }

        Console.WriteLine("Package+version not found");
        return 1;
    }

    static async Task<JsonDocument> GetDocAsync(string url)
    {
        var client = new HttpClient();
        var json = await client.GetStringAsync(url);
        return JsonDocument.Parse (json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

}

