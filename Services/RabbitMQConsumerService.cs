using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace QualityGateService.Services;

public sealed class RabbitMQConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<QualityGateSettings> options,
    QualityGateAggregationService aggregationService,
    ILogger<RabbitMQConsumerService> logger) : BackgroundService
{
    private readonly QualityGateSettings _settings = options.Value;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _channelLock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.RabbitMQHost,
                Port = _settings.RabbitMQPort,
                UserName = _settings.RabbitMQUsername,
                Password = _settings.RabbitMQPassword,
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_settings.RabbitMQExchange, ExchangeType.Fanout, durable: true);
            _channel.ExchangeDeclare(_settings.RabbitMQResultsExchange, ExchangeType.Fanout, durable: true);
            _channel.QueueDeclare(_settings.RabbitMQQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(_settings.RabbitMQQueue, _settings.RabbitMQExchange, routingKey: string.Empty);
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += HandleMessageAsync;

            _channel.BasicConsume(_settings.RabbitMQQueue, autoAck: false, consumer);
            logger.LogInformation("RabbitMQ consumer started on queue {Queue}", _settings.RabbitMQQueue);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RabbitMQ consumer could not be started.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _settings.PendingScanTimeoutSeconds / 2)));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PublishExpiredScansAsync(stoppingToken);
        }
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var body = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            var scanEvent = JsonSerializer.Deserialize<ScanEvent>(body, _jsonOptions)
                ?? throw new InvalidOperationException("RabbitMQ message body could not be deserialized.");

            var pendingState = aggregationService.Add(scanEvent);
            if (pendingState.IsReady && pendingState.ScanEvent is not null)
            {
                await EvaluateAndPublishAsync(pendingState.ScanEvent);
            }
            else
            {
                logger.LogInformation(
                    "Scan {ScanId} is waiting for more scan-results. Current count: {ResultCount}",
                    pendingState.ScanId,
                    pendingState.ResultCount);
            }

            lock (_channelLock)
            {
                _channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process RabbitMQ quality gate message.");
            lock (_channelLock)
            {
                _channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
            }
        }
    }

    private async Task PublishExpiredScansAsync(CancellationToken cancellationToken)
    {
        foreach (var scanEvent in aggregationService.TakeExpired())
        {
            logger.LogWarning(
                "Scan {ScanId} reached aggregation timeout. Evaluating with {ResultCount} scan-result(s).",
                scanEvent.ScanId,
                scanEvent.Results.Count);

            await EvaluateAndPublishAsync(scanEvent, cancellationToken);
        }
    }

    private async Task EvaluateAndPublishAsync(ScanEvent scanEvent, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IQualityGateEvaluatorService>();
        var result = await evaluator.EvaluateAsync(scanEvent, cancellationToken);

        PublishResult(result);
    }

    private void PublishResult(QualityGateResult result)
    {
        if (_channel is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(result, _jsonOptions);
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";

        lock (_channelLock)
        {
            _channel.BasicPublish(
                exchange: _settings.RabbitMQResultsExchange,
                routingKey: string.Empty,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(payload));
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
