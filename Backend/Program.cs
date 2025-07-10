using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NobetApp.Api.BackgroundServices;
using NobetApp.Api.Data;
using NobetApp.Api.Models;
using NobetApp.Api.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    { Title = "ShiftMate API", Version = "v1" });
    // JWT Bearer authentication desteði
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT token'ýnýzý girin. Örnek: Bearer eyJhbGciOi..."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
    {
        new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        },
        Array.Empty<string>()
    }
    });
});
builder.Services.AddDbContext<ShiftMateContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false; // Özel karakter zorunlu olmasýn
    //options.Password.RequiredLength = 9;
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ShiftMateContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<LeaveRequestCleanupService>();
builder.Services.AddHostedService<LeaveRequestCleanupBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication(); // Authorization'dan önce gelmeli

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
        string adminFullName = "Admin ADMIN";
        string adminPassword = "Admin123!";
        string adminDepartmant = "izleme";

        var adminUser = await userManager.FindByNameAsync(adminUsername);
        if (adminUser == null)
        {
            var newAdmin = new ApplicationUser
            {
                UserName = adminUsername,
                FullName = adminFullName,
                Departmant = adminDepartmant,
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

        // User oluþtur
        string userUsername = "user";
        string userFullName = "User USER";
        string userPassword = "User123!";
        string userDepartmant = "konfigurasyon";

        var userUser = await userManager.FindByNameAsync(userUsername);
        if (userUser == null)
        {

            var newUser = new ApplicationUser
            {
                UserName = userUsername,
                FullName = userFullName,
                Departmant = userDepartmant,
            };

            var result = await userManager.CreateAsync(newUser, userPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(newUser, "worker");
                Console.WriteLine("User kullanýcýsý oluþturuldu.");
            }
            else
            {
                Console.WriteLine("User oluþturulamadý: " + string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    });
}
//--------

app.Run();

