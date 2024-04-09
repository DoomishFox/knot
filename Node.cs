using System.Data;
using System.Diagnostics;
using System.Formats.Tar;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace knot;

class NodeInstall
{
    public required string Version { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}

class NodeDist
{
    public required string version { get; set; }
    public required string date { get; set; }
    /* 1) i dont need these, and 2) they're not always in the dist index.
    public required List<string> files { get; set; }
    public required string npm { get; set; }
    public required string v8 { get; set; }
    public required string uv { get; set; }
    public required string zlib { get; set; }
    public required string openssl { get; set; }
    public required string modules { get; set; }
    */
    // it turns out node's dist index provides lts as EITHER a boolean or a
    // string (im assuming for named release versions). this is catastrophic
    // to any json parser that isnt built in javascript so i guess "lts" will
    // just not be available. sucks to suck.
    //public required bool lts { get; set; }
    public required bool security { get; set; }
}

static class Node
{
    private static string? _systemInstall = null;
    private static string SystemInstall => _systemInstall ?? GetSystemInstall();
    // this function should return the system normalized path
    // i.e. on linux and darwin it'll be /usr/local/bin
    // but on windows it'll be some C:\ProgramFiles bullshit
    // NOTE: this means i will need to ask for UAC on windows, but that
    // should be fine. i *think* i can elevate a current process
    private static string GetSystemInstall() => "/usr/local/bin/node";

    //private static string? _downloadDir = "/usr/local/node/downloads";
    private static string? _installDir = null;
    private static string InstallDir => _installDir ?? GetInstallDir();
    private static string GetInstallDir() => "/usr/local/node/versions";
    private static string _nodeMirror = "https://nodejs.org/dist";

    private static JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<IEnumerable<NodeDist>> UpdateDistIndex()
    {
        // get _nodeMirror / index.json and parse it
        Console.WriteLine("updating node distribution index...");
        using var client = new HttpClient();
        var response = await client.GetAsync(_nodeMirror + "/index.json");
        response.EnsureSuccessStatusCode();
        return await JsonSerializer.DeserializeAsync<IEnumerable<NodeDist>>(await response.Content.ReadAsStreamAsync())
            ?? throw new Exception("could not parse node index!");
    }

    public static async Task<string> NormalizeVersionString(string version)
    {
        /*
        version strings can come in serveral different ways
        the expected format is v<major>.<minor.<revision>
        for example: v21.7.2
        however, i want flexibility on inputting it so version strings can be inputted
        in any of these kinds of variations:
        - v21.7.2
        - v21
        - 21
        with two specific cases:
        - latest
        - lts
        */

        var distIndex = await UpdateDistIndex();
        NodeDist? matchedDist = null;
        // check special cases
        if (version == "latest")
        {
            matchedDist = distIndex.First();
        }
        /* see comment in NodeDist
        else if (version == "lts")
        {
            matchedDist = distIndex.First((dist) => dist.lts);
        }
        */
        else
        {
            string prefixedVersion = version.StartsWith('v') ? version : $"v{version}";
            // since the dist index is in decreasing order we can just grab the first
            // matching one
            matchedDist = distIndex.First((dist) => dist.version.Contains(prefixedVersion));
        }

        if (matchedDist is null)
            throw new VersionNotFoundException();
        return matchedDist.version;
    }

    public static IEnumerable<NodeInstall> Versions()
    {
        if (HasSystemInstall())
            yield return new() { Version = SystemInstallVersion(), IsSystem = true };
        if (Directory.Exists(InstallDir))
        {
            string[]? directories = Directory.GetDirectories(InstallDir);
            foreach (string versionPath in directories)
            {
                var active = EnvHelper.PathHas($"{versionPath}/bin");
                yield return new() { Version = Path.GetFileName(versionPath), IsActive = active };
            }
        }
    }

    public static string CurrentVersion() => 
        Versions()
            .First((v) => v.IsActive)
            .Version;

    public static void SetCurrentVersion(string version)
    {
        var versionedInstallDir = $"{InstallDir}/{version}/bin";
        if (EnvHelper.PathHas(versionedInstallDir))
            EnvHelper.RemoveFromSystemPath(versionedInstallDir);
        EnvHelper.AddToSystemPath(versionedInstallDir);
    }

    public static bool HasSystemInstall() => File.Exists(SystemInstall);

    public static string SystemInstallVersion()
    {
        // check version string of installed system version
        if (!HasSystemInstall())
            throw new VersionNotFoundException("no system install detected!");
        string versionString = Process.Start(SystemInstall, "--version").StandardOutput.ReadToEnd();
        Console.WriteLine(versionString);
        return "";
    }

    public static async Task<string> DownloadNodeVersion(string version, bool verbose = false)
    {
        /*
        what needs to happen here is:
        1. assemble download url
        2. download binary archive
        3. decompress to install dir
        */

        // download url is <mirror>/<version>/<archive_name>.<compression>

        string flavor = "node";
        string os = RuntimeInformation.OSDescription switch
        {
            string darwin when darwin.Contains("Darwin") => "darwin",
            string win when win.Contains("Windows") => "win",
            // not sure if this works, gotta test on a linux system
            // string linux when linux.Contains("Linux") => "linux",
            // everything else can stay unhandled, i do Not care about sunOS or BSD
            _ => throw new PlatformNotSupportedException("unknown operating system!")
        };
        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            // theres a bunch more that OSArchitecture can respond with, see:
            // https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.architecture?view=net-8.0
            // but i don't give a shit about supporting *power pc*
            _ => throw new PlatformNotSupportedException("unsupported architecture!")
        };
        // compression can be:
        // - .tar.gz
        // - .zip
        // - .tar.xz
        // for our purposes we can assume tar.gz
        // if we're on windows switch to zip
        // and if we support xz then switch to tar.xz
        string compression = os switch
        {
            "win" => "zip",
            _ => "tar.gz"
        };
        // im ignoring an xz check for now because im lazy. maybe ill add that
        // eventually
        string archiveName = $"{flavor}-{version}-{os}-{arch}";
        string downloadUrl = $"{_nodeMirror}/{version}/{archiveName}.{compression}";

        using var client = new HttpClient();
        var response = await client.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();
        var archiveStream = await response.Content.ReadAsStreamAsync()
            ?? throw new Exception("invalid download stream!");

        // i actually dont have to care about downloading to disk!
        // modern systems easily have enough ram to hold a node binary archive
        // in memory so i can just copy to a memory stream and then decompress
        // it
        // the only thing is im not *sure* of is if i actually need a
        // memorystream. i might be able to just use the stream returned from
        // httpclient?

        /*
        if (!Directory.Exists(_downloadDir))
            Directory.CreateDirectory(_downloadDir);
        using FileStream fs = File.Create($"{_downloadDir}/{archiveName}");
        // TODO: add progress reporting
        // unfortunately, the defualt copytoasync doesnt have progress reporting
        // so id have to make my own implementation lmao
        // https://stackoverflow.com/questions/39742515/stream-copytoasync-with-progress-reporting-progress-is-reported-even-after-cop
        await archiveStream.CopyToAsync(fs);
        */
        var versionedInstallDir = $"{InstallDir}/{version}";
        if (!Directory.Exists(InstallDir))
            Directory.CreateDirectory(InstallDir);

        // decompression
        // compression var gets matched above so i know it'll never be anything
        // unexpected
        if (compression == "tar.gz")
        {
            using var tar = await archiveStream.ExtractGzAsync();
            await TarFile.ExtractToDirectoryAsync(tar, InstallDir, overwriteFiles: true);
            // fix this eventually
            // i think this'll need me to make my own tar extract thing which i
            // do Not want to do right now. this is fine.
            Directory.Move($"{InstallDir}/{archiveName}", versionedInstallDir);
        }
        else if (compression == "zip")
        {
            throw new NotImplementedException();
        }

        return $"{InstallDir}/{version}";
    }

    public static void RemoveNodeVersion(string version)
    {
        // only thing we have to be sure of is if we're removing the active
        // version it needs to fall back to some other version. ig the next
        // newest one would be fine? or just the latest available
        var versionedInstallDir = $"{InstallDir}/{version}";
        Directory.Delete(versionedInstallDir, recursive: true);
        var versionedInstallBinDir = $"{versionedInstallDir}/bin";
        if (EnvHelper.PathHas(versionedInstallBinDir))
            EnvHelper.RemoveFromSystemPath(versionedInstallBinDir);
    }
}