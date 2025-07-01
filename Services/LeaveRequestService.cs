using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Data;
using NobetApp.Api.Models;

namespace NobetApp.Api.Services
{
    public class LeaveRequestService
    {
        private readonly ShiftMateContext _context;

        public LeaveRequestService(ShiftMateContext context)
        {
            _context = context;
        }

        public async Task<LeaveRequest> CreateAsync(string userId, DateTime leaveDate, string? note = null)
        {
            var leaveRequest = new LeaveRequest
            {
                UserId = userId,
                LeaveDate = leaveDate,
                Note = note,
                Status = LeaveStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _context.LeaveRequests.Add(leaveRequest);
            await _context.SaveChangesAsync();
            return leaveRequest;
        }

        public async Task<IEnumerable<LeaveRequest>> GetPendingAsync() =>
            await _context.LeaveRequests.Include(lr => lr.User)
                                        .Where(lr => lr.Status == LeaveStatus.Pending)
                                        .ToListAsync();

        public async Task ApproveAsync(int leaveRequestId, string adminUserId)
        {
            var request = await _context.LeaveRequests.FindAsync(leaveRequestId);
            if (request == null) throw new Exception("Request not found.");

            request.Status = LeaveStatus.Approved;
            request.ApprovedByUserId = adminUserId;
            request.ApprovedAt = DateTime.UtcNow;

            // TODO: Nöbet tablosunu güncelle
            await _context.SaveChangesAsync();
        }

        public async Task RejectAsync(int leaveRequestId, string adminUserId)
        {
            var request = await _context.LeaveRequests.FindAsync(leaveRequestId);
            if (request == null) throw new Exception("Request not found.");

            request.Status = LeaveStatus.Rejected;
            request.ApprovedByUserId = adminUserId;
            request.ApprovedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }
    }
}
