using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NobetApp.Api.Models;
using NobetApp.Api.Services;

namespace NobetApp.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly JwtService _jwtService;

        public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, JwtService jwtService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtService = jwtService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password))
                return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });

            var token = await _jwtService.GenerateToken(user, _userManager);
            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                username = user.UserName,
                role = roles.FirstOrDefault(),
                token
            });
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}