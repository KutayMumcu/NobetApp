using Microsoft.AspNetCore.Identity;

namespace NobetApp.Api.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }
        public bool IsActive { get; set; } = true;
        public string Departmant { get; set; } = null!;
    }
}
