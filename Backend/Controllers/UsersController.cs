using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NobetApp.Api.DTO;
using NobetApp.Api.Models;
using System.Security.Claims;

[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UserController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
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

        // Username güncelleme
        if (!string.IsNullOrEmpty(dto.NewUsername) && dto.NewUsername != user.UserName)
        {
            var existingUser = await _userManager.FindByNameAsync(dto.NewUsername);
            if (existingUser != null)
                return BadRequest(new { message = "Bu kullanıcı adı zaten kullanılıyor." });

            user.UserName = dto.NewUsername;
        }

        // Şifre güncelleme
        if (!string.IsNullOrEmpty(dto.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
            if (!passwordResult.Succeeded)
                return BadRequest(new { message = string.Join(", ", passwordResult.Errors.Select(e => e.Description)) });
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(new { message = string.Join(", ", updateResult.Errors.Select(e => e.Description)) });

        return Ok(new { message = "Profil güncellendi." });
    }

    // Admin herhangi bir kullanıcıyı günceller
    [HttpPut("update-user")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateUser([FromBody] UserUpdateAdminDto dto)
    {
        var user = await _userManager.FindByNameAsync(dto.Username);
        if (user == null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        // Username güncelleme
        if (!string.IsNullOrEmpty(dto.NewUsername) && dto.NewUsername != user.UserName)
        {
            var existingUser = await _userManager.FindByNameAsync(dto.NewUsername);
            if (existingUser != null)
                return BadRequest(new { message = "Bu kullanıcı adı zaten kullanılıyor." });

            user.UserName = dto.NewUsername;
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

        return Ok(new { message = "Kullanıcı güncellendi." });
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
}