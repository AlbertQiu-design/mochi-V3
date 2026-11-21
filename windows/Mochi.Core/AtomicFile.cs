namespace Mochi.Core;

public static class AtomicFile
{
    public static void Write(byte[] data, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var name = Path.GetFileName(path);
        var temp = Path.Combine(directory ?? "", "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temp, data);
        if (File.Exists(path))
        {
            File.Move(temp, path, overwrite: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    public static void WriteText(string text, string path)
    {
        Write(new System.Text.UTF8Encoding(false).GetBytes(text), path);
    }
}

public static class FileBits
{
    public static bool IsExecutable(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        if (OperatingSystem.IsWindows())
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        }
        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Walks the path and follows symlinks, matching Swift's resolvingSymlinksInPath.
    public static string ResolveSymlinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return full;
        var current = root;
        var remainder = full[root.Length..];
        foreach (var part in remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo? link = null;
            if (Directory.Exists(current))
            {
                link = Directory.ResolveLinkTarget(current, returnFinalTarget: true);
            }
            else if (File.Exists(current))
            {
                link = File.ResolveLinkTarget(current, returnFinalTarget: true);
            }
            if (link != null) current = link.FullName;
        }
        return current;
    }

    public static void Backup(string path)
    {
        if (!File.Exists(path)) return;
        var backup = path + ".mochi-island.bak";
        if (File.Exists(backup)) File.Delete(backup);
        File.Copy(path, backup);
    }
}
