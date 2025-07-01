using NobetApp.Api.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NobetApp.Api.Models
{
    public class LeaveRequest
    {
        [Key]
        public int LeaveRequestID { get; set; }

        public string UserId { get; set; } = null!;
        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser User { get; set; } = null!;

        public DateTime LeaveDate { get; set; }             // İzin istenen tarih
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public LeaveStatus Status { get; set; } = LeaveStatus.Pending;

        public string? Note { get; set; }                   // İsteğe açıklama

        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }

    public enum LeaveStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2,
        Canceled = 3
    }
}
