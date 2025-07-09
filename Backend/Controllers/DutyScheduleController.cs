using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Data;
using NobetApp.Api.Models;
using System.Globalization;

namespace NobetApp.Api.Controllers
{
    [ApiController]
    [Route("api/schedule")]
    public class DutyScheduleController : ControllerBase
    {
        private readonly ShiftMateContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        public DutyScheduleController(ShiftMateContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpPost("generate")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GenerateSchedule()
        {
            // 1. Tüm mevcut kayıtları sil
            var all = await _context.DutySchedules.ToListAsync();
            _context.DutySchedules.RemoveRange(all);
            await _context.SaveChangesAsync();

            // 2. Kullanıcıları departmana göre çek
            var users = await _userManager.Users.ToListAsync();
            var configUsers = users.Where(u => u.Departmant.ToLower() == "konfigurasyon").ToList();
            var izlemeUsers = users.Where(u => u.Departmant.ToLower() == "izleme").ToList();

            var newSchedules = new List<DutySchedule>();

            // 3. Başlangıç tarihi olarak bu haftanın Pazartesi gününü al
            var today = DateTime.Today;
            int daysSinceMonday = today.DayOfWeek == DayOfWeek.Sunday ? 6 : ((int)today.DayOfWeek - 1);
            var monday = today.AddDays(-daysSinceMonday); // Haftanın Pazartesi günü

            // 4. Konfigürasyon için nöbet ataması
            for (int i = 0; i < configUsers.Count; i++)
            {
                var primary = configUsers[(i + 0) % configUsers.Count].FullName;
                var backup = configUsers[(i + 1) % configUsers.Count].FullName;
                var date = monday.AddDays(i * 7); // her kullanıcıya bir hafta arayla Pazartesi atanır

                newSchedules.Add(new DutySchedule
                {
                    Department = "konfigurasyon",
                    Date = date,
                    Primary = primary,
                    Backup = backup
                });
            }

            // 5. İzleme için nöbet ataması (kanban, monitoring, backup)
            for (int i = 0; i < izlemeUsers.Count; i++)
            {
                var kanban = izlemeUsers[(i + 0) % izlemeUsers.Count].FullName;
                var monitoring = izlemeUsers[(i + 1) % izlemeUsers.Count].FullName;
                var backup = izlemeUsers[(i + 2) % izlemeUsers.Count].FullName;
                var date = monday.AddDays(i * 7);

                // Aynı kullanıcı aynı satırda birden fazla görevde olmasın
                if (new[] { kanban, monitoring, backup }.Distinct().Count() < 3)
                    continue;

                newSchedules.Add(new DutySchedule
                {
                    Department = "izleme",
                    Date = date,
                    Kanban = kanban,
                    Monitoring = monitoring,
                    Backup = backup
                });
            }

            // 6. İzin çakışmalarını çöz - maksimum 50 iterasyon
            int maxIterations = 50;
            int currentIteration = 0;
            bool hasConflicts = true;

            while (hasConflicts && currentIteration < maxIterations)
            {
                hasConflicts = await HandleLeaveConflicts(configUsers, izlemeUsers, newSchedules);
                currentIteration++;

                if (hasConflicts)
                {
                    Console.WriteLine($"İterasyon {currentIteration}: Hala çakışmalar var, tekrar deneniyor...");
                }
            }

            if (currentIteration >= maxIterations)
            {
                Console.WriteLine($"Maksimum iterasyon sayısına ({maxIterations}) ulaşıldı. Bazı çakışmalar çözülememiş olabilir.");
            }

            // 7. Veritabanına kaydet
            _context.DutySchedules.AddRange(newSchedules);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Nöbet tablosu başarıyla oluşturuldu.",
                count = newSchedules.Count,
                iterations = currentIteration
            });
        }

        private async Task<bool> HandleLeaveConflicts(List<ApplicationUser> configUsers, List<ApplicationUser> izlemeUsers, List<DutySchedule> newSchedules)
        {
            // Onaylanmış izinleri çek (kullanıcı bilgileri ile birlikte)
            var approvedLeaves = await _context.LeaveRequests
                .Where(l => l.Status == LeaveStatus.Approved)
                .Include(l => l.User) // User ilişkisini dahil et
                .ToListAsync();

            // Yardımcı fonksiyon: Belirli tarihte kullanıcı izinli mi?
            bool IsOnLeave(string? fullName, DateTime dutyDate)
            {
                if (string.IsNullOrWhiteSpace(fullName))
                    return false;

                // Nöbet haftasının başlangıç ve bitiş tarihlerini hesapla
                var dutyStartDate = dutyDate; // Nöbet haftasının başlangıcı (Pazartesi)
                var dutyEndDate = dutyDate.AddDays(6); // Nöbet haftasının bitişi (Pazar)

                return approvedLeaves.Any(l =>
                    l.User != null &&
                    l.User.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase) &&
                    // İki tarih aralığı kesişiyor mu kontrolü
                    l.LeaveDate <= dutyEndDate && l.EndDate >= dutyStartDate);
            }

            // Departmana göre kullanıcı listesi
            var configNames = configUsers.Select(u => u.FullName).ToList();
            var izlemeNames = izlemeUsers.Select(u => u.FullName).ToList();

            // Çakışma durumlarını loglamak için
            var conflictLog = new List<string>();
            bool hasAnyConflict = false;

            foreach (var schedule in newSchedules.ToList()) // ToList() ile kopyalayarak iterate ediyoruz
            {
                var date = schedule.Date;
                bool hasConflict = false;

                if (schedule.Department == "konfigurasyon")
                {
                    // PRIMARY kontrolü ve swap
                    if (!string.IsNullOrWhiteSpace(schedule.Primary) && IsOnLeave(schedule.Primary, date))
                    {
                        var available = configNames
                            .Where(name => !IsOnLeave(name, date))
                            .Where(name => !name.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                        if (available != null)
                        {
                            // Swap yapılacak kullanıcıyı bul
                            var swapTarget = FindUserToSwap(available, schedule.Primary, newSchedules, date);
                            if (swapTarget != null)
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Primary SWAP: {schedule.Primary} ↔ {available} (izinli)");
                                PerformSwap(swapTarget, available, schedule.Primary);
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Primary REPLACE: {schedule.Primary} → {available} (izinli)");
                            }
                            schedule.Primary = available;
                        }
                        else
                        {
                            conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Primary için uygun kullanıcı bulunamadı: {schedule.Primary}");
                            hasConflict = true;
                        }
                    }

                    // BACKUP kontrolü ve swap
                    if (!string.IsNullOrWhiteSpace(schedule.Backup) && IsOnLeave(schedule.Backup, date))
                    {
                        var available = configNames
                            .Where(name => !IsOnLeave(name, date))
                            .Where(name => !name.Equals(schedule.Primary, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                        if (available != null)
                        {
                            // Swap yapılacak kullanıcıyı bul
                            var swapTarget = FindUserToSwap(available, schedule.Backup, newSchedules, date);
                            if (swapTarget != null)
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Backup SWAP: {schedule.Backup} ↔ {available} (izinli)");
                                PerformSwap(swapTarget, available, schedule.Backup);
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Backup REPLACE: {schedule.Backup} → {available} (izinli)");
                            }
                            schedule.Backup = available;
                        }
                        else
                        {
                            conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon Backup için uygun kullanıcı bulunamadı: {schedule.Backup}");
                            hasConflict = true;
                        }
                    }

                    // Konfigürasyon departmanında aynı kişi birden fazla göreve atandı mı kontrol et
                    if (!string.IsNullOrWhiteSpace(schedule.Primary) &&
                        !string.IsNullOrWhiteSpace(schedule.Backup) &&
                        schedule.Primary.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                    {
                        conflictLog.Add($"[{date:yyyy-MM-dd}] Konfigürasyon departmanında aynı kişi Primary ve Backup: {schedule.Primary}");
                        hasConflict = true;
                    }
                }
                else if (schedule.Department == "izleme")
                {
                    // KANBAN kontrolü ve swap
                    if (!string.IsNullOrWhiteSpace(schedule.Kanban) && IsOnLeave(schedule.Kanban, date))
                    {
                        var available = izlemeNames
                            .Where(name => !IsOnLeave(name, date))
                            .Where(name => !name.Equals(schedule.Monitoring, StringComparison.OrdinalIgnoreCase))
                            .Where(name => !name.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                        if (available != null)
                        {
                            // Swap yapılacak kullanıcıyı bul
                            var swapTarget = FindUserToSwap(available, schedule.Kanban, newSchedules, date);
                            if (swapTarget != null)
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Kanban SWAP: {schedule.Kanban} ↔ {available} (izinli)");
                                PerformSwap(swapTarget, available, schedule.Kanban);
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Kanban REPLACE: {schedule.Kanban} → {available} (izinli)");
                            }
                            schedule.Kanban = available;
                        }
                        else
                        {
                            conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Kanban için uygun kullanıcı bulunamadı: {schedule.Kanban}");
                            hasConflict = true;
                        }
                    }

                    // MONITORING kontrolü ve swap
                    if (!string.IsNullOrWhiteSpace(schedule.Monitoring) && IsOnLeave(schedule.Monitoring, date))
                    {
                        var available = izlemeNames
                            .Where(name => !IsOnLeave(name, date))
                            .Where(name => !name.Equals(schedule.Kanban, StringComparison.OrdinalIgnoreCase))
                            .Where(name => !name.Equals(schedule.Backup, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                        if (available != null)
                        {
                            // Swap yapılacak kullanıcıyı bul
                            var swapTarget = FindUserToSwap(available, schedule.Monitoring, newSchedules, date);
                            if (swapTarget != null)
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Monitoring SWAP: {schedule.Monitoring} ↔ {available} (izinli)");
                                PerformSwap(swapTarget, available, schedule.Monitoring);
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Monitoring REPLACE: {schedule.Monitoring} → {available} (izinli)");
                            }
                            schedule.Monitoring = available;
                        }
                        else
                        {
                            conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Monitoring için uygun kullanıcı bulunamadı: {schedule.Monitoring}");
                            hasConflict = true;
                        }
                    }

                    // BACKUP kontrolü ve swap
                    if (!string.IsNullOrWhiteSpace(schedule.Backup) && IsOnLeave(schedule.Backup, date))
                    {
                        var available = izlemeNames
                            .Where(name => !IsOnLeave(name, date))
                            .Where(name => !name.Equals(schedule.Kanban, StringComparison.OrdinalIgnoreCase))
                            .Where(name => !name.Equals(schedule.Monitoring, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                        if (available != null)
                        {
                            // Swap yapılacak kullanıcıyı bul
                            var swapTarget = FindUserToSwap(available, schedule.Backup, newSchedules, date);
                            if (swapTarget != null)
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Backup SWAP: {schedule.Backup} ↔ {available} (izinli)");
                                PerformSwap(swapTarget, available, schedule.Backup);
                            }
                            else
                            {
                                conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Backup REPLACE: {schedule.Backup} → {available} (izinli)");
                            }
                            schedule.Backup = available;
                        }
                        else
                        {
                            conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme Backup için uygun kullanıcı bulunamadı: {schedule.Backup}");
                            hasConflict = true;
                        }
                    }

                    // Tüm görevlerin farklı kişilerde olduğunu kontrol et
                    var assignments = new[] { schedule.Kanban, schedule.Monitoring, schedule.Backup }
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    if (assignments.Count > 1 && assignments.Distinct(StringComparer.OrdinalIgnoreCase).Count() != assignments.Count)
                    {
                        conflictLog.Add($"[{date:yyyy-MM-dd}] İzleme departmanında aynı kişi birden fazla göreve atandı: {string.Join(", ", assignments)}");
                        hasConflict = true;
                    }
                }

                if (hasConflict)
                {
                    hasAnyConflict = true;
                }
            }

            // Çakışma loglarını konsola yazdır
            if (conflictLog.Any())
            {
                Console.WriteLine("=== İzin Çakışmaları ===");
                foreach (var log in conflictLog)
                {
                    Console.WriteLine(log);
                }
            }

            return hasAnyConflict;
        }

        private DutySchedule? FindUserToSwap(string availableUser, string conflictedUser, List<DutySchedule> schedules, DateTime currentDate)
        {
            // Available user'ın başka bir tarihte görev alıp almadığını kontrol et
            // Eğer başka bir tarihte görev alıyorsa, o tarihte conflicted user ile swap yapılabilir

            return schedules
                .Where(s => s.Date != currentDate) // Mevcut tarih değil
                .Where(s =>
                    s.Primary?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true ||
                    s.Backup?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true ||
                    s.Kanban?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true ||
                    s.Monitoring?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true)
                .FirstOrDefault();
        }

        private void PerformSwap(DutySchedule targetSchedule, string availableUser, string conflictedUser)
        {
            // Target schedule'da available user'ı conflicted user ile değiştir

            if (targetSchedule.Primary?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true)
            {
                targetSchedule.Primary = conflictedUser;
            }
            else if (targetSchedule.Backup?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true)
            {
                targetSchedule.Backup = conflictedUser;
            }
            else if (targetSchedule.Kanban?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true)
            {
                targetSchedule.Kanban = conflictedUser;
            }
            else if (targetSchedule.Monitoring?.Equals(availableUser, StringComparison.OrdinalIgnoreCase) == true)
            {
                targetSchedule.Monitoring = conflictedUser;
            }
        }

        // GET: /api/schedule/konfigurasyon  veya /api/schedule/izleme
        [HttpGet("{department}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetByDepartment(string department)
        {
            var list = await _context.DutySchedules
                .Where(x => x.Department.ToLower() == department.ToLower())
                .OrderBy(x => x.Date)
                .ToListAsync();

            return Ok(list);
        }

        // POST: /api/schedule
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DutySchedule dto)
        {
            _context.DutySchedules.Add(dto);
            await _context.SaveChangesAsync();
            return Ok(dto);
        }

        // DELETE: /api/schedule/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schedule = await _context.DutySchedules.FindAsync(id);
            if (schedule == null) return NotFound();

            _context.DutySchedules.Remove(schedule);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] DutySchedule updated)
        {
            var existing = await _context.DutySchedules.FindAsync(id);
            if (existing == null)
                return NotFound(new { message = "Kayıt bulunamadı." });

            // Sistem kullanıcılarını al
            var users = await _userManager.Users.ToListAsync();
            var userNames = users.Select(u => u.FullName.ToLower()).ToHashSet();

            // Gelen isimleri doğrula (boş olanları es geçiyoruz)
            if (!string.IsNullOrWhiteSpace(updated.Primary) &&
                !userNames.Contains(updated.Primary.ToLower()))
                return BadRequest(new { message = $"Primary: {updated.Primary} geçerli bir kullanıcı değil." });

            if (!string.IsNullOrWhiteSpace(updated.Backup) &&
                !userNames.Contains(updated.Backup.ToLower()))
                return BadRequest(new { message = $"Backup: {updated.Backup} geçerli bir kullanıcı değil." });

            if (!string.IsNullOrWhiteSpace(updated.Kanban) &&
                !userNames.Contains(updated.Kanban.ToLower()))
                return BadRequest(new { message = $"Kanban: {updated.Kanban} geçerli bir kullanıcı değil." });

            if (!string.IsNullOrWhiteSpace(updated.Monitoring) &&
                !userNames.Contains(updated.Monitoring.ToLower()))
                return BadRequest(new { message = $"Monitoring: {updated.Monitoring} geçerli bir kullanıcı değil." });

            // Değerleri güncelle
            existing.Primary = updated.Primary;
            existing.Backup = updated.Backup;
            existing.Kanban = updated.Kanban;
            existing.Monitoring = updated.Monitoring;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}