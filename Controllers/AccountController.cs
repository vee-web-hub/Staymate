using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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

    // =====================================================================
    // 1. LOGIN
    // =====================================================================

    // ---- ส่วนแสดงผล (GET) ----
    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(UserList));

        return View(new LoginViewModel());
    }

    // ---- ส่วนประมวลผล (POST) : READ ข้อมูลจากตาราง users ----
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        // ดึงผู้ใช้จากฐานข้อมูลพร้อม Role
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == model.Username.Trim());

        if (user == null)
        {
            ModelState.AddModelError("", "Username หรือ Password ไม่ถูกต้อง");
            return View(model);
        }

        if (user.IsActive != true)
        {
            ModelState.AddModelError("", "บัญชีนี้ถูกปิดการใช้งาน");
            return View(model);
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Username หรือ Password ไม่ถูกต้อง");
            return View(model);
        }

        // สร้าง Cookie การเข้าสู่ระบบ
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("FullName", user.FullName)
        };
        foreach (var ur in user.UserRoles)
            claims.Add(new Claim(ClaimTypes.Role, ur.Role.RoleCode));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(UserList));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    // =====================================================================
    // 2. REGISTER (สมัครเอง = Role CUSTOMER)  /  ADD USER (ผู้ดูแลเลือก Role)
    // =====================================================================

    // ---- ส่วนแสดงผล ----
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    // ---- ส่วนประมวลผล : CREATE ลงตาราง users + user_roles ----
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        await CheckDuplicatesAsync(model.Username, model.Email);
        if (!ModelState.IsValid)
            return View(model);

        if (!await AddUserAsync(model, "CUSTOMER"))
        {
            ModelState.AddModelError("", "ไม่พบ Role CUSTOMER ในฐานข้อมูล (ตรวจสอบตาราง roles)");
            return View(model);
        }

        TempData["Success"] = "สมัครสมาชิกสำเร็จ กรุณาเข้าสู่ระบบ";
        return RedirectToAction(nameof(Login));
    }

    // ---- Add User (ส่วนแสดงผล) ----
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Create()
    {
        return View(new UserCreateViewModel { Roles = await GetRoleItemsAsync() });
    }

    // ---- Add User (ส่วนประมวลผล) ----
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        await CheckDuplicatesAsync(model.Username, model.Email);
        if (!ModelState.IsValid)
        {
            model.Roles = await GetRoleItemsAsync();
            return View(model);
        }

        if (!await AddUserAsync(model, model.RoleCode))
        {
            ModelState.AddModelError(nameof(model.RoleCode), "ไม่พบ Role ที่เลือก");
            model.Roles = await GetRoleItemsAsync();
            return View(model);
        }

        TempData["Success"] = $"เพิ่มผู้ใช้ {model.Username} เรียบร้อยแล้ว";
        return RedirectToAction(nameof(UserList));
    }

    // =====================================================================
    // 3. USER LIST  (READ / UPDATE / DELETE)
    // =====================================================================

    // ---- READ : ดึงรายชื่อผู้ใช้ทั้งหมดจากฐานข้อมูล ----
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> UserList(string? q)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(u => u.Username.Contains(q)
                                  || u.FullName.Contains(q)
                                  || u.Email.Contains(q));
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
                IsActive = u.IsActive == true,
                RoleCode = u.UserRoles.Select(ur => ur.Role.RoleCode).FirstOrDefault(),
                RoleName = u.UserRoles.Select(ur => ur.Role.RoleName).FirstOrDefault()
            })
            .ToListAsync();

        ViewBag.Search = q;
        return View(users);
    }

    // ---- UPDATE (ส่วนแสดงผล) ----
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Edit(uint id)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user == null) return NotFound();

        var model = new UserEditViewModel
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            IsActive = user.IsActive == true,
            RoleCode = user.UserRoles.Select(ur => ur.Role.RoleCode).FirstOrDefault() ?? "",
            Roles = await GetRoleItemsAsync()
        };
        return View(model);
    }

    // ---- UPDATE (ส่วนประมวลผล) ----
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.UserId == model.UserId);

        if (user == null) return NotFound();

        await CheckDuplicatesAsync(null, model.Email, model.UserId);
        if (!ModelState.IsValid)
        {
            model.Username = user.Username;
            model.Roles = await GetRoleItemsAsync();
            return View(model);
        }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleCode == model.RoleCode);
        if (role == null)
        {
            ModelState.AddModelError(nameof(model.RoleCode), "ไม่พบ Role ที่เลือก");
            model.Username = user.Username;
            model.Roles = await GetRoleItemsAsync();
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.Email = model.Email.Trim();
        user.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        user.IsActive = model.IsActive;

        if (!string.IsNullOrEmpty(model.NewPassword))
            user.PasswordHash = _hasher.HashPassword(user, model.NewPassword);

        // เปลี่ยน Role ถ้าต่างจากเดิม
        var current = user.UserRoles.FirstOrDefault();
        if (current == null || current.RoleId != role.RoleId)
        {
            _db.UserRoles.RemoveRange(user.UserRoles);
            user.UserRoles.Add(new UserRole { RoleId = role.RoleId });
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = $"แก้ไขข้อมูล {user.Username} เรียบร้อยแล้ว";
        return RedirectToAction(nameof(UserList));
    }

    // ---- DELETE ----
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(uint id)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
        {
            TempData["Error"] = "ไม่สามารถลบบัญชีที่กำลังใช้งานอยู่ได้";
            return RedirectToAction(nameof(UserList));
        }

        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["Error"] = "ไม่พบผู้ใช้ที่ต้องการลบ";
            return RedirectToAction(nameof(UserList));
        }

        try
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"ลบผู้ใช้ {user.Username} เรียบร้อยแล้ว";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "ลบไม่ได้ เนื่องจากผู้ใช้นี้มีข้อมูลที่เกี่ยวข้องอยู่ในระบบ";
        }

        return RedirectToAction(nameof(UserList));
    }

    // =====================================================================
    // Helper
    // =====================================================================

    private async Task<List<SelectListItem>> GetRoleItemsAsync()
    {
        return await _db.Roles
            .OrderBy(r => r.RoleId)
            .Select(r => new SelectListItem { Value = r.RoleCode, Text = r.RoleName })
            .ToListAsync();
    }

    private async Task CheckDuplicatesAsync(string? username, string email, uint? ignoreUserId = null)
    {
        email = email.Trim();

        if (!string.IsNullOrWhiteSpace(username) &&
            await _db.Users.AnyAsync(u => u.Username == username.Trim()))
        {
            ModelState.AddModelError("Username", "Username นี้ถูกใช้งานแล้ว");
        }

        if (await _db.Users.AnyAsync(u => u.Email == email && u.UserId != ignoreUserId))
        {
            ModelState.AddModelError("Email", "Email นี้ถูกใช้งานแล้ว");
        }
    }

    // CREATE: บันทึก users + user_roles
    private async Task<bool> AddUserAsync(RegisterViewModel m, string roleCode)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleCode == roleCode);
        if (role == null) return false;

        var user = new User
        {
            Username = m.Username.Trim(),
            Email = m.Email.Trim(),
            FullName = m.FullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(m.Phone) ? null : m.Phone.Trim(),
            IsActive = true
        };
        user.PasswordHash = _hasher.HashPassword(user, m.Password);
        user.UserRoles.Add(new UserRole { Role = role });

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return true;
    }
}
