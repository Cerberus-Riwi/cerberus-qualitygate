using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Data;
using RabbitMQ.Client;

namespace QualityGateService.Controllers;

[ApiController]
[Route("api")]
public sealed class HealthController(
    AppDbContext dbContext,
    IOptions<QualityGateSettings> options,
    ILogger<HealthController> logger) : ControllerBase
{
    /// <summary>
    /// Kubernetes liveness probe.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "quality-gate",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Kubernetes readiness probe. Checks PostgreSQL and RabbitMQ connectivity.
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not ready", reason = "PostgreSQL unavailable" });
            }

            var factory = new ConnectionFactory { HostName = options.Value.RabbitMQHost };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            return Ok(new { status = "ready" });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Readiness probe failed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not ready", reason = ex.Message });
        }
    }
}
