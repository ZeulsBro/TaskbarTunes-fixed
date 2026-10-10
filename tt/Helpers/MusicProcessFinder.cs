using System.Diagnostics;
using System.IO;

namespace TaskbarTunes.Helpers;

/// <summary>
/// Finds the root process belonging to the selected media session.
/// Process loopback capture includes the target process tree, so choosing the
/// app's root process avoids capturing unrelated browser audio.
/// </summary>
public static class MusicProcessFinder
{
    private static readonly (string Key, string[] Processes)[] Map =
    {
        ("spotify", new[] { "Spotify" }),
        ("brave", new[] { "brave" }),
        ("chrome", new[] { "chrome" }),
        ("edge", new[] { "msedge" }),
        ("firefox", new[] { "firefox" }),
        ("opera", new[] { "opera" }),
        ("vivaldi", new[] { "vivaldi" }),
        ("zen", new[] { "zen" }),
        ("arc", new[] { "Arc" }),
        ("chromium", new[] { "chromium" }),
    };

    public static int? FindPid(string sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId)) return null;

        string id = sourceAppId.ToLowerInvariant();

        // The Yandex Music process name can be localized on some installations
        // (for example, "Яндекс Музыка.exe"), so exact English names are not enough.
        if (id.Contains("yandex") || id.Contains("яндекс"))
        {
            int? yandexPid = FindYandexMusicPid();
            if (yandexPid is not null) return yandexPid;
        }

        foreach (var (key, names) in Map)
        {
            if (!id.Contains(key)) continue;
            foreach (var name in names)
            {
                if (FindRootPid(name) is int pid) return pid;
            }
        }

        // Classic app AUMID such as "player.exe": try its process name directly.
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return FindRootPid(Path.GetFileNameWithoutExtension(sourceAppId));

        return null;
    }

    private static int? FindYandexMusicPid()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return null; }

        try
        {
            Process? fallback = null;
            foreach (var process in processes)
            {
                try
                {
                    string name = process.ProcessName.ToLowerInvariant();
                    bool englishName = name.Contains("yandex") && name.Contains("music");
                    bool localizedName = name.Contains("яндекс") && name.Contains("музык");
                    if (!englishName && !localizedName) continue;

                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.Id;

                    fallback ??= process;
                }
                catch
                {
                    // The process may exit or deny access while we enumerate it.
                }
            }

            return fallback?.Id;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static int? FindRootPid(string processName)
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName(processName); }
        catch { return null; }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.Id;
                }
                catch { }
            }

            return processes.Length > 0 ? processes[0].Id : null;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
}
