namespace ServiceBusEmulatorExplorer;

public sealed class DlqOperationOptions
{
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
