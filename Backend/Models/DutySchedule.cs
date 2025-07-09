using System.ComponentModel.DataAnnotations;

namespace NobetApp.Api.Models
{
    public class DutySchedule
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Department { get; set; } = null!; // "konfigurasyon" veya "izleme"
        [Required]
        public DateTime Date { get; set; }

        // Konfigürasyon nöbetçileri
        public string? Primary { get; set; }
        public string? Backup { get; set; }

        // İzleme nöbetçileri
        public string? Kanban { get; set; }
        public string? Monitoring { get; set; }
    }
}
