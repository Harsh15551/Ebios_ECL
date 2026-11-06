using Ebios.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ebios.Web.Services;

public class SftpAttendanceSchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SftpAttendanceSchedulerWorker> _logger;
    private DateTime? _lastExecutedDate = null;

    public SftpAttendanceSchedulerWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<SftpAttendanceSchedulerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SFTP Attendance Scheduler Background Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool isEnabled = bool.TryParse(_configuration["SftpServer:IsEnabled"] 
                                 ?? Environment.GetEnvironmentVariable("SFTP_ENABLED") 
                                 ?? "true", out bool en) && en;

                if (isEnabled)
                {
                    string scheduledTime = Environment.GetEnvironmentVariable("SFTP_SCHEDULE_TIME")
                                           ?? _configuration["SftpServer:DailyScheduledTime"]
                                           ?? "23:45";

                    DateTime now = DateTime.Now;
                    string currentTime = now.ToString("HH:mm");

                    // Trigger once per day when current time matches scheduled time
                    if (currentTime == scheduledTime && (_lastExecutedDate == null || _lastExecutedDate.Value.Date != now.Date))
                    {
                        _logger.LogInformation("SFTP Attendance Scheduled trigger firing for date {Date} at {Time}", 
                            now.Date.ToString("yyyy-MM-dd"), currentTime);

                        using var scope = _serviceProvider.CreateScope();
                        var sftpService = scope.ServiceProvider.GetRequiredService<ISftpAttendanceService>();

                        var result = await sftpService.ExportAndUploadDailyAttendanceAsync(
                            now.Date,
                            areaId: null,
                            subAreaId: null,
                            triggerType: "SCHEDULED_AUTO",
                            userId: null,
                            username: "system_scheduler",
                            officerName: "Automated Nightly Worker",
                            role: "System");
                        if (result.Success)
                        {
                            _lastExecutedDate = now.Date;
                            _logger.LogInformation("SFTP Daily Attendance scheduled sync succeeded: {Message}", result.Message);
                        }
                        else
                        {
                            _logger.LogWarning("SFTP Daily Attendance scheduled sync failed: {Message}", result.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SFTP Attendance Scheduler Background Worker loop.");
            }

            // Sleep 30 seconds before next check
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
