namespace NobetApp.Api.DTO
{
    public class UserUpdateProfileDto
    {
        public string? NewUsername { get; set; }
        public string? NewPassword { get; set; }
        public string? NewPasswordConfirm { get; set; }
    }
}
