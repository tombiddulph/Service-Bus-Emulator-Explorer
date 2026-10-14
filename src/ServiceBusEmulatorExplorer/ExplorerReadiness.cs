using System.Net.Sockets;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer;

public interface IAmqpTransportProbe
{
    Task<bool> CheckAsync(CancellationToken cancellationToken);
}

public sealed class AmqpTransportProbe(ServiceBusConfig config) : IAmqpTransportProbe
{
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        var endpoint = ServiceBusConnectionStringProperties.Parse(config.ConnectionString).Endpoint;
        using var socket = new TcpClient();
        await socket.ConnectAsync(endpoint.Host, endpoint.IsDefaultPort ? 5672 : endpoint.Port, cancellationToken);
        return true;
    }
}

public sealed class ExplorerReadiness(ServiceBusAdministrationClient administration, IAmqpTransportProbe amqp)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionInfo? _last;

    public async Task<ConnectionInfo> CheckAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_last is not null && DateTimeOffset.UtcNow - _last.CheckedAt < TimeSpan.FromSeconds(5)) return _last;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var adminTask = CheckAdministrationAsync(deadline.Token);
            var transportTask = CheckTransportAsync(deadline.Token);
            await Task.WhenAll(adminTask, transportTask);
            cancellationToken.ThrowIfCancellationRequested();
            _last = new ConnectionInfo(adminTask.Result, transportTask.Result, DateTimeOffset.UtcNow);
            return _last;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> CheckAdministrationAsync(CancellationToken token)
    {
        try
        {
            await using var queues = administration.GetQueuesRuntimePropertiesAsync(token).GetAsyncEnumerator(token);
            await queues.MoveNextAsync(); // A bounded, non-destructive administration request; empty namespaces are healthy.
            return true;
        }
        catch (Exception) { return false; }
    }

    private async Task<bool> CheckTransportAsync(CancellationToken token)
    {
        try { return await amqp.CheckAsync(token); }
        catch (Exception) { return false; }
    }
}
