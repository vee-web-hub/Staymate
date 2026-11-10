//Controllers/CustomerController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;

namespace StayMate_Kavinvee_0576.Controllers
{
    [Authorize(Roles = "CUSTOMER")]
    public class CustomerController : Controller
    {
        private readonly StayMateDbContext _db;

        public CustomerController(StayMateDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            // ดึง UserId จาก Claim ที่สร้างตอน Login
            var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!uint.TryParse(idText, out var userId))
                return RedirectToAction("Login", "Account");

            var user = await _db.Users
                .AsNoTracking()
                .Include(u => u.Bookings)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            // บัญชีถูกลบหรือปิดใช้งานระหว่างที่ Cookie ยังไม่หมดอายุ
            if (user == null || user.IsActive != true)
                return RedirectToAction("Login", "Account");

            return View(user);
        }
    }
}