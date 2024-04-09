using CommandLine;

namespace knot;

class Program
{
    [Verb("install", HelpText = "install node version")]
    class InstallOptions
    {
        [Value(0, MetaName = "version", Required = true, HelpText = "node version")]
        public required string Version { get; set; }
    }

    [Verb("use", HelpText = "use node version")]
    class UseOptions
    {
        [Value(0, MetaName = "version", Required = true, HelpText = "node version")]
        public required string Version { get; set; }
    }

    [Verb("remove", HelpText = "remove node version")]
    class RemoveOptions
    {
        [Value(0, MetaName = "version", Required = true, HelpText = "node version")]
        public required string Version { get; set; }
    }

    [Verb("list", HelpText = "list installed node versions")]
    class ListOptions
    {
    }

    static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<InstallOptions, UseOptions, RemoveOptions, ListOptions>(args)
            .MapResult(
                async (InstallOptions opts) => await Install(opts),
                async (UseOptions opts) => await Task.FromResult(Use(opts)),
                async (RemoveOptions opts) => await Task.FromResult(Remove(opts)),
                async (ListOptions opts) => await Task.FromResult(List()),
                (errs) => Task.FromResult(HandleErrors(errs)));
    }

    static async Task<int> Install(InstallOptions opts)
    {
        try{
            string normalizedVersion = await Node.NormalizeVersionString(opts.Version);
            string outputDir = await Node.DownloadNodeVersion(normalizedVersion);
            Console.WriteLine($"installed node version {normalizedVersion} (to '{outputDir}')");
        }
        catch (Exception e) {
            Console.Error.WriteLine(e);
            // i might eventually get around to dealing with the actual
            // errors. maybe.
            return 1;
        }
        return 0;
    }

    static int Use(UseOptions opts)
    {
        string prefixedVersion = opts.Version.StartsWith('v') ? opts.Version : $"v{opts.Version}";
        // since the dist index is in decreasing order we can just grab the first
        // matching one
        var matchedDist = Node.Versions().Where((dist) => dist.Version.Contains(prefixedVersion));
        if (!matchedDist.Any())
        {
            Console.Error.WriteLine($"node version {prefixedVersion} not installed!");
            return 1;
        }
        if (matchedDist.Count() > 1)
        {
            Console.Error.WriteLine("multiple matching versions found! be more specific");
            foreach (var dist in matchedDist)
                Console.Error.WriteLine($"*  {dist.Version}");
            return 1;
        }
        string version = matchedDist.First().Version;
        Node.SetCurrentVersion(version);
        Console.WriteLine($"using node version {version}");
        return 0;
    }

    static int Remove(RemoveOptions opts)
    {
        // cant normalize the version string in the same manner, it needs
        // to be normalized in respect to the currenlty installed versions
        string prefixedVersion = opts.Version.StartsWith('v') ? opts.Version : $"v{opts.Version}";
        // since the dist index is in decreasing order we can just grab the first
        // matching one
        var matchedDist = Node.Versions().Where((dist) => dist.Version.Contains(prefixedVersion));
        if (!matchedDist.Any())
        {
            Console.Error.WriteLine($"node version {prefixedVersion} already not installed!");
            return 1;
        }
        if (matchedDist.Count() > 1)
        {
            Console.Error.WriteLine("multiple matching versions found! be more specific");
            foreach (var dist in matchedDist)
                Console.Error.WriteLine($"*  {dist.Version}");
            return 1;
        }
        var firstMatchedDist = matchedDist.First();
        Node.RemoveNodeVersion(firstMatchedDist.Version);
        Console.WriteLine($"removed node version {firstMatchedDist.Version}");
        // if we removed the active version fall back to whatever the first one is
        // if (firstMatchedDist.IsActive)
        // {
        //     string newVersion = Node.Versions().First().Version;
        //     Node.SetCurrentVersion(newVersion);
        //     Console.WriteLine($"using node version {newVersion}");
        // }
        return 0;
    }

    static int List()
    {
        if (Node.HasSystemInstall())
            Console.WriteLine("warning: system node install detected - system installs will cause knot to not work as expected!");
        //string currentVersion = Node.CurrentVersion();
        foreach (NodeInstall install in Node.Versions())
            Console.WriteLine($"{(install.IsActive ? "=>" : "* ")} {install.Version}{(install.IsSystem ? " (system)" : "")}");
        return 0;
    }

    static int HandleErrors(IEnumerable<Error> errs)
    {
        foreach (Error err in errs)
        {
            Console.Error.WriteLine(err);
        }
        return 1;
    }
}
