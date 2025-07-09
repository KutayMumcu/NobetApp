using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Models;

namespace NobetApp.Api.Data
{
    public class ShiftMateContext : IdentityDbContext<ApplicationUser>
    {
        public ShiftMateContext(DbContextOptions<ShiftMateContext> options) : base(options) { }

        public DbSet<ShiftSchedule> ShiftSchedules { get; set; } = null!;
        public DbSet<LeaveRequest> LeaveRequests { get; set; } = null!;
        public DbSet<DutySchedule> DutySchedules { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Eğer fluent API ayarları yapacaksan buraya ekle
        }
    }
}
