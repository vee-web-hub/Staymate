using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StayMate_Kavinvee_0576.Models;

namespace StayMate_Kavinvee_0576.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Rooms()
    {
        return View();
    }

    public IActionResult RoomDetails(int? id)
    {
        return View();
    }

    public IActionResult Promotions()
    {
        return View();
    }

    public IActionResult Facilities()
    {
        return View();
    }

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Contact()
    {
        return View();
    }

    public IActionResult FAQ()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}