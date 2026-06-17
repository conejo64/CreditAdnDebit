namespace CardVault.Application.Ports;

public interface IAuthDecisionPublisher
{
    Task PublishAuthResponseAsync(string key, object payload, CancellationToken ct);
}
