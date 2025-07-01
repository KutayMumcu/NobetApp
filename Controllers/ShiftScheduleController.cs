using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NobetApp.Api.Data;

namespace NobetApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShiftScheduleController : ControllerBase
    {
        private readonly ShiftMateContext _context;

        public ShiftScheduleController(ShiftMateContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetSchedules()
        {
            var schedules = await _context.ShiftSchedules
                .Include(s => s.PrimaryUser)
                .Include(s => s.SecondaryUser)
                .Include(s => s.TertiaryUser)
                .ToListAsync();

            return Ok(schedules);
        }
    }
}
