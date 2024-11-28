namespace SbExplorer.Mud.Helpers;

public static class CacheKeyHelper
{
    public static string QueueKey(string nameSpace, string queueName) => $"{nameSpace}/{queueName}/active";
    public static string DeadLetterQueueKey(string nameSpace, string queueName) => $"{nameSpace}/{queueName}/deadletter";

    public static string Queues = "queues";
    public static string Topics = "topics";
}