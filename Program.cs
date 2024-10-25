using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml;

using Mono.Options;

public static class Program {
    static HttpClient? client;
    static HttpClient Client {
        get {
            if (client is null)
                client = new HttpClient ();
            return client;
        }
    }
    static async Task<int> Main (string[] args)
    {
//         args = new string[] {
//     "--feed", "https://pkgs.dev.azure.com/azure-public/vside/_packaging/xamarin-impl/nuget/v3/index.json", "--packageid", "xamarin.messaging.client", "--version", "2.1.15"
// };

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
                var id = secondLevelItem.GetProperty("@id").GetString()!;
                if (!id.EndsWith (suffix, StringComparison.Ordinal))
                    continue;
                Console.WriteLine($"Found package {packageid} with version {version}");
                var packageVersion = secondLevelItem.GetProperty("catalogEntry").GetProperty("version").GetString()!;
                var hashStart = packageVersion.LastIndexOf('+');
                var hash = packageVersion.Substring(hashStart + 1);
                if (hash.Length < 16) {
                    var packageContent = secondLevelItem.GetProperty("packageContent").GetString()!;
                    Console.WriteLine($"Version '{hash}' is not a hash. Downloading package to inspect it (url = {packageContent})");
                    var fn = Path.GetFileName (packageContent);
                    var tmpPath = $"/tmp/{fn}";
                    await DownloadAsync (packageContent, tmpPath);
                    using var zip = ZipFile.OpenRead (tmpPath);
                    foreach (var entry in zip.Entries) {
                        if (entry.Name.EndsWith (".nuspec", StringComparison.Ordinal)) {
                            using var entryStream = entry.Open ();
                            using var streamReader = new StreamReader (entryStream);
                            var entryXml = streamReader.ReadToEnd ();
                            var entryDoc = new XmlDocument();
                            entryDoc.LoadXml(entryXml);
                            var repositoryNode = entryDoc.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='repository']")!;
                            var repositoryHash = repositoryNode.Attributes!["commit"]!.Value;
                            Console.WriteLine($"Hash: {repositoryHash}");
                        }
                    }
                    File.Delete(tmpPath);
                } else {
                    Console.WriteLine($"Hash: {hash}");
                }
                return 0;
            }
        }

        Console.WriteLine("Package+version not found");
        return 1;
    }

    static async Task DownloadAsync (string url, string saveAs)
    {
        var stream = await Client.GetStreamAsync(url);
        using (var fs = new FileStream (saveAs, FileMode.Create, FileAccess.Write, FileShare.None)) {
            await stream.CopyToAsync (fs);
        }
        Console.WriteLine ($"Saved {url} to: {saveAs}");
    }

    static async Task<JsonDocument> GetDocAsync(string url, string? saveAs = null)
    {
        var json = await Client.GetStringAsync(url);
        if (!string.IsNullOrEmpty (saveAs)) {
            File.WriteAllText (saveAs, json);
            Console.WriteLine ($"Saved json to: {saveAs}");
        }
        return ParseJson (json);
    }

    static JsonDocument ParseJson (string json)
    {
        return JsonDocument.Parse (json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

}

