namespace ServiceBusEmulatorExplorer;

public sealed class ExplorerOptions
{
    public bool ReadOnly { get; set; }
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173", "http://127.0.0.1:5173"];
}
