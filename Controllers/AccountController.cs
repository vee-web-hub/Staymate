
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers;

public class AccountController : Controller
{
    private readonly StayMateDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public AccountController(StayMateDbContext db)
    {
        _db = db;
    }

    // =========================================================
    // LOGIN
    // =========================================================

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectByRole(
                User.FindAll(ClaimTypes.Role).Select(c => c.Value)
            );
        }

        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        var username = model.Username.Trim();

        var user = await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == username);

        // ตรวจสอบ Username
        if (user == null)
        {
            ModelState.AddModelError(
                "",
                "Username หรือ Password ไม่ถูกต้อง"
            );

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // ตรวจสอบ Password
        var result = _hasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            model.Password
        );

        if (result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(
                "",
                "Username หรือ Password ไม่ถูกต้อง"
            );

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // ตรวจสอบสถานะบัญชี
        if (user.IsActive != true)
        {
            ModelState.AddModelError(
                "",
                "บัญชีนี้ถูกปิดการใช้งาน"
            );

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // ดึง Role ของผู้ใช้
        var roleCodes = user.UserRoles
            .Select(ur => ur.Role.RoleCode)
            .ToList();

        if (roleCodes.Count == 0)
        {
            ModelState.AddModelError(
                "",
                "บัญชีนี้ยังไม่ได้กำหนดสิทธิ์การใช้งาน"
            );

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // สร้าง Claims
        var claims = new List<Claim>
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user.UserId.ToString()
            ),

            new Claim(
                ClaimTypes.Name,
                user.Username
            ),

            new Claim(
                "FullName",
                user.FullName
            )
        };

        claims.AddRange(
            roleCodes.Select(role =>
                new Claim(ClaimTypes.Role, role)
            )
        );

        // สร้าง Authentication Cookie
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme
        );

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal
        );

        // Redirect CUSTOMER ไปหน้าลูกค้าเสมอ
        if (roleCodes.Contains("CUSTOMER"))
        {
            return RedirectToAction(
                "Index",
                "Customer"
            );
        }

        // Role อื่นสามารถใช้ ReturnUrl ได้
        if (!string.IsNullOrEmpty(returnUrl)
            && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectByRole(roleCodes);
    }

    // =========================================================
    // LOGOUT
    // =========================================================

   [HttpPost]
[Authorize]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Logout()
{
    await HttpContext.SignOutAsync(
        CookieAuthenticationDefaults.AuthenticationScheme
    );

    return RedirectToAction("Index", "Home");
}

    // =========================================================
    // ACCESS DENIED
    // =========================================================

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // =========================================================
    // REGISTER
    // สมัครสมาชิกใหม่เป็น CUSTOMER เท่านั้น
    // =========================================================

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectByRole(
                User.FindAll(ClaimTypes.Role).Select(c => c.Value)
            );
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model)
    {
        await CheckDuplicatesAsync(
            model.Username,
            model.Email
        );

        if (!ModelState.IsValid)
            return View(model);

        var success = await AddUserAsync(
            model,
            "CUSTOMER"
        );

        if (!success)
        {
            ModelState.AddModelError(
                "",
                "ไม่พบ Role CUSTOMER ในฐานข้อมูล"
            );

            return View(model);
        }

        TempData["Success"] = "สมัครสมาชิกสำเร็จ กรุณาเข้าสู่ระบบ";

        return RedirectToAction(nameof(Login));
    }

    // =========================================================
    // USER LIST
    // =========================================================

    [HttpGet]
[Authorize]
public async Task<IActionResult> UserList(string? q)
    {
        var query = _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .AsQueryable();

        // ค้นหาผู้ใช้
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();

            query = query.Where(u =>
                u.Username.Contains(q) ||
                u.FullName.Contains(q) ||
                u.Email.Contains(q)
            );
        }

        var users = await query
            .OrderBy(u => u.UserId)
            .Select(u => new UserListItem
            {
                UserId = u.UserId,
                Username = u.Username,
                FullName = u.FullName,
                Email = u.Email,
                Phone = u.Phone,

                // แก้ CS8629 และ CS0266
                IsActive = u.IsActive ?? false,

                RoleCode = u.UserRoles
                    .Select(ur => ur.Role.RoleCode)
                    .FirstOrDefault(),

                RoleName = u.UserRoles
                    .Select(ur => ur.Role.RoleName)
                    .FirstOrDefault()
            })
            .ToListAsync();

        ViewBag.Search = q;

        return View(users);
    }

    // =========================================================
    // REDIRECT BY ROLE
    // =========================================================

    private IActionResult RedirectByRole(
        IEnumerable<string> roleCodes)
    {
        var roles = roleCodes.ToList();

        if (roles.Contains("CUSTOMER"))
        {
            return RedirectToAction(
                "Index",
                "Customer"
            );
        }

        if (roles.Contains("MANAGER") ||
            roles.Contains("OWNER") ||
            roles.Contains("FRONT_ADMIN"))
        {
            return RedirectToAction(
                "UserList",
                "Account"
            );
        }

        return RedirectToAction(
            "Index",
            "Home"
        );
    }

    // =========================================================
    // CHECK DUPLICATES
    // =========================================================

    private async Task CheckDuplicatesAsync(
        string? username,
        string email)
    {
        email = email.Trim();

        if (!string.IsNullOrWhiteSpace(username) &&
            await _db.Users.AnyAsync(
                u => u.Username == username.Trim()))
        {
            ModelState.AddModelError(
                "Username",
                "Username นี้ถูกใช้งานแล้ว"
            );
        }

        if (await _db.Users.AnyAsync(
            u => u.Email == email))
        {
            ModelState.AddModelError(
                "Email",
                "Email นี้ถูกใช้งานแล้ว"
            );
        }
    }

    // =========================================================
    // CREATE USER
    // =========================================================

    private async Task<bool> AddUserAsync(
        RegisterViewModel model,
        string roleCode)
    {
        var role = await _db.Roles
            .FirstOrDefaultAsync(r => r.RoleCode == roleCode);

        if (role == null)
            return false;

        var user = new User
        {
            Username = model.Username.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),

            Phone = string.IsNullOrWhiteSpace(model.Phone)
                ? null
                : model.Phone.Trim(),

            IsActive = true
        };

        user.PasswordHash = _hasher.HashPassword(
            user,
            model.Password
        );

        user.UserRoles.Add(
            new UserRole
            {
                Role = role
            }
        );

        _db.Users.Add(user);

        await _db.SaveChangesAsync();

        return true;
    }
}
