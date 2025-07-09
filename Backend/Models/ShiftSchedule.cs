using NobetApp.Api.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NobetApp.Api.Models
{
    public class ShiftSchedule
    {
        [Key]
        public int ScheduleID { get; set; }

        public DateTime ShiftDate { get; set; }

        // 1. Nöbetçi
        public string? PrimaryUserId { get; set; }
        [ForeignKey(nameof(PrimaryUserId))]
        public virtual ApplicationUser? PrimaryUser { get; set; }

        public string? PrimaryBackupUserId { get; set; }
        [ForeignKey(nameof(PrimaryBackupUserId))]
        public virtual ApplicationUser? PrimaryBackupUser { get; set; }

        // 2. Nöbetçi
        public string? SecondaryUserId { get; set; }
        [ForeignKey(nameof(SecondaryUserId))]
        public virtual ApplicationUser? SecondaryUser { get; set; }

        public string? SecondaryBackupUserId { get; set; }
        [ForeignKey(nameof(SecondaryBackupUserId))]
        public virtual ApplicationUser? SecondaryBackupUser { get; set; }

        // 3. Nöbetçi
        public string? TertiaryUserId { get; set; }
        [ForeignKey(nameof(TertiaryUserId))]
        public virtual ApplicationUser? TertiaryUser { get; set; }

        public string? TertiaryBackupUserId { get; set; }
        [ForeignKey(nameof(TertiaryBackupUserId))]
        public virtual ApplicationUser? TertiaryBackupUser { get; set; }

        public string? Note { get; set; }
    }
}
