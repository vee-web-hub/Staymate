
using Microsoft.AspNetCore.Mvc;

namespace StayMate_Kavinvee_0576.Controllers
{
    public class OwnerController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}
