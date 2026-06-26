using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QualityGateService.Config;

namespace QualityGateService.Services;

public sealed class RollbackService(
    HttpClient httpClient,
    IOptions<QualityGateSettings> options,
    ILogger<RollbackService> logger) : IRollbackService
{
    private readonly QualityGateSettings _settings = options.Value;

    public async Task<RollbackResult> RollbackAsync(string deploymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deploymentId))
        {
            throw new ArgumentException("DeploymentId is required.", nameof(deploymentId));
        }

        var timestamp = DateTime.UtcNow;
        var commandPreview = $"kubectl rollout undo deployment/{deploymentId} --namespace={_settings.KubernetesNamespace}";

        if (string.IsNullOrWhiteSpace(_settings.KubernetesServiceAccountToken))
        {
            logger.LogWarning("Kubernetes token is not configured. Simulating rollback: {Command}", commandPreview);
            return new RollbackResult(deploymentId, true, $"Rollback simulated: {commandPreview}", timestamp);
        }

        var requestUri = $"/apis/apps/v1/namespaces/{Uri.EscapeDataString(_settings.KubernetesNamespace)}/deployments/{Uri.EscapeDataString(deploymentId)}";
        var payload = new
        {
            metadata = new
            {
                annotations = new Dictionary<string, string>
                {
                    ["cerberus.io/rollback-requested-at"] = timestamp.ToString("O")
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.KubernetesServiceAccountToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/strategic-merge-patch+json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Kubernetes rollback patch failed for deployment {DeploymentId}. Status: {StatusCode}. Body: {Body}",
                deploymentId,
                response.StatusCode,
                responseBody);

            return new RollbackResult(deploymentId, false, $"Rollback failed with status {(int)response.StatusCode}", timestamp);
        }

        logger.LogInformation("Kubernetes rollback patch accepted for deployment {DeploymentId}", deploymentId);
        return new RollbackResult(deploymentId, true, $"Rollback executed: {commandPreview}", timestamp);
    }
}
