namespace SbExplorer.Mud.Config;

public class ServiceBusConfig
{
    public const string SectionName = "ServiceBus";
    public string ConnectionString { get; set; } = null!;
    public int RefreshIntervalMs { get; set; }
    public string EmulatorConfigFilePath { get; set; } = null!;
}

public class RedisConfig
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; set; } = null!;
    public string InstanceName { get; set; } = null!;
}