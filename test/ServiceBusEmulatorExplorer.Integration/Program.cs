using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.ServiceBus;

// Deliberately separate from the fast fake-backed test suite. Only run against a disposable emulator.
var apiUrl = Environment.GetEnvironmentVariable("INTEGRATION_API_URL") ?? "http://localhost:8080";
var connection = Environment.GetEnvironmentVariable("INTEGRATION_SERVICEBUS_CONNECTION") ??
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
using var http = new HttpClient { BaseAddress = new Uri(apiUrl), Timeout = TimeSpan.FromSeconds(45) };
await using var bus = new ServiceBusClient(connection, new ServiceBusClientOptions
{
    RetryOptions = new ServiceBusRetryOptions { TryTimeout = TimeSpan.FromSeconds(10), MaxRetries = 2 }
});
var queue = "integration-" + Guid.NewGuid().ToString("N");
var topic = "integration-topic-" + Guid.NewGuid().ToString("N");
const string sub = "consumer";
var createdQueue = false;
var createdTopic = false;
try
{
    await Require(await http.GetAsync("/health"));
    await Require(await http.GetAsync("/")); // Exercises embedded frontend in the published AOT image.
    await Require(await http.GetAsync("/openapi/v1.json"));
    await Require(await http.PostAsJsonAsync("/api/queues", new { name = queue }));
    createdQueue = true;
    await Require(await http.PostAsJsonAsync($"/api/queues/{queue}/messages", new { body = "integration-message" }));
    await WaitForMessage($"/api/queues/{queue}/messages?mode=peek&state=active");

    await using (var receiver = bus.CreateReceiver(queue))
    {
        var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("No message received for dead-letter test.");
        await receiver.DeadLetterMessageAsync(message, "integration-test", "Testing recovery");
    }
    await WaitForMessage($"/api/queues/{queue}/messages?mode=peek&state=deadletter");
    await Require(await http.PostAsJsonAsync($"/api/deadletter/queue/{queue}/replay", new { removeFromDlq = true }));
    await WaitForMessage($"/api/queues/{queue}/messages?mode=peek&state=active");
    await Require(await http.PostAsync($"/api/queues/{queue}/purge", null));
    var empty = await http.GetFromJsonAsync<JsonElement>($"/api/queues/{queue}/messages?mode=peek&state=active");
    if (empty.GetProperty("items").GetArrayLength() != 0) throw new InvalidOperationException("Purge left active messages.");

    await Require(await http.PostAsJsonAsync("/api/topics", new { name = topic }));
    createdTopic = true;
    await Require(await http.PostAsJsonAsync($"/api/topics/{topic}/subscriptions", new { name = sub }));
    await Require(await http.PostAsJsonAsync($"/api/topics/{topic}/messages", new { body = "topic-message" }));
    await WaitForMessage($"/api/topics/{topic}/subscriptions/{sub}/messages?mode=peek&state=active");
    await Require(await http.DeleteAsync($"/api/topics/{topic}/subscriptions/{sub}"));
    Console.WriteLine("Published-container and real-emulator integration checks passed.");
}
finally
{
    if (createdQueue) await Require(await http.DeleteAsync($"/api/queues/{queue}"));
    if (createdTopic) await Require(await http.DeleteAsync($"/api/topics/{topic}"));
}

static async Task Require(HttpResponseMessage response)
{
    using (response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}

async Task WaitForMessage(string path)
{
    for (var attempt = 0; attempt < 20; attempt++)
    {
        var page = await http.GetFromJsonAsync<JsonElement>(path);
        if (page.GetProperty("items").GetArrayLength() > 0) return;
        await Task.Delay(500);
    }
    throw new InvalidOperationException("Timed out waiting for message: " + path);
}
