namespace ServiceBusEmulatorExplorer;

public class OtlpConfig
{
    public static string SectionName => "Otlp";
    public string Endpoint { get; set; } = string.Empty;
    public bool EnableConsoleExporter { get; set; }
}