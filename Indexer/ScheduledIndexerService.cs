using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Indexer;

/// <summary>
/// Background service that runs the indexer on a configurable schedule.
/// </summary>
public sealed class ScheduledIndexerService : BackgroundService
{
    private readonly MultiProviderIndexerService _indexer;
    private readonly ILogger<ScheduledIndexerService> _logger;
    private readonly TimeSpan _interval;

    public ScheduledIndexerService(
        MultiProviderIndexerService indexer,
        ILogger<ScheduledIndexerService> logger,
        IConfiguration configuration)
    {
        _indexer = indexer;
        _logger = logger;
        
        // Read schedule from config (default: every 6 hours)
        var intervalHours = configuration.GetValue<int?>("Scheduler:IntervalHours") ?? 6;
        _interval = TimeSpan.FromHours(intervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled indexer started. Running every {Interval}", _interval);

        // Optional: Run immediately on startup
        var runOnStartup = true; // Could make this configurable
        if (!runOnStartup)
        {
            await Task.Delay(_interval, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Starting scheduled indexing run at {Time}", startTime);

            try
            {
                var exitCode = await _indexer.ExecuteAsync(stoppingToken);
                
                if (exitCode == 0)
                {
                    _logger.LogInformation("Indexing completed successfully");
                }
                else
                {
                    _logger.LogWarning("Indexing completed with exit code {ExitCode}", exitCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Indexing run failed with exception");
            }

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation("Indexing run completed in {Duration}. Next run in {Interval}", elapsed, _interval);

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Scheduled indexer stopped");
    }
}
