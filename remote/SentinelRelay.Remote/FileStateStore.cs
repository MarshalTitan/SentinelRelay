using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace SentinelRelay.Remote;

/// <summary>Owner-only state, exclusive cross-process transactions, atomic replace. Hashes only.</summary>
public sealed class FileStateStore : IStateStore
{
    private readonly string directory;
    private readonly string path;
    private readonly object gate = new();
    public FileStateStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        RejectLink(this.directory);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(this.directory).SetAccessControl(security);
        }
        else File.SetUnixFileMode(this.directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        path = Path.Combine(this.directory, "authority.json");
        Access(_ => (true, true));
    }
    public T Access<T>(Func<RemoteState, (T Result, bool Save)> action)
    {
        lock (gate) return AccessCore(action);
    }
    private T AccessCore<T>(Func<RemoteState, (T Result, bool Save)> action)
    {
        var lockPath = Path.Combine(directory, "authority.lock");
        RejectLink(lockPath);
        using var transaction = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RejectLink(path);
        var state = File.Exists(path) ? JsonSerializer.Deserialize<RemoteState>(File.ReadAllBytes(path), Protocol.Json)
            ?? throw new InvalidDataException("invalid_state") : new RemoteState();
        if (state.Version != Protocol.Version || state.Devices is null || state.Instances is null || state.Invitations is null)
            throw new InvalidDataException("invalid_state");
        var (result, save) = action(state);
        if (save)
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    JsonSerializer.Serialize(file, state, Protocol.Json);
                    file.Flush(true);
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return result;
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("state_link_rejected");
    }
}
