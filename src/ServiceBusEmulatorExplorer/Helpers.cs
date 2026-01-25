namespace ServiceBusEmulatorExplorer;

public class Helpers
{
    
}

public readonly record struct CaseInsensitiveEnum<T>(T Value) where T : struct, Enum
{
    public static bool TryParse(string? value, out CaseInsensitiveEnum<T> result)
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
        {
            result = new CaseInsensitiveEnum<T>(parsed);
            return true;
        }
        result = default;
        return false;
    }
    
    public static implicit operator T(CaseInsensitiveEnum<T> e) => e.Value;
}
