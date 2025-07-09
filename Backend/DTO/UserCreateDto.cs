public class UserCreateDto
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public string Departmant { get; set; } = null!;
}
