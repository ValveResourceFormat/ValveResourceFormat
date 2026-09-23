using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SteamKit2;
using SteamKit2.CDN;
using SteamKit2.Internal;

// ANSI color codes, only used when the output is a terminal
const string Bold = "1";
const string Red = "31";
const string Green = "32";
const string Cyan = "36";
const string BoldYellow = "1;33";
const string Gray = "90";

var colorOut = ShouldUseColor(Console.IsOutputRedirected);
var colorErr = ShouldUseColor(Console.IsErrorRedirected);

uint searchAppId = 730;
string? outputRoot = null;
var requestedIds = new List<ulong>();
var searchTerms = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];

    if (arg == "--app" && i + 1 < args.Length)
    {
        searchAppId = uint.Parse(args[++i], CultureInfo.InvariantCulture);
    }
    else if (arg == "--output" && i + 1 < args.Length)
    {
        outputRoot = args[++i];
    }
    else if (TryParsePublishedFileId(arg, out var id))
    {
        requestedIds.Add(id);
    }
    else
    {
        searchTerms.Add(arg);
    }
}

if (requestedIds.Count == 0 && searchTerms.Count == 0)
{
    PrintUsage();
    return 1;
}

outputRoot = Path.GetFullPath(outputRoot ?? Path.Combine(GetMiscFolder(), "workshop"));

var client = new SteamClient();
var manager = new CallbackManager(client);
var user = client.GetHandler<SteamUser>()!;
var apps = client.GetHandler<SteamApps>()!;
var content = client.GetHandler<SteamContent>()!;
var publishedFile = client.GetHandler<SteamUnifiedMessages>()!.CreateService<PublishedFile>();
using var cdnClient = new Client(client);

var connected = new TaskCompletionSource<bool>();
var loggedOn = new TaskCompletionSource<SteamUser.LoggedOnCallback?>();

using var connectedSub = manager.Subscribe<SteamClient.ConnectedCallback>(_ => connected.TrySetResult(true));
using var disconnectedSub = manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
{
    connected.TrySetResult(false);
    loggedOn.TrySetResult(null);
});
using var loggedOnSub = manager.Subscribe<SteamUser.LoggedOnCallback>(callback => loggedOn.TrySetResult(callback));

using var pumpCts = new CancellationTokenSource();
var pump = Task.Run(async () =>
{
    try
    {
        while (!pumpCts.IsCancellationRequested)
        {
            await manager.RunWaitCallbackAsync(pumpCts.Token).ConfigureAwait(false);
        }
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown
    }
});

var logOn = await ConnectToSteam().ConfigureAwait(false);

if (logOn == null)
{
    return 1;
}

if (searchTerms.Count > 0)
{
    var searchText = string.Join(' ', searchTerms);
    var results = await SearchWorkshop(searchText).ConfigureAwait(false);

    if (results.Count == 0)
    {
        WriteError($"No results for \"{searchText}\" in app {searchAppId}.");
        client.Disconnect();
        return 1;
    }

    PrintSearchResults(results);

    if (!Console.IsInputRedirected)
    {
        requestedIds.AddRange(AskForSelection(results));
    }

    if (requestedIds.Count == 0)
    {
        client.Disconnect();
        return 0;
    }
}

var failedItems = new HashSet<ulong>();
var items = await ResolveItems(requestedIds).ConfigureAwait(false);

var servers = (await content.GetServersForSteamPipe(cellId: logOn.CellID).ConfigureAwait(false))
    .Where(static s => s.AllowedAppIds.Length == 0 && !s.UseAsProxy && !s.SteamChinaOnly && s.Type is "SteamCache" or "CDN")
    .ToList();
var nextServer = 0;

if (servers.Count == 0)
{
    WriteError("No CDN servers available.");
    client.Disconnect();
    return 1;
}

var fileJobs = new List<FileJob>();
var manifestJobs = new List<(PublishedFileDetails Item, byte[] DepotKey)>();
var depotKeys = new Dictionary<uint, byte[]?>();

foreach (var item in items)
{
    await Console.Error.WriteLineAsync($"{Prefix(item.publishedfileid)} {Color(item.title, Bold, colorErr)} ({FormatSize(item.file_size)})").ConfigureAwait(false);

    // Older items are a single file hosted on a plain url
    if (!string.IsNullOrEmpty(item.file_url))
    {
        var fileName = Path.GetFileName(item.filename);

        if (string.IsNullOrEmpty(fileName))
        {
            fileName = item.publishedfileid.ToString(CultureInfo.InvariantCulture);
        }

        fileJobs.Add(new FileJob(item, fileName));
        continue;
    }

    if (item.hcontent_file == 0)
    {
        WriteError($"[{item.publishedfileid}] Item has no downloadable content.");
        failedItems.Add(item.publishedfileid);
        continue;
    }

    var depotKey = await GetDepotKey(item.consumer_appid).ConfigureAwait(false);

    if (depotKey == null)
    {
        failedItems.Add(item.publishedfileid);
        continue;
    }

    manifestJobs.Add((item, depotKey));
}

await Parallel.ForEachAsync(manifestJobs, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (job, _) =>
{
    var manifest = await DownloadManifest(job.Item.consumer_appid, job.Item.hcontent_file, job.DepotKey).ConfigureAwait(false);

    if (manifest?.Files == null)
    {
        WriteError($"[{job.Item.publishedfileid}] Failed to download manifest.");

        lock (failedItems)
        {
            failedItems.Add(job.Item.publishedfileid);
        }

        return;
    }

    lock (fileJobs)
    {
        foreach (var file in manifest.Files)
        {
            if ((file.Flags & EDepotFileFlag.Directory) == 0)
            {
                fileJobs.Add(new FileJob(job.Item, file.FileName, file, job.DepotKey));
            }
        }
    }
}).ConfigureAwait(false);

// Files are downloaded over plain HTTP, so the Steam connection is no longer needed
client.Disconnect();
await pumpCts.CancelAsync().ConfigureAwait(false);
await pump.ConfigureAwait(false);

using var httpClient = new HttpClient();

await Parallel.ForEachAsync(fileJobs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (job, cancellationToken) =>
{
    var itemId = job.Item.publishedfileid;

    try
    {
        var downloaded = await DownloadFile(job, cancellationToken).ConfigureAwait(false);
        var status = downloaded ? Color("Downloaded", Green, colorErr) : Color("Up to date", Gray, colorErr);

        await Console.Error.WriteLineAsync($"{Prefix(itemId)} {status} {job.FileName}").ConfigureAwait(false);
    }
    catch (Exception e)
    {
        WriteError($"[{itemId}] Failed {job.FileName}: {e.Message}");

        lock (failedItems)
        {
            failedItems.Add(itemId);
        }
    }
}).ConfigureAwait(false);

foreach (var item in items)
{
    if (!failedItems.Contains(item.publishedfileid))
    {
        Console.WriteLine(Color(GetItemFolder(item), Green, colorOut));
    }
}

return failedItems.Count > 0 ? 1 : 0;

void PrintUsage()
{
    Console.WriteLine("Downloads Steam Workshop items anonymously (Counter-Strike 2 and Dota 2).");
    Console.WriteLine();
    Console.WriteLine("Usage: WorkshopDownloader <id|url|search text>... [--app <appid>] [--output <folder>]");
    Console.WriteLine();
    Console.WriteLine("  <id|url>       Published file ids or workshop URLs, collections are expanded.");
    Console.WriteLine("  <search text>  Lists matching items, and prompts for them when running interactively.");
    Console.WriteLine("  --app          App to search in (default: 730), ids and urls detect their app.");
    Console.WriteLine("  --output       Output folder (default: Misc/workshop), items go into <id>_<name>/.");
}

async Task<SteamUser.LoggedOnCallback?> ConnectToSteam()
{
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        await Console.Error.WriteLineAsync(Color("Connecting to Steam...", Gray, colorErr)).ConfigureAwait(false);
        client.Connect();

        if (await connected.Task.ConfigureAwait(false))
        {
            user.LogOnAnonymous();

            var result = await loggedOn.Task.ConfigureAwait(false);

            if (result?.Result == EResult.OK)
            {
                return result;
            }

            WriteError($"Failed to log on anonymously: {result?.Result}");
            client.Disconnect();
            return null;
        }

        connected = new TaskCompletionSource<bool>();
        loggedOn = new TaskCompletionSource<SteamUser.LoggedOnCallback?>();
        await Task.Delay(TimeSpan.FromSeconds(attempt)).ConfigureAwait(false);
    }

    WriteError("Failed to connect to Steam.");
    return null;
}

async Task<List<PublishedFileDetails>> SearchWorkshop(string text)
{
    var response = await publishedFile.QueryFiles(new CPublishedFile_QueryFiles_Request
    {
        appid = searchAppId,
        search_text = text,
        numperpage = 20,
        query_type = (uint)SteamKit2.EPublishedFileQueryType.RankedByTextSearch,
        return_metadata = true,
    });

    if (response.Result != EResult.OK)
    {
        WriteError($"Search failed ({response.Result}).");
        return [];
    }

    return response.Body.publishedfiledetails;
}

void PrintSearchResults(List<PublishedFileDetails> results)
{
    Console.WriteLine(Color($"{"#",3} {"id",-12} {"size",10} {"updated",-10} {"subs",8} {"favs",6} title", Gray, colorOut));

    var newest = results.Max(static r => r.time_updated);
    var mostSubs = results.Max(static r => r.lifetime_subscriptions);
    var mostFavs = results.Max(static r => r.lifetime_favorited);

    for (var i = 0; i < results.Count; i++)
    {
        var result = results[i];
        var id = Color($"{result.publishedfileid,-12}", Cyan, colorOut);
        var updated = Highlight($"{DateTimeOffset.FromUnixTimeSeconds(result.time_updated):yyyy-MM-dd}", result.time_updated == newest);
        var subs = Highlight($"{result.lifetime_subscriptions,8}", result.lifetime_subscriptions == mostSubs);
        var favs = Highlight($"{result.lifetime_favorited,6}", result.lifetime_favorited == mostFavs);
        var title = Color(result.title, Bold, colorOut);

        Console.WriteLine($"{i + 1,3} {id} {FormatSize(result.file_size),10} {updated} {subs} {favs} {title}");
    }

    string Highlight(string text, bool isBest) => isBest ? Color(text, BoldYellow, colorOut) : text;
}

static List<ulong> AskForSelection(List<PublishedFileDetails> results)
{
    Console.Write($"Select items to download (1-{results.Count}, separated by spaces): ");

    var selected = new List<ulong>();
    var input = Console.ReadLine() ?? string.Empty;

    foreach (var part in input.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
    {
        if (int.TryParse(part, out var number) && number >= 1 && number <= results.Count)
        {
            selected.Add(results[number - 1].publishedfileid);
        }
    }

    return selected;
}

async Task<List<PublishedFileDetails>> ResolveItems(List<ulong> ids)
{
    var resolved = new List<PublishedFileDetails>();
    var seen = new HashSet<ulong>();
    var pending = ids.Where(seen.Add).ToList();

    // Collections are expanded into their children, which may be collections themselves
    while (pending.Count > 0)
    {
        var request = new CPublishedFile_GetDetails_Request { includechildren = true };
        request.publishedfileids.AddRange(pending);

        var response = await publishedFile.GetDetails(request);

        if (response.Result != EResult.OK)
        {
            WriteError($"Failed to get published file details ({response.Result}).");
            failedItems.UnionWith(pending);
            break;
        }

        pending = [];

        foreach (var details in response.Body.publishedfiledetails)
        {
            var result = (EResult)details.result;

            if (result != EResult.OK)
            {
                WriteError($"[{details.publishedfileid}] Failed to get details ({result}).");
                failedItems.Add(details.publishedfileid);
                continue;
            }

            if ((EWorkshopFileType)details.file_type == EWorkshopFileType.Collection)
            {
                await Console.Error.WriteLineAsync($"{Prefix(details.publishedfileid)} Collection {Color(details.title, Bold, colorErr)} with {details.children.Count} items").ConfigureAwait(false);
                pending.AddRange(details.children.Select(static c => c.publishedfileid).Where(seen.Add));
                continue;
            }

            resolved.Add(details);
        }
    }

    return resolved;
}

async Task<byte[]?> GetDepotKey(uint appId)
{
    if (depotKeys.TryGetValue(appId, out var cachedKey))
    {
        return cachedKey;
    }

    // Source 2 workshop content lives in a depot with the same id as the app
    var result = await apps.GetDepotDecryptionKey(appId, appId);
    var depotKey = result.Result == EResult.OK ? result.DepotKey : null;

    if (depotKey == null)
    {
        WriteError($"Workshop content for app {appId} is not available anonymously ({result.Result}).");
    }

    depotKeys[appId] = depotKey;
    return depotKey;
}

async Task<DepotManifest?> DownloadManifest(uint appId, ulong manifestId, byte[] depotKey)
{
    const int MaxAttempts = 5;

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var requestCode = await content.GetManifestRequestCode(appId, appId, manifestId, "public").ConfigureAwait(false);

            return await cdnClient.DownloadManifestAsync(appId, manifestId, requestCode, GetServer(), depotKey).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            WriteError($"Manifest {manifestId} download failed: {e.Message}");

            if (attempt == MaxAttempts)
            {
                return null;
            }

            Interlocked.Increment(ref nextServer);
            await Task.Delay(TimeSpan.FromSeconds(attempt)).ConfigureAwait(false);
        }
    }
}

async Task<bool> DownloadFile(FileJob job, CancellationToken cancellationToken)
{
    var folder = GetItemFolder(job.Item);
    var path = Path.GetFullPath(Path.Combine(folder, job.FileName.Replace('\\', '/')));

    if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    {
        throw new InvalidDataException("File path escapes the item folder.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    var file = job.File;

    if (file == null)
    {
        using var httpStream = await httpClient.GetStreamAsync(new Uri(job.Item.file_url), cancellationToken).ConfigureAwait(false);
        using var fileStream = File.Create(path);
        await httpStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

        return true;
    }

    if (File.Exists(path) && new FileInfo(path).Length == (long)file.TotalSize && await HasExpectedHash().ConfigureAwait(false))
    {
        return false;
    }

    using (var handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous, (long)file.TotalSize))
    {
        RandomAccess.SetLength(handle, (long)file.TotalSize);

        await Parallel.ForEachAsync(file.Chunks, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (chunk, chunkCancellationToken) =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent((int)chunk.UncompressedLength);

            try
            {
                var written = await DownloadChunk(job.Item.consumer_appid, chunk, buffer, job.DepotKey!, chunkCancellationToken).ConfigureAwait(false);

                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, written), (long)chunk.Offset, chunkCancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }).ConfigureAwait(false);
    }

    if (!await HasExpectedHash().ConfigureAwait(false))
    {
        File.Delete(path);
        throw new InvalidDataException("Hash mismatch.");
    }

    return true;

    // Empty files have a zeroed hash in the manifest
    async Task<bool> HasExpectedHash()
    {
        if (file.TotalSize == 0)
        {
            return true;
        }

        var hash = await HashFile(path, cancellationToken).ConfigureAwait(false);
        return file.FileHash.SequenceEqual(hash);
    }
}

async Task<int> DownloadChunk(uint depotId, DepotManifest.ChunkData chunk, byte[] buffer, byte[] depotKey, CancellationToken cancellationToken)
{
    const int MaxAttempts = 5;

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return await cdnClient.DownloadDepotChunkAsync(depotId, chunk, GetServer(), buffer, depotKey).ConfigureAwait(false);
        }
        catch (Exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref nextServer);
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }
    }
}

Server GetServer() => servers[nextServer % servers.Count];

string GetItemFolder(PublishedFileDetails item) => Path.Combine(outputRoot, GetFolderName(item));

string Prefix(ulong publishedFileId) => Color($"[{publishedFileId}]", Cyan, colorErr);

void WriteError(string message) => Console.Error.WriteLine(Color(message, Red, colorErr));

static async Task<byte[]> HashFile(string path, CancellationToken cancellationToken)
{
    using var stream = File.OpenRead(path);
#pragma warning disable CA5350 // Steam manifests store SHA-1 file hashes
    return await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA5350
}

static bool TryParsePublishedFileId(string input, out ulong id)
{
    if (ulong.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out id))
    {
        return true;
    }

    var match = Regex.Match(input, @"^https?://steamcommunity\.com/.*[?&]id=(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    return match.Success && ulong.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out id);
}

static string GetFolderName(PublishedFileDetails item)
{
    var id = item.publishedfileid.ToString(CultureInfo.InvariantCulture);
    var name = Regex.Replace(item.title.ToLowerInvariant(), "[^a-z0-9]+", "_", RegexOptions.CultureInvariant).Trim('_');

    if (name.Length > 64)
    {
        name = name[..64].TrimEnd('_');
    }

    return name.Length > 0 ? $"{id}_{name}" : id;
}

static string FormatSize(ulong bytes) => $"{bytes / 1024.0 / 1024.0:N1} MB";

static bool ShouldUseColor(bool redirected) => !redirected && Environment.GetEnvironmentVariable("NO_COLOR") == null;

static string Color(string text, string code, bool enabled) => enabled ? $"\e[{code}m{text}\e[0m" : text;

static string GetMiscFolder([CallerFilePath] string sourceFilePath = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, ".."));

record FileJob(PublishedFileDetails Item, string FileName, DepotManifest.FileData? File = null, byte[]? DepotKey = null);
