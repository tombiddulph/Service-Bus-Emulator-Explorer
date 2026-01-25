namespace ServiceBusEmulatorExplorer;

public class ServiceBusConfig
{
    public const string SectionName = "ServiceBus";
    public string ConnectionString { get; set; } = null!;
    public string AdministrationConnectionString { get; set; } = null!;
    public string? EmulatorConfigFilePath { get; set; } 
}