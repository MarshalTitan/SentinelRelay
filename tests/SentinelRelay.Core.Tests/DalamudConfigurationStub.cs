namespace Dalamud.Configuration;

// Keeps the configuration model testable without loading the Dalamud runtime.
public interface IPluginConfiguration
{
    int Version { get; set; }
}
