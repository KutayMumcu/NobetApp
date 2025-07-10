using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Data;
using NobetApp.Api.DTO;
using NobetApp.Api.Models;
using System.Security.Claims;

[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ShiftMateContext _context;

    public UserController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ShiftMateContext context)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
    }

    [HttpPost("create")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateUser([FromBody] UserCreateDto dto)
    {
        var user = new ApplicationUser
        {
            UserName = dto.Username,
            FullName = dto.FullName,
            IsActive = dto.IsActive,
            Departmant = dto.Departmant
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            Console.WriteLine("unsuccessful");
            return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        if (!await _roleManager.RoleExistsAsync(dto.Role))
            await _roleManager.CreateAsync(new IdentityRole(dto.Role));

        await _userManager.AddToRoleAsync(user, dto.Role);

        return Ok(new { message = "Kullanıcı oluşturuldu." });
    }

    [HttpGet("list")]
    [AllowAnonymous]
    public async Task<IActionResult> ListUsers()
    {
        var users = _userManager.Users.ToList();

        var userList = new List<object>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userList.Add(new
            {
                user.Id,
                user.UserName,
                user.FullName,
                user.IsActive,
                user.Departmant,
                Roles = roles
            });
        }

        return Ok(userList);
    }

    [HttpDelete("delete/{username}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteUser(string username)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        // Kullanıcı silinmeden önce nöbet tablolarından ismini kaldır
        await RemoveUserFromDutySchedules(user.FullName);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { message = "Silme başarısız." });

        return Ok(new { message = "Kullanıcı silindi." });
    }

    // Normal kullanıcı kendi bilgilerini günceller
    [HttpPut("update-profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateProfileDto dto)
    {
        var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(currentUsername))
            return Unauthorized(new { message = "Kullanıcı bilgisi alınamadı." });

        var user = await _userManager.FindByNameAsync(currentUsername);
        if (user == null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        bool shouldLogout = false;

        // Username güncelleme
        if (!string.IsNullOrEmpty(dto.NewUsername) && dto.NewUsername != user.UserName)
        {
            var existingUser = await _userManager.FindByNameAsync(dto.NewUsername);
            if (existingUser != null)
                return BadRequest(new { message = "Bu kullanıcı adı zaten kullanılıyor." });

            user.UserName = dto.NewUsername;
            shouldLogout = true; // Kullanıcı adı değiştiğinde çıkış yap
        }

        // Şifre güncelleme
        if (!string.IsNullOrEmpty(dto.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
            if (!passwordResult.Succeeded)
                return BadRequest(new { message = string.Join(", ", passwordResult.Errors.Select(e => e.Description)) });

            shouldLogout = true; // Şifre değiştiğinde çıkış yap
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(new { message = string.Join(", ", updateResult.Errors.Select(e => e.Description)) });

        return Ok(new
        {
            message = "Profil güncellendi.",
            shouldLogout = shouldLogout
        });
    }

    // Admin herhangi bir kullanıcıyı günceller
    [HttpPut("update-user")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateUser([FromBody] UserUpdateAdminDto dto)
    {
        var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(currentUsername))
            return Unauthorized(new { message = "Kullanıcı bilgisi alınamadı." });

        var user = await _userManager.FindByNameAsync(dto.Username);
        if (user == null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        // Mevcut bilgileri sakla
        var oldFullName = user.FullName;
        var oldDepartment = user.Departmant?.ToLower();

        // Admin kendi bilgilerini mi güncelliyor?
        bool isUpdatingOwnProfile = dto.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase);
        bool shouldLogout = false;

        // Username güncelleme
        if (!string.IsNullOrEmpty(dto.NewUsername) && dto.NewUsername != user.UserName)
        {
            var existingUser = await _userManager.FindByNameAsync(dto.NewUsername);
            if (existingUser != null)
                return BadRequest(new { message = "Bu kullanıcı adı zaten kullanılıyor." });

            user.UserName = dto.NewUsername;

            // Admin kendi kullanıcı adını değiştiriyorsa çıkış yapsın
            if (isUpdatingOwnProfile)
            {
                shouldLogout = true;
            }
        }

        // Diğer bilgileri güncelleme
        if (!string.IsNullOrEmpty(dto.NewFullName))
            user.FullName = dto.NewFullName;

        if (!string.IsNullOrEmpty(dto.NewDepartment))
            user.Departmant = dto.NewDepartment;

        // Şifre güncelleme
        if (!string.IsNullOrEmpty(dto.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
            if (!passwordResult.Succeeded)
                return BadRequest(new { message = string.Join(", ", passwordResult.Errors.Select(e => e.Description)) });

            // Admin kendi şifresini değiştiriyorsa çıkış yapsın
            if (isUpdatingOwnProfile)
            {
                shouldLogout = true;
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(new { message = string.Join(", ", updateResult.Errors.Select(e => e.Description)) });

        // Rol güncelleme
        if (!string.IsNullOrEmpty(dto.NewRole))
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                    return BadRequest(new { message = "Eski rol kaldırılamadı." });
            }

            if (!await _roleManager.RoleExistsAsync(dto.NewRole))
                await _roleManager.CreateAsync(new IdentityRole(dto.NewRole));

            var addRoleResult = await _userManager.AddToRoleAsync(user, dto.NewRole);
            if (!addRoleResult.Succeeded)
                return BadRequest(new { message = "Yeni rol eklenemedi." });
        }

        // Nöbet tablolarını güncelle - sadece backend'de yapılıyor
        await UpdateDutySchedulesAdvanced(oldFullName, oldDepartment, user.FullName, user.Departmant?.ToLower());

        return Ok(new
        {
            message = "Kullanıcı güncellendi.",
            shouldLogout = shouldLogout
        });
    }

    // Kullanıcının kendi bilgilerini getir
    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> GetProfile()
    {
        var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(currentUsername))
            return Unauthorized(new { message = "Kullanıcı bilgisi alınamadı." });

        var user = await _userManager.FindByNameAsync(currentUsername);
        if (user == null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new
        {
            user.Id,
            user.UserName,
            user.FullName,
            user.IsActive,
            user.Departmant,
            Roles = roles
        });
    }

    // Gelişmiş nöbet tablolarını güncelleme metodu
    private async Task UpdateDutySchedulesAdvanced(string oldFullName, string? oldDepartment, string newFullName, string? newDepartment)
    {
        Console.WriteLine($"UpdateDutySchedulesAdvanced başladı: {oldFullName} -> {newFullName}, {oldDepartment} -> {newDepartment}");

        // Tüm nöbet kayıtlarını al
        var allSchedules = await _context.DutySchedules.ToListAsync();
        var schedulesToUpdate = new List<DutySchedule>();

        // FullName değişikliği kontrolü
        if (!string.IsNullOrEmpty(newFullName) && oldFullName != newFullName)
        {
            Console.WriteLine($"FullName güncelleniyor: {oldFullName} -> {newFullName}");
            foreach (var schedule in allSchedules)
            {
                bool isUpdated = false;

                if (schedule.Primary?.Equals(oldFullName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    schedule.Primary = newFullName;
                    isUpdated = true;
                }
                if (schedule.Backup?.Equals(oldFullName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    schedule.Backup = newFullName;
                    isUpdated = true;
                }
                if (schedule.Kanban?.Equals(oldFullName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    schedule.Kanban = newFullName;
                    isUpdated = true;
                }
                if (schedule.Monitoring?.Equals(oldFullName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    schedule.Monitoring = newFullName;
                    isUpdated = true;
                }

                if (isUpdated)
                {
                    schedulesToUpdate.Add(schedule);
                }
            }
        }

        // Departman değişikliği kontrolü
        if (!string.IsNullOrEmpty(newDepartment) && oldDepartment != newDepartment)
        {
            Console.WriteLine($"Departman güncelleniyor: {oldDepartment} -> {newDepartment}");
            var currentFullName = !string.IsNullOrEmpty(newFullName) ? newFullName : oldFullName;

            // Önce eski departmandan kullanıcıyı kaldır
            if (!string.IsNullOrEmpty(oldDepartment))
            {
                Console.WriteLine($"Eski departmandan kaldırılıyor: {currentFullName} from {oldDepartment}");
                await RemoveUserFromDepartmentSchedules(currentFullName, oldDepartment);
            }

            // Sonra yeni departmana akıllı entegrasyon yap
            Console.WriteLine($"Yeni departmana entegre ediliyor: {currentFullName} to {newDepartment}");
            await IntegrateUserToNewDepartment(currentFullName, newDepartment);
        }

        // Sadece isim değişikliği varsa değişiklikleri kaydet
        if (schedulesToUpdate.Any())
        {
            await _context.SaveChangesAsync();
            Console.WriteLine($"Nöbet tabloları güncellendi: {schedulesToUpdate.Count} kayıt etkilendi.");
        }
    }

    // Kullanıcıyı eski departmandan kaldır
    private async Task RemoveUserFromDepartmentSchedules(string fullName, string department)
    {
        Console.WriteLine($"RemoveUserFromDepartmentSchedules başladı: {fullName} from {department}");

        var departmentSchedules = await _context.DutySchedules
            .Where(s => s.Department.ToLower() == department)
            .ToListAsync();

        Console.WriteLine($"Departman için {departmentSchedules.Count} kayıt bulundu");

        foreach (var schedule in departmentSchedules)
        {
            bool isUpdated = false;

            if (schedule.Primary?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine($"Primary kaldırıldı: {schedule.Primary} -> null, Schedule ID: {schedule.Id}");
                schedule.Primary = null;
                isUpdated = true;
            }
            if (schedule.Backup?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine($"Backup kaldırıldı: {schedule.Backup} -> null, Schedule ID: {schedule.Id}");
                schedule.Backup = null;
                isUpdated = true;
            }
            if (schedule.Kanban?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine($"Kanban kaldırıldı: {schedule.Kanban} -> null, Schedule ID: {schedule.Id}");
                schedule.Kanban = null;
                isUpdated = true;
            }
            if (schedule.Monitoring?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine($"Monitoring kaldırıldı: {schedule.Monitoring} -> null, Schedule ID: {schedule.Id}");
                schedule.Monitoring = null;
                isUpdated = true;
            }

            if (isUpdated)
            {
                Console.WriteLine($"Kullanıcı eski departmandan kaldırıldı: {fullName} - Schedule ID: {schedule.Id}");
            }
        }

        await _context.SaveChangesAsync();
        Console.WriteLine($"Eski departmandan kaldırma işlemi tamamlandı");
    }

    // Kullanıcıyı yeni departmana akıllı entegrasyon yap
    private async Task IntegrateUserToNewDepartment(string fullName, string newDepartment)
    {
        Console.WriteLine($"IntegrateUserToNewDepartment başladı: {fullName} to {newDepartment}");

        var departmentSchedules = await _context.DutySchedules
            .Where(s => s.Department.ToLower() == newDepartment)
            .OrderBy(s => s.Date)
            .ToListAsync();

        Console.WriteLine($"Yeni departman için {departmentSchedules.Count} kayıt bulundu");

        if (departmentSchedules.Count == 0)
        {
            Console.WriteLine($"Yeni departman için nöbet tablosu bulunamadı: {newDepartment}");
            return;
        }

        if (newDepartment == "konfigurasyon")
        {
            Console.WriteLine("Konfigürasyon departmanına entegrasyon başlıyor");
            await IntegrateToConfigurationDepartment(fullName, departmentSchedules);
        }
        else if (newDepartment == "izleme")
        {
            Console.WriteLine("İzleme departmanına entegrasyon başlıyor");
            await IntegrateToMonitoringDepartment(fullName, departmentSchedules);
        }

        await _context.SaveChangesAsync();
        Console.WriteLine($"Yeni departmana entegrasyon tamamlandı");
    }

    // Konfigürasyon departmanına entegrasyon (2 rollü sistem)
    private async Task IntegrateToConfigurationDepartment(string fullName, List<DutySchedule> schedules)
    {
        if (schedules.Count == 0) return;

        var firstRow = schedules[0];
        Console.WriteLine($"Konfigürasyon - İlk satır: Primary={firstRow.Primary}, Backup={firstRow.Backup}");

        // Yeni satır oluştur
        var newRow = new DutySchedule
        {
            Department = "konfigurasyon",
            Date = GetNextAvailableDate(schedules),
            Primary = firstRow.Primary,
            Backup = fullName
        };

        Console.WriteLine($"Konfigürasyon - Yeni satır oluşturuluyor: Primary={newRow.Primary}, Backup={newRow.Backup}, Date={newRow.Date}");

        // Yeni satırı ekle
        _context.DutySchedules.Add(newRow);

        // İlk satırı güncelle
        Console.WriteLine($"Konfigürasyon - İlk satır güncelleniyor: Primary {firstRow.Primary} -> {fullName}");
        firstRow.Primary = fullName;

        Console.WriteLine($"Konfigürasyon departmanına entegre edildi: {fullName}");
    }

    // İzleme departmanına entegrasyon (3 rollü sistem)
    private async Task IntegrateToMonitoringDepartment(string fullName, List<DutySchedule> schedules)
    {
        if (schedules.Count == 0) return;

        var firstRow = schedules[0];
        var secondRow = schedules.Count > 1 ? schedules[1] : null;

        Console.WriteLine($"İzleme - İlk satır: Kanban={firstRow.Kanban}, Monitoring={firstRow.Monitoring}, Backup={firstRow.Backup}");
        if (secondRow != null)
        {
            Console.WriteLine($"İzleme - İkinci satır: Kanban={secondRow.Kanban}, Monitoring={secondRow.Monitoring}, Backup={secondRow.Backup}");
        }

        // Yeni satır oluştur
        var newRow = new DutySchedule
        {
            Department = "izleme",
            Date = GetNextAvailableDate(schedules),
            Kanban = firstRow.Kanban,
            Monitoring = firstRow.Monitoring,
            Backup = fullName
        };

        Console.WriteLine($"İzleme - Yeni satır oluşturuluyor: Kanban={newRow.Kanban}, Monitoring={newRow.Monitoring}, Backup={newRow.Backup}, Date={newRow.Date}");

        // Yeni satırı ekle
        _context.DutySchedules.Add(newRow);

        // İlk satırı güncelle
        Console.WriteLine($"İzleme - İlk satır güncelleniyor: Kanban {firstRow.Kanban} -> {firstRow.Monitoring}, Monitoring {firstRow.Monitoring} -> {fullName}");
        firstRow.Kanban = firstRow.Monitoring;
        firstRow.Monitoring = fullName;

        // İkinci satır varsa güncelle
        if (secondRow != null)
        {
            Console.WriteLine($"İzleme - İkinci satır güncelleniyor: Kanban {secondRow.Kanban} -> {fullName}");
            secondRow.Kanban = fullName;
        }

        Console.WriteLine($"İzleme departmanına entegre edildi: {fullName}");
    }

    // Sonraki uygun tarihi hesapla
    private DateTime GetNextAvailableDate(List<DutySchedule> schedules)
    {
        if (schedules.Count == 0)
        {
            return DateTime.Today;
        }

        var lastDate = schedules.Max(s => s.Date);
        var nextDate = lastDate.AddDays(1);
        Console.WriteLine($"Sonraki uygun tarih hesaplandı: {nextDate}");
        return nextDate;
    }

    private async Task RemoveUserFromDutySchedules(string fullName)
    {
        var allSchedules = await _context.DutySchedules.ToListAsync();
        var schedulesToUpdate = new List<DutySchedule>();

        foreach (var schedule in allSchedules)
        {
            bool isUpdated = false;

            if (schedule.Primary?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                schedule.Primary = null;
                isUpdated = true;
            }
            if (schedule.Backup?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                schedule.Backup = null;
                isUpdated = true;
            }
            if (schedule.Kanban?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                schedule.Kanban = null;
                isUpdated = true;
            }
            if (schedule.Monitoring?.Equals(fullName, StringComparison.OrdinalIgnoreCase) == true)
            {
                schedule.Monitoring = null;
                isUpdated = true;
            }

            if (isUpdated)
            {
                schedulesToUpdate.Add(schedule);
            }
        }

        if (schedulesToUpdate.Any())
        {
            await _context.SaveChangesAsync();
            Console.WriteLine($"Kullanıcı nöbet tablolarından kaldırıldı: {fullName}");
        }
    }
}