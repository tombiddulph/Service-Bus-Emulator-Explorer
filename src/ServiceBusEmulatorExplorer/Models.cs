using System.Text.Json.Serialization;

namespace ServiceBusEmulatorExplorer;

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

public record QueueInfo(
    string Name,
    EntityStatus Status,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long? ScheduledMessageCount = null,
    long? MaxDeliveryCount = null,
    string? LockDuration = null,
    string? DefaultTtl = null,
    DateTimeOffset? CreatedAt = null
);

public record TopicInfo(
    string Name,
    EntityStatus Status,
    int ActiveMessageCount,
    int DeadLetterMessageCount,
    int? ScheduledMessageCount = null,
    DateTime? CreatedAt = null
);

public record SubscriptionInfo(
    string Name,
    EntityStatus Status,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long? ScheduledMessageCount = null,
    long? MaxDeliveryCount = null,
    string? LockDuration = null,
    string? DefaultTtl = null
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
    Dictionary<string, object>? UserProperties = null
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
