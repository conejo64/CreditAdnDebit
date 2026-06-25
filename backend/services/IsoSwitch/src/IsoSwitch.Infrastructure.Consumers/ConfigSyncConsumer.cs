using Confluent.Kafka;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IsoSwitch.Infrastructure.Consumers;

public sealed class ConfigSyncConsumer : BackgroundService
{
    private readonly ILogger<ConfigSyncConsumer> _logger;
    private readonly IServiceProvider _sp;
    private readonly string _bootstrapServers;
    private readonly string _groupId;
    private readonly string _clientId;

    public ConfigSyncConsumer(string bootstrapServers, string groupId, string clientId, IServiceProvider sp, ILogger<ConfigSyncConsumer> logger)
    {
        _bootstrapServers = bootstrapServers;
        _groupId = groupId;
        _clientId = clientId;
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var config = new ConsumerConfig { BootstrapServers = _bootstrapServers, GroupId = _groupId, ClientId = _clientId, AutoOffsetReset = AutoOffsetReset.Earliest };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(new[] { "cv.demo", "cv.routing.updated", "cv.card.status.changed", "cv.merchant.config.updated", "cv.catalog.country.upserted", "cv.catalog.binrange.upserted", "cv.catalog.cardproduct.upserted" });
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(stoppingToken);
                if (cr is null) continue;
                var topic = cr.Topic;
                var key = cr.Message.Key;
                var value = cr.Message.Value;
                _logger.LogInformation("IsoSwitch received topic={Topic} key={Key}", topic, key);
                if (topic == "cv.routing.updated")
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
                    var doc = JsonDocument.Parse(value);
                    var root = doc.RootElement;
                    var id = root.TryGetProperty("ruleId", out var rid) ? rid.GetGuid() : Guid.Empty;
                    if (id == Guid.Empty) continue;
                    var entity = await db.RoutingRulesCache.FindAsync(new object[] { id }, stoppingToken);
                    if (entity is null)
                    {
                        entity = new RoutingRuleCacheEntity { Id = id };
                        db.RoutingRulesCache.Add(entity);
                    }
                    entity.Priority = root.GetProperty("priority").GetInt32();
                    entity.BinStart = root.GetProperty("binStart").GetInt32();
                    entity.BinEnd = root.GetProperty("binEnd").GetInt32();
                    entity.ConnectorId = root.GetProperty("connectorId").GetString() ?? "SIMULATOR";
                    entity.Enabled = root.GetProperty("enabled").GetBoolean();
                    entity.UpdatedOn = root.GetProperty("updatedOn").GetDateTimeOffset();
                    await db.SaveChangesAsync(stoppingToken);
                }
            } catch (OperationCanceledException) {} catch (Exception ex) { _logger.LogError(ex, "ConfigSyncConsumer error"); }
        }
    }
}
