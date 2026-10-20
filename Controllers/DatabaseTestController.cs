
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;

namespace StayMate_Kavinvee_0576.Controllers;

public class DatabaseTestController : Controller
{
    private readonly StayMateDbContext _context;

    public DatabaseTestController(StayMateDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var roomTypes = await _context.RoomTypes
            .AsNoTracking()
            .ToListAsync();

        return Json(roomTypes);
    }
}
