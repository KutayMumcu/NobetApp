using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using NobetApp.Api.Models;
using NobetApp.Api.DTO;
using NobetApp.Api.Data;
using NobetApp.Api.Services;

namespace NobetApp.Api.Controllers
{
    [ApiController]
    [Route("api/leave-requests")]
    [Authorize]
    public class LeaveRequestsController : ControllerBase
    {
        private readonly ShiftMateContext _context;
        private readonly LeaveRequestCleanupService _cleanupService;
        private readonly ILogger<LeaveRequestsController> _logger;

        public LeaveRequestsController(
            ShiftMateContext context,
            LeaveRequestCleanupService cleanupService,
            ILogger<LeaveRequestsController> logger)
        {
            _context = context;
            _cleanupService = cleanupService;
            _logger = logger;
        }

        // GET: api/leave-requests
        [HttpGet]
        public async Task<ActionResult<IEnumerable<LeaveRequestDto>>> GetAllLeaveRequests()
        {
            try
            {
                // Önce geçmiş talepleri temizle
                await _cleanupService.CancelExpiredPendingRequestsAsync();

                var requests = await _context.LeaveRequests
                    .Include(lr => lr.User)
                    .OrderByDescending(lr => lr.CreatedAt)
                    .ToListAsync();

                var requestDtos = requests.Select(lr => new LeaveRequestDto
                {
                    Id = lr.LeaveRequestID,
                    UserId = lr.UserId,
                    FullName = lr.User?.FullName ?? "",
                    Username = lr.User?.UserName ?? "",
                    StartDate = lr.LeaveDate.ToString("yyyy-MM-dd"),
                    EndDate = lr.EndDate.ToString("yyyy-MM-dd"),
                    Reason = lr.Note,
                    Status = lr.Status.ToString().ToLower(),
                    RequestDate = lr.CreatedAt.ToString("yyyy-MM-dd"),
                    AdminResponse = null,
                    ProcessedDate = lr.ApprovedAt?.ToString("yyyy-MM-dd")
                }).ToList();

                return Ok(requestDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talepleri yüklenirken hata oluştu.");
                return StatusCode(500, new { message = "İzin talepleri yüklenirken hata oluştu.", error = ex.Message });
            }
        }

        // GET: api/leave-requests/my-requests
        [HttpGet("my-requests")]
        public async Task<ActionResult<IEnumerable<LeaveRequestDto>>> GetMyLeaveRequests()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Kullanıcı kimliği bulunamadı." });
                }

                // Önce bu kullanıcının geçmiş taleplerini temizle
                await _cleanupService.CancelExpiredPendingRequestsForUserAsync(userId);

                var requests = await _context.LeaveRequests
                    .Include(lr => lr.User)
                    .Where(lr => lr.UserId == userId)
                    .OrderByDescending(lr => lr.CreatedAt)
                    .ToListAsync();

                var requestDtos = requests.Select(lr => new LeaveRequestDto
                {
                    Id = lr.LeaveRequestID,
                    UserId = lr.UserId,
                    FullName = lr.User?.FullName ?? "",
                    Username = lr.User?.UserName ?? "",
                    StartDate = lr.LeaveDate.ToString("yyyy-MM-dd"),
                    EndDate = lr.EndDate.ToString("yyyy-MM-dd"),
                    Reason = lr.Note,
                    Status = lr.Status.ToString().ToLower(),
                    RequestDate = lr.CreatedAt.ToString("yyyy-MM-dd"),
                    AdminResponse = null,
                    ProcessedDate = lr.ApprovedAt?.ToString("yyyy-MM-dd")
                }).ToList();

                return Ok(requestDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talepleri yüklenirken hata oluştu.");
                return StatusCode(500, new { message = "İzin talepleri yüklenirken hata oluştu.", error = ex.Message });
            }
        }

        // POST: api/leave-requests
        [HttpPost]
        public async Task<ActionResult<LeaveRequestDto>> CreateLeaveRequest([FromBody] CreateLeaveRequestDto request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Kullanıcı kimliği bulunamadı." });
                }

                // Validate date
                if (!DateTime.TryParse(request.StartDate, out var startDate))
                    return BadRequest(new { message = "Geçersiz başlangıç tarihi." });

                if (!DateTime.TryParse(request.EndDate, out var endDate))
                    endDate = startDate;

                // Geçmiş tarih kontrolü
                if (startDate < DateTime.Today)
                {
                    return BadRequest(new { message = "Geçmiş tarih için izin talebi oluşturamazsınız." });
                }

                // Önce bu kullanıcının geçmiş taleplerini temizle
                await _cleanupService.CancelExpiredPendingRequestsForUserAsync(userId);

                // Check for existing request on the same date
                var hasExistingRequest = await _context.LeaveRequests
                    .Where(lr => lr.UserId == userId &&
                                lr.LeaveDate.Date == startDate.Date &&
                                lr.Status != LeaveStatus.Rejected &&
                                lr.Status != LeaveStatus.Canceled)
                    .AnyAsync();

                if (hasExistingRequest)
                {
                    return BadRequest(new { message = "Bu tarih için zaten bir izin talebiniz bulunmaktadır." });
                }

                var leaveRequest = new LeaveRequest
                {
                    UserId = userId,
                    LeaveDate = startDate,
                    EndDate = endDate,
                    Note = request.Reason?.Trim(),
                    Status = LeaveStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _context.LeaveRequests.Add(leaveRequest);
                await _context.SaveChangesAsync();

                // Return created request with user info
                var createdRequest = await _context.LeaveRequests
                    .Include(lr => lr.User)
                    .Where(lr => lr.LeaveRequestID == leaveRequest.LeaveRequestID)
                    .FirstOrDefaultAsync();

                if (createdRequest == null)
                {
                    return StatusCode(500, new { message = "Oluşturulan talep bulunamadı." });
                }

                var createdDto = new LeaveRequestDto
                {
                    Id = createdRequest.LeaveRequestID,
                    UserId = createdRequest.UserId,
                    FullName = createdRequest.User?.FullName ?? "",
                    Username = createdRequest.User?.UserName ?? "",
                    StartDate = createdRequest.LeaveDate.ToString("yyyy-MM-dd"),
                    EndDate = createdRequest.EndDate.ToString("yyyy-MM-dd"),
                    Reason = createdRequest.Note,
                    Status = createdRequest.Status.ToString().ToLower(),
                    RequestDate = createdRequest.CreatedAt.ToString("yyyy-MM-dd"),
                    AdminResponse = null,
                    ProcessedDate = createdRequest.ApprovedAt?.ToString("yyyy-MM-dd")
                };

                return CreatedAtAction(nameof(GetLeaveRequest), new { id = leaveRequest.LeaveRequestID }, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi oluşturulurken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi oluşturulurken hata oluştu.", error = ex.Message });
            }
        }

        // GET: api/leave-requests/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<LeaveRequestDto>> GetLeaveRequest(int id)
        {
            try
            {
                var request = await _context.LeaveRequests
                    .Include(lr => lr.User)
                    .Where(lr => lr.LeaveRequestID == id)
                    .FirstOrDefaultAsync();

                if (request == null)
                {
                    return NotFound(new { message = "İzin talebi bulunamadı." });
                }

                var requestDto = new LeaveRequestDto
                {
                    Id = request.LeaveRequestID,
                    UserId = request.UserId,
                    FullName = request.User?.FullName ?? "",
                    Username = request.User?.UserName ?? "",
                    StartDate = request.LeaveDate.ToString("yyyy-MM-dd"),
                    EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                    Reason = request.Note,
                    Status = request.Status.ToString().ToLower(),
                    RequestDate = request.CreatedAt.ToString("yyyy-MM-dd"),
                    AdminResponse = null,
                    ProcessedDate = request.ApprovedAt?.ToString("yyyy-MM-dd")
                };

                return Ok(requestDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi yüklenirken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi yüklenirken hata oluştu.", error = ex.Message });
            }
        }

        // PUT: api/leave-requests/{id}/approve
        // PUT: api/leave-requests/{id}/approve
        [HttpPut("{id}/approve")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> ApproveLeaveRequest(int id, [FromBody] AdminResponseDto response)
        {
            try
            {
                var leaveRequest = await _context.LeaveRequests.FindAsync(id);
                if (leaveRequest == null)
                {
                    return NotFound(new { message = "İzin talebi bulunamadı." });
                }

                if (leaveRequest.Status != LeaveStatus.Pending)
                {
                    return BadRequest(new { message = "Bu izin talebi zaten işlenmiş." });
                }

                // Geçmiş tarih kontrolü
                if (leaveRequest.LeaveDate < DateTime.Today)
                {
                    return BadRequest(new { message = "Geçmiş tarihli izin talebi onaylanamaz." });
                }

                var adminUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                leaveRequest.Status = LeaveStatus.Approved;
                leaveRequest.ApprovedByUserId = adminUserId;
                leaveRequest.ApprovedAt = DateTime.UtcNow;

                // Önce izin talebini kaydet
                await _context.SaveChangesAsync();

                _logger.LogInformation("İzin talebi onaylandı: ID={RequestId}, UserId={UserId}, LeaveDate={LeaveDate}-{EndDate}",
                    id, leaveRequest.UserId, leaveRequest.LeaveDate, leaveRequest.EndDate);

                // Sonra çakışmaları çözümle (bu kendi SaveChanges'ini çağırır)
                await handleLeaveConflicts();

                return Ok(new { message = "İzin talebi onaylandı." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi onaylanırken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi onaylanırken hata oluştu.", error = ex.Message });
            }
        }

        private async Task handleLeaveConflicts()
        {
            try
            {
                // Mevcut nöbet programını al
                var schedules = await _context.DutySchedules
                    .OrderBy(s => s.Date)
                    .ToListAsync();

                if (!schedules.Any())
                {
                    _logger.LogInformation("Nöbet programı bulunamadı, çakışma kontrolü yapılmadı.");
                    return;
                }

                // Onaylanmış izin taleplerini al
                var approvedLeaves = await _context.LeaveRequests
                    .Where(l => l.Status == LeaveStatus.Approved)
                    .Include(l => l.User)
                    .ToListAsync();

                // Sistem kullanıcılarını departmanlara göre grupla
                var users = await _context.Users.ToListAsync();
                var configUsers = users.Where(u => u.Departmant.ToLower() == "konfigurasyon").ToList();
                var izlemeUsers = users.Where(u => u.Departmant.ToLower() == "izleme").ToList();

                // Yardımcı fonksiyon: Belirli tarihte kullanıcı izinli mi?
                bool IsOnLeave(string? fullName, DateTime dutyDate)
                {
                    if (string.IsNullOrWhiteSpace(fullName))
                        return false;

                    // Nöbet haftasının başlangıç ve bitiş tarihlerini hesapla
                    var dutyStartDate = dutyDate; // Nöbet haftasının başlangıcı
                    var dutyEndDate = dutyDate.AddDays(6); // Nöbet haftasının bitişi

                    return approvedLeaves.Any(l =>
                        l.User != null &&
                        l.User.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase) &&
                        // İzin tarihi ile nöbet haftası kesişiyor mu?
                        l.LeaveDate <= dutyEndDate && l.EndDate >= dutyStartDate);
                }

                var configNames = configUsers.Select(u => u.FullName).ToList();
                var izlemeNames = izlemeUsers.Select(u => u.FullName).ToList();

                var conflictLog = new List<string>();
                bool hasChanges = false;

                foreach (var schedule in schedules)
                {
                    var date = schedule.Date;
                    bool scheduleChanged = false;

                    _logger.LogInformation("Checking schedule for date: {Date}, Department: {Department}",
                        date, schedule.Department);

                    if (schedule.Department.ToLower() == "konfigurasyon")
                    {
                        // PRIMARY kontrolü
                        if (!string.IsNullOrWhiteSpace(schedule.Primary) && IsOnLeave(schedule.Primary, date))
                        {
                            _logger.LogInformation("Primary {Primary} is on leave for date {Date}",
                                schedule.Primary, date);

                            var available = configNames
                                .Where(name => !IsOnLeave(name, date))
                                .Where(name => !name.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                                .FirstOrDefault();

                            if (available != null)
                            {
                                var oldPrimary = schedule.Primary;
                                schedule.Primary = available;
                                scheduleChanged = true;
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Primary değiştirildi: {oldPrimary} → {available} (izin nedeniyle)");
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Primary için uygun kullanıcı bulunamadı: {schedule.Primary}");
                            }
                        }

                        // BACKUP kontrolü
                        if (!string.IsNullOrWhiteSpace(schedule.Backup) && IsOnLeave(schedule.Backup, date))
                        {
                            _logger.LogInformation("Backup {Backup} is on leave for date {Date}",
                                schedule.Backup, date);

                            var available = configNames
                                .Where(name => !IsOnLeave(name, date))
                                .Where(name => !name.Equals(schedule.Primary, StringComparison.OrdinalIgnoreCase))
                                .FirstOrDefault();

                            if (available != null)
                            {
                                var oldBackup = schedule.Backup;
                                schedule.Backup = available;
                                scheduleChanged = true;
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Backup değiştirildi: {oldBackup} → {available} (izin nedeniyle)");
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Backup için uygun kullanıcı bulunamadı: {schedule.Backup}");
                            }
                        }
                    }
                    else if (schedule.Department.ToLower() == "izleme")
                    {
                        // KANBAN kontrolü
                        if (!string.IsNullOrWhiteSpace(schedule.Kanban) && IsOnLeave(schedule.Kanban, date))
                        {
                            _logger.LogInformation("Kanban {Kanban} is on leave for date {Date}",
                                schedule.Kanban, date);

                            var available = izlemeNames
                                .Where(name => !IsOnLeave(name, date))
                                .Where(name => !name.Equals(schedule.Monitoring, StringComparison.OrdinalIgnoreCase))
                                .Where(name => !name.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                                .FirstOrDefault();

                            if (available != null)
                            {
                                var oldKanban = schedule.Kanban;
                                schedule.Kanban = available;
                                scheduleChanged = true;
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Kanban değiştirildi: {oldKanban} → {available} (izin nedeniyle)");
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Kanban için uygun kullanıcı bulunamadı: {schedule.Kanban}");
                            }
                        }

                        // MONITORING kontrolü
                        if (!string.IsNullOrWhiteSpace(schedule.Monitoring) && IsOnLeave(schedule.Monitoring, date))
                        {
                            _logger.LogInformation("Monitoring {Monitoring} is on leave for date {Date}",
                                schedule.Monitoring, date);

                            var available = izlemeNames
                                .Where(name => !IsOnLeave(name, date))
                                .Where(name => !name.Equals(schedule.Kanban, StringComparison.OrdinalIgnoreCase))
                                .Where(name => !name.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                                .FirstOrDefault();

                            if (available != null)
                            {
                                var oldMonitoring = schedule.Monitoring;
                                schedule.Monitoring = available;
                                scheduleChanged = true;
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Monitoring değiştirildi: {oldMonitoring} → {available} (izin nedeniyle)");
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Monitoring için uygun kullanıcı bulunamadı: {schedule.Monitoring}");
                            }
                        }

                        // BACKUP kontrolü
                        if (!string.IsNullOrWhiteSpace(schedule.Backup) && IsOnLeave(schedule.Backup, date))
                        {
                            _logger.LogInformation("Backup {Backup} is on leave for date {Date}",
                                schedule.Backup, date);

                            var available = izlemeNames
                                .Where(name => !IsOnLeave(name, date))
                                .Where(name => !name.Equals(schedule.Kanban, StringComparison.OrdinalIgnoreCase))
                                .Where(name => !name.Equals(schedule.Monitoring, StringComparison.OrdinalIgnoreCase))
                                .FirstOrDefault();

                            if (available != null)
                            {
                                var oldBackup = schedule.Backup;
                                schedule.Backup = available;
                                scheduleChanged = true;
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Backup değiştirildi: {oldBackup} → {available} (izin nedeniyle)");
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Backup için uygun kullanıcı bulunamadı: {schedule.Backup}");
                            }
                        }
                    }

                    if (scheduleChanged)
                    {
                        hasChanges = true;
                        _logger.LogInformation("Schedule changed for date {Date}", date);
                    }
                }

                // Değişiklikleri kaydet
                if (hasChanges)
                {
                    _logger.LogInformation("Saving {ChangeCount} schedule changes", conflictLog.Count);
                    // Update yerine direkt SaveChanges çağır
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("İzin onayı nedeniyle nöbet programında {ChangeCount} değişiklik yapıldı.", conflictLog.Count);
                }
                else
                {
                    _logger.LogInformation("İzin onayı sonrası nöbet programında değişiklik yapılmadı.");
                }

                // Çakışma loglarını kaydet
                if (conflictLog.Any())
                {
                    _logger.LogInformation("İzin çakışması çözümleme raporu:");
                    foreach (var log in conflictLog)
                    {
                        _logger.LogInformation(log);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin çakışması çözümlenirken hata oluştu.");
                throw;
            }
        }

        // PUT: api/leave-requests/{id}/reject
        [HttpPut("{id}/reject")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> RejectLeaveRequest(int id, [FromBody] AdminResponseDto response)
        {
            try
            {
                var leaveRequest = await _context.LeaveRequests.FindAsync(id);
                if (leaveRequest == null)
                {
                    return NotFound(new { message = "İzin talebi bulunamadı." });
                }

                if (leaveRequest.Status != LeaveStatus.Pending)
                {
                    return BadRequest(new { message = "Bu izin talebi zaten işlenmiş." });
                }

                var adminUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                leaveRequest.Status = LeaveStatus.Rejected;
                leaveRequest.ApprovedByUserId = adminUserId;
                leaveRequest.ApprovedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(new { message = "İzin talebi reddedildi." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi reddedilirken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi reddedilirken hata oluştu.", error = ex.Message });
            }
        }

        // PUT: api/leave-requests/{id}/cancel
        [HttpPut("{id}/cancel")]
        public async Task<IActionResult> CancelLeaveRequest(int id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Kullanıcı kimliği bulunamadı." });
                }

                var leaveRequest = await _context.LeaveRequests.FindAsync(id);
                if (leaveRequest == null)
                {
                    return NotFound(new { message = "İzin talebi bulunamadı." });
                }

                // Only allow cancellation of own requests
                if (leaveRequest.UserId != userId && !User.IsInRole("admin"))
                {
                    return Forbid();
                }

                if (leaveRequest.Status != LeaveStatus.Pending)
                {
                    return BadRequest(new { message = "Sadece bekleyen izin talepleri iptal edilebilir." });
                }

                leaveRequest.Status = LeaveStatus.Canceled;
                leaveRequest.ApprovedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "İzin talebi iptal edildi." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi iptal edilirken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi iptal edilirken hata oluştu.", error = ex.Message });
            }
        }

        // DELETE: api/leave-requests/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLeaveRequest(int id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Kullanıcı kimliği bulunamadı." });
                }

                var leaveRequest = await _context.LeaveRequests.FindAsync(id);
                if (leaveRequest == null)
                {
                    return NotFound(new { message = "İzin talebi bulunamadı." });
                }

                // Only allow deletion of own requests and only if pending or canceled
                if (leaveRequest.UserId != userId && !User.IsInRole("admin"))
                {
                    return Forbid();
                }

                if (leaveRequest.Status != LeaveStatus.Pending && leaveRequest.Status != LeaveStatus.Canceled)
                {
                    return BadRequest(new { message = "Sadece bekleyen veya iptal edilmiş izin talepleri silinebilir." });
                }

                _context.LeaveRequests.Remove(leaveRequest);
                await _context.SaveChangesAsync();

                return Ok(new { message = "İzin talebi silindi." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi silinirken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi silinirken hata oluştu.", error = ex.Message });
            }
        }

        // PUT: api/leave-requests/{id}/cancel-approved (YENİ ENDPOINT)
        [HttpPut("{id}/cancel-approved")]
        [Authorize]
        public async Task<IActionResult> CancelApprovedLeaveRequest(int id, [FromBody] AdminResponseDto response)
        {
            try
            {
                var leaveRequest = await _context.LeaveRequests.FindAsync(id);
                if (leaveRequest == null)
                {
                    return NotFound(new { message = "İzin talebi bulunamadı." });
                }

                if (leaveRequest.Status != LeaveStatus.Approved)
                {
                    return BadRequest(new { message = "Sadece onaylanmış izin talepleri iptal edilebilir." });
                }

                var adminUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // İzin kaydını veritabanından tamamen sil
                _context.LeaveRequests.Remove(leaveRequest);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin {AdminId} tarafından onaylanmış izin talebi silindi: ID={RequestId}, UserId={UserId}, LeaveDate={LeaveDate}",
                    adminUserId, id, leaveRequest.UserId, leaveRequest.LeaveDate);

                return Ok(new { message = "Onaylanmış izin talebi başarıyla iptal edildi ve veritabanından silindi." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Onaylanmış izin talebi iptal edilirken hata oluştu.");
                return StatusCode(500, new { message = "Onaylanmış izin talebi iptal edilirken hata oluştu.", error = ex.Message });
            }
        }

        // GET: api/leave-requests/cleanup/status
        [HttpGet("cleanup/status")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> GetCleanupStatus()
        {
            try
            {
                var expiredCount = await _cleanupService.GetExpiredPendingRequestsCountAsync();
                return Ok(new { expiredPendingRequests = expiredCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup status alınırken hata oluştu.");
                return StatusCode(500, new { message = "Cleanup status alınırken hata oluştu.", error = ex.Message });
            }
        }

        // POST: api/leave-requests/cleanup/run
        [HttpPost("cleanup/run")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> RunCleanup()
        {
            try
            {
                var canceledCount = await _cleanupService.CancelExpiredPendingRequestsAsync();
                return Ok(new
                {
                    message = $"{canceledCount} adet geçmiş izin talebi iptal edildi.",
                    canceledCount = canceledCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manuel cleanup çalıştırılırken hata oluştu.");
                return StatusCode(500, new { message = "Manuel cleanup çalıştırılırken hata oluştu.", error = ex.Message });
            }
        }
    }
}