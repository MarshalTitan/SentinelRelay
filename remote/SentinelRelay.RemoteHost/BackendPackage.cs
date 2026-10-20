using System.Security.Cryptography;
using SentinelRelay.RemoteInput;
namespace SentinelRelay.RemoteHost;
public static class BackendPackage
{
    public static async Task<string> Prepare()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sentinel", "GamepadCheck", "backend");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("backend_cache_link");
        var path = Path.Combine(directory, "viiper-0.9.0.exe");
        if (File.Exists(path) && await Task.Run(() => ValidBackend(path))) return path;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await client.GetAsync("https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-windows-amd64.exe",
            HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 32 * 1024 * 1024) throw new InvalidDataException("backend_size");
        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(cancel.Token))
            {
                var buffer = new byte[65536]; long total = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancel.Token); if (count == 0) break;
                    total += count; if (total > 32 * 1024 * 1024) throw new InvalidDataException("backend_size");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancel.Token);
                }
            }
            if (!await Task.Run(() => ValidBackend(temp))) throw new InvalidDataException("backend_verification");
            File.Move(temp, path, true);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static bool ValidBackend(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(ViiperBackend.WindowsSha256, StringComparison.OrdinalIgnoreCase) &&
            SignatureCheck.Valid(path);
    }


}
