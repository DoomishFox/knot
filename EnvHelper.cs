using System.Runtime.InteropServices;

namespace knot;

// macos:
// file to place path in: /etc/paths.d/node
// then you can run:
// $PATH = ""
//if [ -x /usr/libexec/path_helper ]; then
//    eval `/usr/libexec/path_helper -s`
//fi

static class EnvHelper
{
    public static bool PathHas(string value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (File.Exists("/etc/paths.d/node")
                && File.ReadAllText("/etc/paths.d/node").Trim() == value)
                return true;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new NotImplementedException();
        }
        return false;
    }

    public static void AddToSystemPath(string additional)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            using var f = File.CreateText("/etc/paths.d/node");
            f.Write(additional);
            f.Flush();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new NotImplementedException();
        }
    }

    public static void RemoveFromSystemPath(string removal)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            File.Delete("/etc/paths.d/node");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new NotImplementedException();
        }
    }

    public static void AddToCurrentShell(string additional)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        throw new NotImplementedException();
    }
}