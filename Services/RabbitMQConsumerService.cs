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
    ILogger<RabbitMQConsumerService> logger) : BackgroundService
{
    private readonly QualityGateSettings _settings = options.Value;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private IConnection? _connection;
    private IModel? _channel;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.RabbitMQHost,
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

        return Task.CompletedTask;
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

            using var scope = scopeFactory.CreateScope();
            var evaluator = scope.ServiceProvider.GetRequiredService<IQualityGateEvaluatorService>();
            var result = await evaluator.EvaluateAsync(scanEvent);

            PublishResult(result);
            _channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process RabbitMQ quality gate message.");
            _channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
        }
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

        _channel.BasicPublish(
            exchange: _settings.RabbitMQResultsExchange,
            routingKey: string.Empty,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(payload));
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
