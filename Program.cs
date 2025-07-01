using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Models;
using NobetApp.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ShiftMateContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false; // Özel karakter zorunlu olmasýn
    options.Password.RequiredLength = 6;
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ShiftMateContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

//-----------
using (var scope = app.Services.CreateScope())
{
    await Task.Run(async () =>
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // 1. Rolleri oluþtur
        string[] roles = new[] { "admin", "worker" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Admin kullanýcýyý oluþtur
        string adminUsername = "admin";
        string adminPassword = "Admin123!";

        var adminUser = await userManager.FindByNameAsync(adminUsername);
        if (adminUser == null)
        {
            var newAdmin = new ApplicationUser
            {
                UserName = adminUsername,
                Email = "admin@example.com"
            };

            var result = await userManager.CreateAsync(newAdmin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(newAdmin, "admin");
                Console.WriteLine("Admin kullanýcýsý oluþturuldu.");
            }
            else
            {
                Console.WriteLine("Admin oluþturulamadý: " + string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    });
}
//--------

app.Run();

