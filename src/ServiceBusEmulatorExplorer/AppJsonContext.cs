using System.Text.Json.Serialization;

namespace ServiceBusEmulatorExplorer;

[JsonSerializable(typeof(List<QueueInfo>))]
[JsonSerializable(typeof(QueueInfo))]
[JsonSerializable(typeof(TopicInfo))]
[JsonSerializable(typeof(List<TopicInfo>))]
[JsonSerializable(typeof(SubscriptionInfo))]
[JsonSerializable(typeof(List<SubscriptionInfo>))]
[JsonSerializable(typeof(MessageInfo))]
[JsonSerializable(typeof(PagedMessages))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(CreateTopicRequest))]
[JsonSerializable(typeof(CreateSubscriptionRequest))]
[JsonSerializable(typeof(CreateQueueRequest))]
[JsonSerializable(typeof(BulkDlqDeleteRequest))]
public partial class AppJsonContext : JsonSerializerContext;
