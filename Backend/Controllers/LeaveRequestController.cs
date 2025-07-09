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

                await _context.SaveChangesAsync();

                return Ok(new { message = "İzin talebi onaylandı." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İzin talebi onaylanırken hata oluştu.");
                return StatusCode(500, new { message = "İzin talebi onaylanırken hata oluştu.", error = ex.Message });
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