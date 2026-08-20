using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBusEmulatorExplorer;

public record EnvironmentInfo(string Name);

[JsonConverter(typeof(JsonStringEnumConverter<EntityStatus>))]
public enum EntityStatus
{
    Active,
    Disabled,
    SendDisabled,
    ReceiveDisabled
}

[JsonConverter(typeof(JsonStringEnumConverter<MessageState>))]
public enum MessageState
{
    Active,
    Deadletter
}

[JsonConverter(typeof(JsonStringEnumConverter<PeekMode>))]
public enum PeekMode
{
    Peek
}

[JsonConverter(typeof(JsonStringEnumConverter<PurgeStatus>))]
public enum PurgeStatus
{
    Completed,
    TimedOut,
    Unauthorized,
    Failed,
    SessionRequired
}

public record PurgeResult(PurgeStatus Status, int RemovedCount, string? Message = null);

public record QueueInfo(
    string Name,
    EntityStatus Status,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long? ScheduledMessageCount = null,
    long? MaxDeliveryCount = null,
    string? LockDuration = null,
    string? DefaultTtl = null,
    DateTimeOffset? CreatedAt = null,
    bool ActiveMessageCountIsExact = true,
    bool DeadLetterMessageCountIsExact = true
);

public record TopicInfo(
    string Name,
    EntityStatus Status,
    int ActiveMessageCount,
    int DeadLetterMessageCount,
    int? ScheduledMessageCount = null,
    DateTime? CreatedAt = null,
    bool ActiveMessageCountIsExact = true,
    bool DeadLetterMessageCountIsExact = true
);

public record SubscriptionInfo(
    string Name,
    EntityStatus Status,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long? ScheduledMessageCount = null,
    int? MaxDeliveryCount = null,
    string? LockDuration = null,
    string? DefaultTtl = null,
    DateTime? CreatedAt = null,
    bool ActiveMessageCountIsExact = true,
    bool DeadLetterMessageCountIsExact = true
);

public record MessageInfo(
    string MessageId,
    string BodyPreview,
    string? Body = null,
    DateTime? EnqueuedTime = null,
    DateTime? ExpiresAt = null,
    int? DeliveryCount = null,
    string? ContentType = null,
    string? SessionId = null,
    Dictionary<string, object?>? UserProperties = null,
    Dictionary<string, object>? SystemProperties = null
);

public record PagedMessages(
    IReadOnlyList<MessageInfo> Items,
    int? Total = null,
    bool? HasMore = null
);

public record SendMessageRequest(
    string Body,
    string? ContentType = null,
    Dictionary<string, JsonElement>? UserProperties = null,
    string? SessionId = null
);

public record CreateQueueRequest(
    string Name,
    int? MaxDeliveryCount = null,
    string? LockDuration = null,
    string? DefaultTtl = null
);

public record CreateTopicRequest(
    string Name
);

public record CreateSubscriptionRequest(
    string Name,
    int? MaxDeliveryCount = null,
    string? LockDuration = null,
    string? DefaultTtl = null
);

public record BulkDlqDeleteRequest(
    List<string>? MessageIds = null
);

public record ReplayDlqRequest(
    List<string>? MessageIds = null,
    string? Body = null,
    string? ContentType = null,
    Dictionary<string, JsonElement>? UserProperties = null,
    bool RemoveFromDlq = true
);

public record CountResult(int Count, List<string>? NotFound = null);

public record ReplayDlqResult(
    int Count,
    bool IsPartial,
    List<ReplayMessageOutcome> Outcomes,
    List<string>? NotFound = null,
    string? Error = null
);

public record ReplayMessageOutcome(
    string MessageId,
    bool Sent,
    bool RemovedFromDlq,
    string? Error = null
);
