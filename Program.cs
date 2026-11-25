using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ไม่พบ ConnectionStrings:DefaultConnection");

builder.Services.AddDbContext<StayMateDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var appInstanceId = Guid.NewGuid().ToString("N");

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;

        options.Cookie.Name = "StayMate.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Production ใช้ Always, ตอนพัฒนาผ่าน http จะยังล็อกอินได้
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // เพิ่ม Claim ประจำ Application Instance
        options.Events.OnSigningIn = context =>
        {
            context.Principal?.Identities
                .FirstOrDefault()?
                .AddClaim(new Claim("AppInstanceId", appInstanceId));

            return Task.CompletedTask;
        };

        // ตรวจสอบว่า Cookie มาจาก Instance ปัจจุบัน และมีสิทธิ์ Role ถูกต้องหรือไม่
        options.Events.OnValidatePrincipal = async context =>
        {
            var instanceClaim = context.Principal?
                .FindFirst("AppInstanceId")?.Value;

            var hasRole = context.Principal?.Claims
                .Any(c => c.Type == ClaimTypes.Role) == true;

            if (instanceClaim != appInstanceId || !hasRole)
            {
                context.RejectPrincipal();

                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme
                );
            }
        };
    });

// จำกัดการลองล็อกอิน/สมัคร: 5 ครั้งต่อนาทีต่อ IP กัน brute force
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ป้องกันการกดปุ่ม Back ของ Browser เพื่อดูหน้า Dashboard หลัง Logout
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }
    await next();
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
