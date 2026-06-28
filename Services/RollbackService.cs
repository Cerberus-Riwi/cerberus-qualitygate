using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        var serviceAccountToken = _settings.KubernetesServiceAccountToken;
        if (string.IsNullOrWhiteSpace(serviceAccountToken))
        {
            logger.LogError("Kubernetes token is not configured. Rollback cannot be executed: {Command}", commandPreview);
            return new RollbackResult(deploymentId, false, "Rollback failed: Kubernetes service account token is not configured.", timestamp);
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceAccountToken);

        var namespaceName = Uri.EscapeDataString(_settings.KubernetesNamespace);
        var deploymentName = Uri.EscapeDataString(deploymentId);
        var deploymentUri = $"/apis/apps/v1/namespaces/{namespaceName}/deployments/{deploymentName}";

        var deployment = await GetJsonAsync(deploymentUri, cancellationToken);
        if (deployment is null)
        {
            return new RollbackResult(deploymentId, false, "Rollback failed: deployment could not be read from Kubernetes.", timestamp);
        }

        var previousTemplate = await GetPreviousReplicaSetTemplateAsync(deployment, namespaceName, cancellationToken);
        if (previousTemplate is null)
        {
            logger.LogError("No previous ReplicaSet template found for deployment {DeploymentId}", deploymentId);
            return new RollbackResult(deploymentId, false, "Rollback failed: previous ReplicaSet was not found.", timestamp);
        }

        var payload = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["annotations"] = new JsonObject
                {
                    ["cerberus.io/rollback-requested-at"] = timestamp.ToString("O")
                }
            },
            ["spec"] = new JsonObject
            {
                ["template"] = previousTemplate.DeepClone()
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, deploymentUri);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/strategic-merge-patch+json");

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

    private async Task<JsonNode?> GetJsonAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Kubernetes GET failed for {RequestUri}. Status: {StatusCode}. Body: {Body}",
                requestUri,
                response.StatusCode,
                responseBody);

            return null;
        }

        return JsonNode.Parse(responseBody);
    }

    private async Task<JsonNode?> GetPreviousReplicaSetTemplateAsync(JsonNode deployment, string namespaceName, CancellationToken cancellationToken)
    {
        var currentRevision = int.TryParse(
            deployment["metadata"]?["annotations"]?["deployment.kubernetes.io/revision"]?.GetValue<string>(),
            out var revision)
            ? revision
            : int.MaxValue;

        var deploymentUid = deployment["metadata"]?["uid"]?.GetValue<string>();
        var labelSelector = BuildLabelSelector(deployment["spec"]?["selector"]?["matchLabels"]?.AsObject());
        if (string.IsNullOrWhiteSpace(labelSelector))
        {
            return null;
        }

        var replicaSetsUri = $"/apis/apps/v1/namespaces/{namespaceName}/replicasets?labelSelector={Uri.EscapeDataString(labelSelector)}";
        var replicaSets = await GetJsonAsync(replicaSetsUri, cancellationToken);
        var items = replicaSets?["items"]?.AsArray();
        if (items is null)
        {
            return null;
        }

        return items
            .Where(item => item is not null && IsOwnedByDeployment(item, deploymentUid))
            .Select(item => new
            {
                Revision = ReadRevision(item!),
                Template = item!["spec"]?["template"]
            })
            .Where(item => item.Template is not null && item.Revision < currentRevision)
            .OrderByDescending(item => item.Revision)
            .FirstOrDefault()
            ?.Template;
    }

    private static string BuildLabelSelector(JsonObject? matchLabels)
    {
        if (matchLabels is null)
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            matchLabels.Select(label => $"{label.Key}={label.Value?.GetValue<string>()}"));
    }

    private static bool IsOwnedByDeployment(JsonNode replicaSet, string? deploymentUid)
    {
        if (string.IsNullOrWhiteSpace(deploymentUid))
        {
            return true;
        }

        var ownerReferences = replicaSet["metadata"]?["ownerReferences"]?.AsArray();
        return ownerReferences?.Any(owner =>
            owner?["kind"]?.GetValue<string>() == "Deployment"
            && owner["uid"]?.GetValue<string>() == deploymentUid) == true;
    }

    private static int ReadRevision(JsonNode replicaSet)
    {
        return int.TryParse(
            replicaSet["metadata"]?["annotations"]?["deployment.kubernetes.io/revision"]?.GetValue<string>(),
            out var revision)
            ? revision
            : 0;
    }
}
