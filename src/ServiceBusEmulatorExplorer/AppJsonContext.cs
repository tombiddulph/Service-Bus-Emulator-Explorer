using System.Text.Json.Serialization;

namespace ServiceBusEmulatorExplorer;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<QueueInfo>))]
[JsonSerializable(typeof(QueueInfo))]
[JsonSerializable(typeof(TopicInfo))]
[JsonSerializable(typeof(List<TopicInfo>))]
[JsonSerializable(typeof(SubscriptionInfo))]
[JsonSerializable(typeof(List<SubscriptionInfo>))]
[JsonSerializable(typeof(EnvironmentInfo))]
[JsonSerializable(typeof(MessageInfo))]
[JsonSerializable(typeof(PagedMessages))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(CreateTopicRequest))]
[JsonSerializable(typeof(CreateSubscriptionRequest))]
[JsonSerializable(typeof(CreateQueueRequest))]
[JsonSerializable(typeof(BulkDlqDeleteRequest))]
[JsonSerializable(typeof(ReplayDlqRequest))]
[JsonSerializable(typeof(CountResult))]
[JsonSerializable(typeof(ReplayDlqResult))]
[JsonSerializable(typeof(DeleteDlqResult))]
[JsonSerializable(typeof(PurgeResult))]
public partial class AppJsonContext : JsonSerializerContext;
