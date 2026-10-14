using System.Globalization;

namespace ServiceBusEmulatorExplorer;

internal static class EntityValidation
{
    public static Dictionary<string, string[]> Validate(string? name, int maxNameLength = 260,
        int? maxDeliveryCount = null, string? lockDuration = null, string? defaultTtl = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > maxNameLength ||
            !char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]) ||
            name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            errors["name"] = [$"Use 1–{maxNameLength} letters, numbers, hyphens, underscores or periods; start and end with a letter or number."];
        if (maxDeliveryCount is <= 0)
            errors["maxDeliveryCount"] = ["Max delivery count must be greater than zero."];
        if (lockDuration is not null && (!TryDuration(lockDuration, out var duration) ||
                                        duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(5)))
            errors["lockDuration"] = ["Lock duration must be a valid TimeSpan greater than zero and at most 00:05:00."];
        if (defaultTtl is not null && (!TryDuration(defaultTtl, out var ttl) || ttl <= TimeSpan.Zero || ttl > TimeSpan.FromHours(1)))
            errors["defaultTtl"] = ["Default TTL must be a valid TimeSpan greater than zero and at most 01:00:00 (emulator limit)."];
        return errors;
    }

    private static bool TryDuration(string value, out TimeSpan duration) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out duration);

    public static TimeSpan DurationOrDefault(string? value, TimeSpan fallback) =>
        value is null ? fallback : TimeSpan.Parse(value, CultureInfo.InvariantCulture);
}
