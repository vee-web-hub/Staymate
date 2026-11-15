
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

        // ==========================================
        // ตรวจสอบผู้ใช้งานที่ Login
        // ==========================================
        private async Task<uint?> GetCurrentUserId()
        {
            var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!uint.TryParse(idText, out var userId))
                return null;

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.UserId == userId &&
                    u.IsActive == true);

            if (user == null)
                return null;

            return userId;
        }

        // ==========================================
        // 1. HOME
        // ==========================================
        public async Task<IActionResult> Index()
        {
            var userId = await GetCurrentUserId();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = await _db.Users
                .AsNoTracking()
                .Include(u => u.Bookings)
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            return View(user);
        }

        // ==========================================
        // 2. ROOM AVAILABILITY
        // ==========================================
        public async Task<IActionResult> RoomAvailability()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 3. ROOMS / ROOM DETAILS
        // ==========================================
        public async Task<IActionResult> Rooms()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        public async Task<IActionResult> RoomDetails(uint id)
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 4. PROMOTIONS
        // ==========================================
        public async Task<IActionResult> Promotions()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 5. CHECKOUT & PAYMENT
        // ==========================================
        public async Task<IActionResult> Checkout()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 6. MY BOOKINGS / BOOKING DETAILS
        // ==========================================
        public async Task<IActionResult> MyBookings()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        public async Task<IActionResult> BookingDetails(uint id)
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 7. PROFILE
        // ==========================================
        public async Task<IActionResult> Profile()
        {
            var userId = await GetCurrentUserId();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            return View(user);
        }

        // ==========================================
        // 8. REVIEW & FEEDBACK
        // ==========================================
        public async Task<IActionResult> Reviews()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }
    }
}
