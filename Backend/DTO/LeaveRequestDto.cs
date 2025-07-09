using System.ComponentModel.DataAnnotations;

namespace NobetApp.Api.DTO
{
    public class LeaveRequestDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty; // Same as EndDate for single day leave
        public string EndDate { get; set; } = string.Empty;   // Same as StartDate for single day leave
        public string? Reason { get; set; }   // Maps to Note in your model
        public string Status { get; set; } = string.Empty;    // pending, approved, rejected, canceled
        public string RequestDate { get; set; } = string.Empty; // Maps to CreatedAt
        public string? AdminResponse { get; set; } // Not available in your model
        public string? ProcessedDate { get; set; } // Maps to ApprovedAt
    }

    public class CreateLeaveRequestDto
    {
        [Required(ErrorMessage = "İzin tarihi gereklidir.")]
        [DataType(DataType.Date)]
        public string StartDate { get; set; } = string.Empty;  // ← LeaveDate'den değiştirildi
        public string EndDate { get;set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Not 500 karakterden uzun olamaz.")]
        public string? Reason { get; set; }  // ← Note'dan değiştirildi
    }

    public class AdminResponseDto
    {
        [MaxLength(1000, ErrorMessage = "Admin yanıtı 1000 karakterden uzun olamaz.")]
        public string? AdminResponse { get; set; }
    }
}