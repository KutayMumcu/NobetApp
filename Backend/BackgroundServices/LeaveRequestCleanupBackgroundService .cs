using NobetApp.Api.Services;

namespace NobetApp.Api.BackgroundServices
{
    public class LeaveRequestCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LeaveRequestCleanupBackgroundService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromHours(1); // Her saat kontrol et

        public LeaveRequestCleanupBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<LeaveRequestCleanupBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var cleanupService = scope.ServiceProvider.GetRequiredService<LeaveRequestCleanupService>();

                    var canceledCount = await cleanupService.CancelExpiredPendingRequestsAsync();

                    if (canceledCount > 0)
                    {
                        _logger.LogInformation("Background service: {Count} adet geçmiş izin talebi iptal edildi.", canceledCount);
                    }

                    // İsteğe bağlı: 30 günden eski iptal edilmiş talepleri sil
                    // var deletedCount = await cleanupService.DeleteCanceledExpiredRequestsAsync(30);

                    await Task.Delay(_period, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background service çalışırken hata oluştu.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Hata durumunda 5 dakika bekle
                }
            }
        }
    }
}