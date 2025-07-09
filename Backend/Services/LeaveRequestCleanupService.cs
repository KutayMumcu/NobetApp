using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Data;
using NobetApp.Api.Models;

namespace NobetApp.Api.Services
{
    public class LeaveRequestCleanupService
    {
        private readonly ShiftMateContext _context;
        private readonly ILogger<LeaveRequestCleanupService> _logger;

        public LeaveRequestCleanupService(ShiftMateContext context, ILogger<LeaveRequestCleanupService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Geçmiş tarihteki onaylanmamış izin taleplerini iptal eder
        /// </summary>
        public async Task<int> CancelExpiredPendingRequestsAsync()
        {
            try
            {
                var today = DateTime.Today;

                // Geçmiş tarihteki pending durumundaki izin taleplerini bul
                var expiredRequests = await _context.LeaveRequests
                    .Where(lr => lr.LeaveDate < today && lr.Status == LeaveStatus.Pending)
                    .ToListAsync();

                if (!expiredRequests.Any())
                {
                    _logger.LogInformation("Iptal edilecek geçmiş izin talebi bulunamadı.");
                    return 0;
                }

                // Tüm expired requestleri canceled olarak işaretle
                foreach (var request in expiredRequests)
                {
                    request.Status = LeaveStatus.Canceled;
                    request.ApprovedAt = DateTime.UtcNow;

                    _logger.LogInformation("Geçmiş izin talebi iptal edildi: ID={RequestId}, UserId={UserId}, LeaveDate={LeaveDate}",
                        request.LeaveRequestID, request.UserId, request.LeaveDate);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("{Count} adet geçmiş izin talebi iptal edildi.", expiredRequests.Count);
                return expiredRequests.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Geçmiş izin talepleri iptal edilirken hata oluştu.");
                throw;
            }
        }

        /// <summary>
        /// Belirtilen kullanıcının geçmiş tarihteki onaylanmamış izin taleplerini iptal eder
        /// </summary>
        public async Task<int> CancelExpiredPendingRequestsForUserAsync(string userId)
        {
            try
            {
                var today = DateTime.Today;

                var expiredRequests = await _context.LeaveRequests
                    .Where(lr => lr.UserId == userId &&
                                lr.LeaveDate < today &&
                                lr.Status == LeaveStatus.Pending)
                    .ToListAsync();

                if (!expiredRequests.Any())
                {
                    return 0;
                }

                foreach (var request in expiredRequests)
                {
                    request.Status = LeaveStatus.Canceled;
                    request.ApprovedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Kullanıcı {UserId} için {Count} adet geçmiş izin talebi iptal edildi.",
                    userId, expiredRequests.Count);

                return expiredRequests.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kullanıcı {UserId} için geçmiş izin talepleri iptal edilirken hata oluştu.", userId);
                throw;
            }
        }

        /// <summary>
        /// İsteğe bağlı: Geçmiş tarihteki iptal edilmiş izin taleplerini tamamen siler
        /// </summary>
        public async Task<int> DeleteCanceledExpiredRequestsAsync(int olderThanDays = 30)
        {
            try
            {
                var cutoffDate = DateTime.Today.AddDays(-olderThanDays);

                var requestsToDelete = await _context.LeaveRequests
                    .Where(lr => lr.LeaveDate < cutoffDate &&
                                lr.Status == LeaveStatus.Canceled)
                    .ToListAsync();

                if (!requestsToDelete.Any())
                {
                    _logger.LogInformation("Silinecek eski iptal edilmiş izin talebi bulunamadı.");
                    return 0;
                }

                _context.LeaveRequests.RemoveRange(requestsToDelete);
                await _context.SaveChangesAsync();

                _logger.LogInformation("{Count} adet eski iptal edilmiş izin talebi silindi.", requestsToDelete.Count);
                return requestsToDelete.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Eski iptal edilmiş izin talepleri silinirken hata oluştu.");
                throw;
            }
        }

        /// <summary>
        /// Geçmiş tarihteki onaylanmamış izin taleplerinin sayısını döndürür
        /// </summary>
        public async Task<int> GetExpiredPendingRequestsCountAsync()
        {
            var today = DateTime.Today;
            return await _context.LeaveRequests
                .CountAsync(lr => lr.LeaveDate < today && lr.Status == LeaveStatus.Pending);
        }

        /// <summary>
        /// Belirtilen kullanıcının geçmiş tarihteki onaylanmamış izin taleplerinin sayısını döndürür
        /// </summary>
        public async Task<int> GetExpiredPendingRequestsCountForUserAsync(string userId)
        {
            var today = DateTime.Today;
            return await _context.LeaveRequests
                .CountAsync(lr => lr.UserId == userId &&
                               lr.LeaveDate < today &&
                               lr.Status == LeaveStatus.Pending);
        }
    }
}