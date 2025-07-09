namespace NobetApp.Api.DTO
{
    public class UserUpdateAdminDto
    {
        public string Username { get; set; } = null!; // Güncellenecek kullanıcının mevcut username'i
        public string? NewUsername { get; set; }
        public string? NewFullName { get; set; }
        public string? NewDepartment { get; set; }
        public string? NewRole { get; set; }
        public string? NewPassword { get; set; }
        public string? NewPasswordConfirm { get; set; }
    }
}
