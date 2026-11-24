using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers
{
    // *** ถ้า RoleCode ของแม่บ้านในตาราง roles ไม่ใช่ "HOUSEKEEPING" ให้แก้ค่าคงที่ตัวเดียวนี้ ***
    [Authorize(Roles = HousekeepingController.HousekeepingRole)]
    public class HousekeepingController : Controller
    {
        public const string HousekeepingRole = "HOUSEKEEPING";

        private const string StatusPending = HousekeepingViewModel.StatusPending;
        private const string StatusInProgress = HousekeepingViewModel.StatusInProgress;
        private const string StatusDone = HousekeepingViewModel.StatusDone;
        private const string ChecklistPassedMarker = HousekeepingViewModel.ChecklistPassedMarker;

        private readonly StayMateDbContext _context;
        private readonly ILogger<HousekeepingController> _logger;

        public HousekeepingController(StayMateDbContext context, ILogger<HousekeepingController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ==========================================
        // 1. DASHBOARD (Index & AJAX Endpoints)
        // ==========================================

        public async Task<IActionResult> Index()
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();

                var model = await BuildDashboardViewModelAsync(userInfo.Value.UserId, userInfo.Value.FullName);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Housekeeping Index failed");
                TempData["ErrorMessage"] = "เกิดข้อผิดพลาดในการโหลดข้อมูล Dashboard กรุณาลองใหม่อีกครั้ง";
                return View(new HousekeepingViewModel
                {
                    CurrentDate = DateTime.Now,
                    CurrentUserName = User.Identity?.Name ?? "พนักงานฝ่ายแม่บ้าน",
                    DailyStatusSummary = "ไม่สามารถโหลดข้อมูลล่าสุดได้ในขณะนี้"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();

                var model = await BuildDashboardViewModelAsync(userInfo.Value.UserId, userInfo.Value.FullName);
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalAssignedRooms = model.TotalAssignedRooms,
                        pendingRooms = model.PendingRooms,
                        inProgressRooms = model.InProgressRooms,
                        completedToday = model.CompletedToday,
                        maintenanceRooms = model.MaintenanceRooms,
                        cleaningProgress = model.CleaningProgress,
                        remainingTasksCount = model.RemainingTasksCount,
                        unassignedPendingTasks = model.UnassignedPendingTasks,
                        myActiveTasks = model.MyActiveTasks,
                        dailyStatusSummary = model.DailyStatusSummary,
                        currentDate = model.CurrentDate.ToString("yyyy-MM-ddTHH:mm:ss")
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDashboardData failed");
                return StatusCode(500, new { success = false, message = "เกิดข้อผิดพลาดในการดึงข้อมูลสรุป Dashboard" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCleaningTasks(string? search, string? status, string? sort)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                var query = _context.HousekeepingTasks.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var keyword = search.Trim().TrimStart('#');
                    var isId = uint.TryParse(keyword, out var searchId);
                    query = query.Where(t => t.Room.RoomNumber.Contains(keyword) || (isId && t.TaskId == searchId));
                }

                var normalizedStatus = NormalizeStatus(status);
                if (normalizedStatus != null)
                {
                    query = query.Where(t => t.Status == normalizedStatus);
                }

                query = sort == "asc"
                    ? query.OrderBy(t => t.CreatedAt)
                    : query.OrderByDescending(t => t.CreatedAt);

                var tasks = await ProjectTasks(query.Take(50), uid).ToListAsync();
                return Json(new { success = true, data = tasks });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCleaningTasks failed");
                return StatusCode(500, new { success = false, message = "เกิดข้อผิดพลาดในการโหลดรายการงานทำความสะอาด" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentActivities()
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();

                var activities = await BuildRecentActivitiesAsync(12);
                return Json(new { success = true, data = activities });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRecentActivities failed");
                return StatusCode(500, new { success = false, message = "เกิดข้อผิดพลาดในการโหลดประวัติการทำงานล่าสุด" });
            }
        }

        // ==========================================
        // 2. CLEANING TASKS (ค้นหา/กรอง/การ์ดและตารางงานทั้งหมด)
        // ==========================================

        public async Task<IActionResult> CleaningTasks(
            string? search, string? status, byte? floor, string? priority, DateOnly? dateFrom, DateOnly? dateTo, string? tab)
        {
            var userInfo = await GetValidHousekeeperAsync();
            if (userInfo == null) return Denied();
            var uid = userInfo.Value.UserId;

            var baseQuery = _context.HousekeepingTasks.AsNoTracking();

            // ดึงตัวเลข Summary Cards 4 ใบจากฐานข้อมูลจริง
            var totalTasksCount = await baseQuery.CountAsync();
            var pendingTasksCount = await baseQuery.CountAsync(t => t.Status == StatusPending);
            var inProgressTasksCount = await baseQuery.CountAsync(t => t.Status == StatusInProgress);
            var completedTasksCount = await baseQuery.CountAsync(t => t.Status == StatusDone);

            // ดึงรายการชั้นทั้งหมดที่มีจริงในระบบ
            var floors = await _context.Rooms
                .AsNoTracking()
                .Where(r => r.IsActive == true)
                .Select(r => r.FloorNo)
                .Distinct()
                .OrderBy(f => f)
                .ToListAsync();

            var statusFilter = NormalizeStatus(status);
            var query = baseQuery.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim().TrimStart('#');
                var isId = uint.TryParse(keyword, out var searchId);
                query = query.Where(t => t.Room.RoomNumber.Contains(keyword) ||
                                         (isId && t.TaskId == searchId));
            }

            if (floor.HasValue)
            {
                query = query.Where(t => t.Room.FloorNo == floor.Value);
            }

            if (status != null && status.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Room.Status == "maintenance");
            }
            else if (statusFilter != null)
            {
                query = query.Where(t => t.Status == statusFilter);
            }

            if (dateFrom.HasValue)
            {
                var from = dateFrom.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(t => t.CreatedAt >= from);
            }

            if (dateTo.HasValue)
            {
                var toExclusive = dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(t => t.CreatedAt < toExclusive);
            }

            var projectedList = await ProjectTasks(query.OrderByDescending(t => t.CreatedAt).Take(300), uid)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(priority))
            {
                projectedList = projectedList
                    .Where(t => string.Equals(t.Priority, priority, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // จัดเรียงตามลำดับสถานะที่ต้องทำก่อน -> ความเร่งด่วน -> เวลาที่ได้รับมอบหมาย
            var sortedAllTasks = projectedList
                .OrderBy(t => t.SortStatusOrder)
                .ThenBy(t => t.SortPriorityOrder)
                .ThenBy(t => t.CreatedAt)
                .ToList();

            var model = new HousekeepingViewModel
            {
                CurrentUserId = uid,
                CurrentUserName = userInfo.Value.FullName,
                CurrentDate = DateTime.Now,

                // Summary Cards 4 ใบ
                TotalTasks = totalTasksCount,
                PendingTasks = pendingTasksCount,
                InProgressTasks = inProgressTasksCount,
                CompletedTasks = completedTasksCount,

                // Filters
                SearchKeyword = search,
                StatusFilter = status,
                FloorFilter = floor,
                PriorityFilter = priority,
                DateFrom = dateFrom,
                DateTo = dateTo,
                AvailableFloors = floors,
                ActiveTab = tab == "completed" ? "completed" : "open",

                // Lists
                AllCleaningTasks = sortedAllTasks,
                OpenTasks = sortedAllTasks.Where(t => t.Status != StatusDone).ToList(),
                CompletedTaskList = sortedAllTasks.Where(t => t.Status == StatusDone)
                    .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
                    .ToList()
            };

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    summary = new
                    {
                        totalTasks = model.TotalTasks,
                        pendingTasks = model.PendingTasks,
                        inProgressTasks = model.InProgressTasks,
                        completedTasks = model.CompletedTasks
                    },
                    tasks = model.AllCleaningTasks
                });
            }

            return View(model);
        }

        // ==========================================
        // 3. TASK DETAILS (พร้อม Timeline เหตุการณ์จริงของงาน)
        // ==========================================

        public async Task<IActionResult> TaskDetails(uint id)
        {
            var userInfo = await GetValidHousekeeperAsync();
            if (userInfo == null) return Denied();
            var uid = userInfo.Value.UserId;

            var detail = await _context.HousekeepingTasks
                .AsNoTracking()
                .Where(t => t.TaskId == id)
                .Select(t => new HousekeepingViewModel.TaskDetailItem
                {
                    TaskId = t.TaskId,
                    RoomId = t.RoomId,
                    RoomNumber = t.Room.RoomNumber,
                    FloorNo = t.Room.FloorNo,
                    RoomTypeName = t.Room.RoomType.TypeName,
                    RoomStatus = t.Room.Status,
                    BookingId = t.BookingId,
                    BookingCode = t.Booking != null ? t.Booking.BookingCode : null,
                    Note = t.Note,
                    Status = t.Status,
                    AssignedTo = t.AssignedTo,
                    AssignedToName = t.AssignedToNavigation != null ? t.AssignedToNavigation.FullName : null,
                    IsMine = t.AssignedTo == uid,
                    CreatedAt = t.CreatedAt,
                    StartedAt = t.StartedAt,
                    CompletedAt = t.CompletedAt
                })
                .FirstOrDefaultAsync();

            if (detail == null)
            {
                if (IsAjaxRequest())
                {
                    return new JsonResult(new { success = false, message = "ไม่พบงานแม่บ้านที่ระบุ" }) { StatusCode = 404 };
                }
                return NotFound();
            }

            // สร้าง Timeline จาก Timestamp จริงในฐานข้อมูลเท่านั้น (ห้ามจำลองเวลา)
            var timeline = new List<HousekeepingViewModel.TaskTimelineEvent>
            {
                new HousekeepingViewModel.TaskTimelineEvent
                {
                    EventType = "assigned",
                    Title = $"ได้รับมอบหมายงานทำความสะอาดห้อง {detail.RoomNumber}",
                    Description = string.IsNullOrWhiteSpace(detail.BookingCode)
                        ? $"เปิดใบงานหมายเลข #{detail.TaskId}"
                        : $"เปิดใบงานหมายเลข #{detail.TaskId} (อ้างอิงรหัสจอง {detail.BookingCode})",
                    ActorName = detail.AssignedToName ?? "ระบบอัตโนมัติ",
                    Timestamp = detail.CreatedAt
                }
            };

            if (detail.StartedAt.HasValue)
            {
                timeline.Add(new HousekeepingViewModel.TaskTimelineEvent
                {
                    EventType = "started",
                    Title = $"เริ่มทำความสะอาดห้อง {detail.RoomNumber}",
                    Description = "เปลี่ยนสถานะงานเป็น In Progress (กำลังทำความสะอาด)",
                    ActorName = detail.AssignedToName ?? "พนักงานฝ่ายแม่บ้าน",
                    Timestamp = detail.StartedAt.Value
                });
            }

            // ดึงประวัติ RoomStatusHistory ที่เกี่ยวข้องกับห้องนี้ในช่วงเวลางาน
            var endWindow = (detail.CompletedAt ?? DateTime.Now).AddMinutes(10);
            var roomLogs = await _context.RoomStatusHistories
                .AsNoTracking()
                .Where(h => h.RoomId == detail.RoomId &&
                            h.ChangedAt >= detail.CreatedAt.AddMinutes(-5) &&
                            h.ChangedAt <= endWindow)
                .OrderBy(h => h.ChangedAt)
                .Select(h => new
                {
                    h.OldStatus,
                    h.NewStatus,
                    h.Note,
                    h.ChangedAt,
                    ActorName = h.ChangedByNavigation != null ? h.ChangedByNavigation.FullName : "ระบบ"
                })
                .ToListAsync();

            foreach (var log in roomLogs)
            {
                if (log.NewStatus == "maintenance")
                {
                    timeline.Add(new HousekeepingViewModel.TaskTimelineEvent
                    {
                        EventType = "maintenance",
                        Title = $"แจ้งปัญหาอุปกรณ์ / ซ่อมบำรุงห้อง {detail.RoomNumber}",
                        Description = log.Note ?? "เปลี่ยนสถานะห้องเป็น Maintenance",
                        ActorName = log.ActorName,
                        Timestamp = log.ChangedAt
                    });
                }
                else if (log.NewStatus == "available")
                {
                    timeline.Add(new HousekeepingViewModel.TaskTimelineEvent
                    {
                        EventType = "completed",
                        Title = $"ตรวจสอบความพร้อมห้อง {detail.RoomNumber} ผ่านเกณฑ์ (Available)",
                        Description = log.Note ?? "เปลี่ยนสถานะห้องเป็นพร้อมให้บริการ",
                        ActorName = log.ActorName,
                        Timestamp = log.ChangedAt
                    });
                }
            }

            if (detail.CompletedAt.HasValue)
            {
                timeline.Add(new HousekeepingViewModel.TaskTimelineEvent
                {
                    EventType = "completed",
                    Title = $"ทำความสะอาดห้อง {detail.RoomNumber} เสร็จสิ้น",
                    Description = detail.DurationMinutes.HasValue
                        ? $"บันทึกจบงานทำความสะอาด (ใช้เวลาทั้งหมด {detail.DurationMinutes.Value} นาที)"
                        : "บันทึกจบงานทำความสะอาดเรียบร้อย",
                    ActorName = detail.AssignedToName ?? "พนักงานฝ่ายแม่บ้าน",
                    Timestamp = detail.CompletedAt.Value
                });
            }

            detail.TimelineEvents = timeline.OrderBy(e => e.Timestamp).ToList();

            if (IsAjaxRequest())
            {
                return Json(new { success = true, data = detail });
            }

            return View(new HousekeepingViewModel
            {
                CurrentUserId = uid,
                CurrentUserName = userInfo.Value.FullName,
                Detail = detail
            });
        }

        // ==========================================
        // 4. ROOM STATUS (อัปเดตสถานะห้องพัก + Summary การ์ด + ประวัติจริง)
        // ==========================================

        public async Task<IActionResult> RoomStatus()
        {
            var userInfo = await GetValidHousekeeperAsync();
            if (userInfo == null) return Denied();
            var uid = userInfo.Value.UserId;

            var rooms = await ProjectRooms(
                    _context.Rooms.AsNoTracking()
                        .Where(r => r.IsActive == true)
                        .OrderBy(r => r.FloorNo).ThenBy(r => r.RoomNumber), uid)
                .ToListAsync();

            var histories = await _context.RoomStatusHistories
                .AsNoTracking()
                .OrderByDescending(h => h.ChangedAt)
                .Take(50)
                .Select(h => new HousekeepingViewModel.RoomHistoryItem
                {
                    HistoryId = h.HistoryId,
                    RoomId = h.RoomId,
                    RoomNumber = h.Room.RoomNumber,
                    FloorNo = h.Room.FloorNo,
                    OldStatus = h.OldStatus ?? string.Empty,
                    NewStatus = h.NewStatus,
                    ChangedByName = h.ChangedByNavigation != null ? h.ChangedByNavigation.FullName : "ระบบ",
                    ChangedAt = h.ChangedAt,
                    Note = h.Note
                })
                .ToListAsync();

            var floors = rooms.Select(r => r.FloorNo).Distinct().OrderBy(f => f).ToList();
            var roomTypes = rooms
                .Where(r => !string.IsNullOrWhiteSpace(r.RoomTypeName))
                .Select(r => r.RoomTypeName!)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            var model = new HousekeepingViewModel
            {
                CurrentUserId = uid,
                CurrentUserName = userInfo.Value.FullName,
                CurrentDate = DateTime.Now,
                Rooms = rooms,
                TotalRooms = rooms.Count,
                AvailableRooms = rooms.Count(r => r.ManagementState == "available"),
                OccupiedRooms = rooms.Count(r => r.ManagementState == "occupied"),
                NeedsCleaningRooms = rooms.Count(r => r.Status == "needs_cleaning"),
                PendingCleaningRooms = rooms.Count(r => r.ManagementState == "pending_cleaning"),
                InProgressCleaningRooms = rooms.Count(r => r.ManagementState == "in_progress"),
                MaintenanceRooms = rooms.Count(r => r.ManagementState == "maintenance"),
                AvailableFloors = floors,
                AvailableRoomTypes = roomTypes,
                RoomStatusHistories = histories
            };

            return View(model);
        }

        // ==========================================
        // 5. UPDATE ROOM STATUS WORKFLOW (ควบคุมเงื่อนไขทั้ง 8 ข้อ + Checklist)
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRoomStatusWorkflow(
            uint roomId,
            string targetAction,
            string? note,
            bool checkBedLinens = false,
            bool checkBathroom = false,
            bool checkAmenities = false,
            bool checkAppliances = false)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;
                var actorName = userInfo.Value.FullName;

                var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
                if (cleanNote != null && cleanNote.Length > 240)
                {
                    cleanNote = cleanNote.Substring(0, 240);
                }

                await using var tx = await _context.Database.BeginTransactionAsync();

                var room = await _context.Rooms
                    .FirstOrDefaultAsync(r => r.RoomId == roomId && r.IsActive == true);

                if (room == null)
                {
                    await tx.RollbackAsync();
                    return Respond(false, "ไม่พบข้อมูลห้องพักที่ระบุในระบบ");
                }

                var hasActiveStay = await _context.StayRecords
                    .AnyAsync(s => s.RoomId == room.RoomId && s.CheckedOutAt == null);

                // กฎข้อ 1 และ 2: ห้ามเปลี่ยนห้องที่มีลูกค้าเข้าพัก (Occupied) เป็น Available หรือ Pending Cleaning โดยเด็ดขาด
                if (room.Status == "occupied" || hasActiveStay)
                {
                    await tx.RollbackAsync();
                    return Respond(false, $"ห้อง {room.RoomNumber} มีลูกค้าเข้าพักอยู่ (Occupied) ฝ่ายแม่บ้านไม่สามารถเปลี่ยนสถานะห้องนี้ได้");
                }

                var openTask = await _context.HousekeepingTasks
                    .Where(t => t.RoomId == room.RoomId && t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .FirstOrDefaultAsync();

                var now = DateTime.Now;
                var actionKey = (targetAction ?? "").Trim().ToLowerInvariant();

                // ---------------------------------------------------------
                // CASE A: เริ่มทำความสะอาด (Pending Cleaning -> In Progress)
                // ---------------------------------------------------------
                if (actionKey == "start_cleaning")
                {
                    if (room.Status == "maintenance")
                    {
                        await tx.RollbackAsync();
                        return Respond(false, $"ห้อง {room.RoomNumber} อยู่ในสถานะซ่อมบำรุง ต้องยืนยันการซ่อมเสร็จก่อนเริ่มทำความสะอาด");
                    }

                    var oldRoomStatus = room.Status;
                    if (room.Status != "needs_cleaning")
                    {
                        room.Status = "needs_cleaning";
                    }

                    if (openTask == null)
                    {
                        openTask = new HousekeepingTask
                        {
                            RoomId = room.RoomId,
                            Status = StatusInProgress,
                            AssignedTo = uid,
                            CreatedAt = now,
                            StartedAt = now,
                            Note = cleanNote ?? "แม่บ้านเปิดงานและเริ่มทำความสะอาด"
                        };
                        _context.HousekeepingTasks.Add(openTask);
                    }
                    else
                    {
                        if (openTask.AssignedTo != null && openTask.AssignedTo != uid)
                        {
                            await tx.RollbackAsync();
                            return Respond(false, "งานทำความสะอาดห้องนี้มีพนักงานท่านอื่นรับผิดชอบอยู่แล้ว");
                        }
                        openTask.AssignedTo = uid;
                        openTask.Status = StatusInProgress;
                        openTask.StartedAt ??= now;
                        if (!string.IsNullOrWhiteSpace(cleanNote))
                        {
                            openTask.Note = AppendNote(openTask.Note, cleanNote);
                        }
                    }

                    _context.RoomStatusHistories.Add(new RoomStatusHistory
                    {
                        RoomId = room.RoomId,
                        OldStatus = oldRoomStatus,
                        NewStatus = "needs_cleaning",
                        ChangedBy = uid,
                        ChangedAt = now,
                        Note = string.IsNullOrWhiteSpace(cleanNote)
                            ? $"เริ่มทำความสะอาดห้อง {room.RoomNumber} (In Progress)"
                            : $"เริ่มทำความสะอาด: {cleanNote}"
                    });

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Respond(true, $"เริ่มทำความสะอาดห้อง {room.RoomNumber} เรียบร้อยแล้ว", new
                    {
                        roomId = room.RoomId,
                        roomNumber = room.RoomNumber,
                        managementState = "in_progress",
                        updatedBy = actorName,
                        updatedAt = now.ToString("dd MMM yyyy HH:mm")
                    });
                }

                // ---------------------------------------------------------
                // CASE B: ทำความสะอาดเสร็จสิ้น + ตรวจ Checklist (In Progress -> Completed & Available)
                // ---------------------------------------------------------
                if (actionKey == "complete_cleaning")
                {
                    if (room.Status == "maintenance")
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "ห้องที่อยู่ในสถานะ Maintenance ไม่สามารถเปลี่ยนเป็น Available โดยตรงได้");
                    }

                    if (openTask == null || openTask.Status != StatusInProgress)
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "ต้องอยู่ในสถานะ 'กำลังทำความสะอาด (In Progress)' ก่อนจึงจะบันทึกเสร็จสิ้นได้");
                    }

                    if (openTask.AssignedTo != null && openTask.AssignedTo != uid)
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "คุณไม่ใช่ผู้รับผิดชอบงานทำความสะอาดของห้องนี้");
                    }

                    // ตรวจสอบ Checklist ความพร้อมทั้ง 4 ข้อ
                    if (!checkBedLinens || !checkBathroom || !checkAmenities || !checkAppliances)
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "กรุณาติ๊กยืนยัน Checklist การตรวจสอบความพร้อมของห้องพักให้ครบทุกข้อก่อนปล่อยห้องเป็น Available");
                    }

                    openTask.AssignedTo = uid;
                    openTask.Status = StatusDone;
                    openTask.CompletedAt = now;

                    // เติมเครื่องหมาย [ตรวจห้องผ่าน] เสมอ เพื่อใช้เป็นหลักฐานว่าผ่าน Checklist จริง (แม้ไม่ได้กรอกหมายเหตุ)
                    var passNote = string.IsNullOrWhiteSpace(cleanNote)
                        ? ChecklistPassedMarker
                        : $"{ChecklistPassedMarker} {cleanNote}";
                    openTask.Note = AppendNote(openTask.Note, passNote);

                    // ตรวจสอบว่าไม่มีงานอื่นค้างในห้องเดียวกัน
                    var hasOtherOpen = await _context.HousekeepingTasks
                        .AnyAsync(t => t.RoomId == room.RoomId && t.TaskId != openTask.TaskId && t.Status != StatusDone);

                    var oldStatus = room.Status;
                    if (!hasOtherOpen)
                    {
                        room.Status = "available";
                        _context.RoomStatusHistories.Add(new RoomStatusHistory
                        {
                            RoomId = room.RoomId,
                            OldStatus = oldStatus,
                            NewStatus = "available",
                            BookingId = openTask.BookingId,
                            ChangedBy = uid,
                            ChangedAt = now,
                            Note = string.IsNullOrWhiteSpace(cleanNote)
                                ? $"ทำความสะอาดเสร็จและผ่าน Checklist ครบถ้วน (งาน #{openTask.TaskId})"
                                : $"ทำความสะอาดเสร็จ (ผ่าน Checklist): {cleanNote}"
                        });
                    }

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Respond(true,
                        hasOtherOpen
                            ? $"ปิดงานห้อง {room.RoomNumber} เรียบร้อย แต่ยังมีงานอื่นค้างในห้องนี้ จึงยังไม่เปลี่ยนเป็น Available"
                            : $"ทำความสะอาดห้อง {room.RoomNumber} เสร็จสิ้น และเปลี่ยนสถานะห้องเป็น Available พร้อมให้บริการแล้ว",
                        new
                        {
                            roomId = room.RoomId,
                            roomNumber = room.RoomNumber,
                            managementState = hasOtherOpen ? "pending_cleaning" : "available",
                            updatedBy = actorName,
                            updatedAt = now.ToString("dd MMM yyyy HH:mm")
                        });
                }

                // ---------------------------------------------------------
                // CASE C: แจ้งปัญหาอุปกรณ์ชำรุด (-> Maintenance)
                // ---------------------------------------------------------
                if (actionKey == "report_maintenance")
                {
                    if (string.IsNullOrWhiteSpace(cleanNote))
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "กรุณาระบุรายละเอียดปัญหาหรืออุปกรณ์ที่ชำรุดในช่องหมายเหตุ");
                    }

                    if (room.Status == "maintenance")
                    {
                        await tx.RollbackAsync();
                        return Respond(false, $"ห้อง {room.RoomNumber} อยู่ในสถานะซ่อมบำรุงอยู่แล้ว");
                    }

                    var oldStatus = room.Status;
                    room.Status = "maintenance";

                    _context.RoomStatusHistories.Add(new RoomStatusHistory
                    {
                        RoomId = room.RoomId,
                        OldStatus = oldStatus,
                        NewStatus = "maintenance",
                        ChangedBy = uid,
                        ChangedAt = now,
                        Note = $"แจ้งซ่อมบำรุง (Maintenance): {cleanNote}"
                    });

                    if (openTask != null)
                    {
                        openTask.Note = AppendNote(openTask.Note, $"[แจ้งซ่อม: {cleanNote}]");
                    }

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Respond(true, $"เปลี่ยนสถานะห้อง {room.RoomNumber} เป็น Maintenance (ระงับการขายชั่วคราว) เรียบร้อยแล้ว", new
                    {
                        roomId = room.RoomId,
                        roomNumber = room.RoomNumber,
                        managementState = "maintenance",
                        updatedBy = actorName,
                        updatedAt = now.ToString("dd MMM yyyy HH:mm")
                    });
                }

                // ---------------------------------------------------------
                // CASE D: ซ่อมบำรุงเสร็จสิ้น -> ส่งเข้าขั้นตอนรอทำความสะอาดและตรวจสอบ (Maintenance -> Pending Cleaning)
                // กฎข้อ 6: ห้อง Maintenance ห้ามข้ามไปเป็น Available โดยตรง ต้องผ่านการทำความสะอาดและตรวจความพร้อมก่อน
                // ---------------------------------------------------------
                if (actionKey == "resolve_maintenance")
                {
                    if (room.Status != "maintenance")
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "ห้องนี้ไม่ได้อยู่ในสถานะซ่อมบำรุง");
                    }

                    if (string.IsNullOrWhiteSpace(cleanNote))
                    {
                        await tx.RollbackAsync();
                        return Respond(false, "กรุณาระบุรายละเอียดการแก้ไขปัญหาซ่อมบำรุงก่อนส่งต่อทำความสะอาด");
                    }

                    room.Status = "needs_cleaning";

                    if (openTask == null)
                    {
                        _context.HousekeepingTasks.Add(new HousekeepingTask
                        {
                            RoomId = room.RoomId,
                            Status = StatusPending,
                            AssignedTo = uid,
                            CreatedAt = now,
                            Note = $"ทำความสะอาดหลังซ่อมบำรุงเสร็จ: {cleanNote}"
                        });
                    }

                    _context.RoomStatusHistories.Add(new RoomStatusHistory
                    {
                        RoomId = room.RoomId,
                        OldStatus = "maintenance",
                        NewStatus = "needs_cleaning",
                        ChangedBy = uid,
                        ChangedAt = now,
                        Note = $"ซ่อมบำรุงเสร็จสิ้น ส่งต่อทำความสะอาด: {cleanNote}"
                    });

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Respond(true, $"บันทึกการซ่อมห้อง {room.RoomNumber} เสร็จสิ้น และส่งห้องเข้าสู่สถานะ Pending Cleaning เพื่อทำความสะอาดก่อนเปิดขาย", new
                    {
                        roomId = room.RoomId,
                        roomNumber = room.RoomNumber,
                        managementState = "pending_cleaning",
                        updatedBy = actorName,
                        updatedAt = now.ToString("dd MMM yyyy HH:mm")
                    });
                }

                await tx.RollbackAsync();
                return Respond(false, "ไม่พบรูปแบบคำสั่งเปลี่ยนสถานะที่ระบุ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateRoomStatusWorkflow failed (roomId={RoomId}, action={Action})", roomId, targetAction);
                return Respond(false, "เกิดข้อผิดพลาดในการอัปเดตสถานะห้องพัก กรุณาลองใหม่อีกครั้ง");
            }
        }

        // ==========================================
        // 6. CLEANING HISTORY MANAGEMENT (ประวัติงานทำความสะอาด + KPI + Performance)
        // ==========================================

        [HttpGet]
        public IActionResult CleaningHistory(
            string? search, byte? floor, uint? employeeId, string? status,
            DateOnly? singleDate, DateOnly? dateFrom, DateOnly? dateTo, string? issueType)
        {
            return RedirectToAction(nameof(History), new
            {
                search,
                floor,
                employeeId,
                status,
                singleDate,
                dateFrom,
                dateTo,
                issueType
            });
        }

        public async Task<IActionResult> History(
            string? search,
            byte? floor,
            uint? employeeId,
            string? status,
            DateOnly? singleDate,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            string? issueType)
        {
            var userInfo = await GetValidHousekeeperAsync();
            if (userInfo == null) return Denied();
            var uid = userInfo.Value.UserId;

            var todayStart = DateTime.Today;
            var tomorrowStart = todayStart.AddDays(1);

            var allTasksQuery = _context.HousekeepingTasks.AsNoTracking();

            // ---------------------------------------------------------
            // 1. คำนวณ Summary Cards 4 ใบจากฐานข้อมูลจริง
            // ---------------------------------------------------------
            var totalCompletedAll = await allTasksQuery.CountAsync(t => t.Status == StatusDone);

            var completedTodayCount = await allTasksQuery.CountAsync(t =>
                t.Status == StatusDone &&
                t.CompletedAt.HasValue &&
                t.CompletedAt.Value >= todayStart &&
                t.CompletedAt.Value < tomorrowStart);

            // คำนวณเวลาเฉลี่ยจริง (เฉพาะงานที่มีทั้ง StartedAt และ CompletedAt)
            var durationSamples = await allTasksQuery
                .Where(t => t.Status == StatusDone &&
                            t.StartedAt.HasValue &&
                            t.CompletedAt.HasValue &&
                            t.CompletedAt.Value >= t.StartedAt.Value)
                .OrderByDescending(t => t.CompletedAt)
                .Take(500)
                .Select(t => new { Start = t.StartedAt!.Value, End = t.CompletedAt!.Value })
                .ToListAsync();

            double avgMinutes = 0;
            int onTimeCount = 0;
            if (durationSamples.Count > 0)
            {
                var mins = durationSamples
                    .Select(x => (x.End - x.Start).TotalMinutes)
                    .Where(m => m >= 0 && m <= 720) // กรองค่าผิดปกติที่เกิน 12 ชม.
                    .ToList();

                if (mins.Count > 0)
                {
                    avgMinutes = Math.Round(mins.Average(), 1);
                    onTimeCount = mins.Count(m => m <= 45.0);
                }
            }

            // จำนวนรายการแจ้งปัญหา (จาก RoomStatusHistories ที่เปลี่ยนเป็น maintenance หรือหมายเหตุแจ้งซ่อม)
            var maintenanceLogCount = await _context.RoomStatusHistories
                .AsNoTracking()
                .CountAsync(h => h.NewStatus == "maintenance");

            var taskIssueCount = await allTasksQuery
                .CountAsync(t => t.Room.Status == "maintenance" ||
                                 (t.Note != null && (t.Note.Contains("แจ้งซ่อม") || t.Note.Contains("ชำรุด") || t.Note.Contains("เสีย"))));

            var reportedIssuesTotal = Math.Max(maintenanceLogCount, taskIssueCount);

            // ---------------------------------------------------------
            // 2. ดึงตัวเลือก Dropdown สำหรับ Filter (ชั้น และ รายชื่อพนักงานแม่บ้าน)
            // ---------------------------------------------------------
            var floors = await _context.Rooms
                .AsNoTracking()
                .Where(r => r.IsActive == true)
                .Select(r => r.FloorNo)
                .Distinct()
                .OrderBy(f => f)
                .ToListAsync();

            var housekeepers = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive == true &&
                            (u.UserRoles.Any(ur => ur.Role.RoleCode == HousekeepingRole) ||
                             u.HousekeepingTasks.Any()))
                .OrderBy(u => u.FullName)
                .Select(u => new HousekeepingViewModel.HousekeeperOption
                {
                    UserId = u.UserId,
                    FullName = u.FullName
                })
                .ToListAsync();

            // ---------------------------------------------------------
            // 3. กรองรายการประวัติ (Search & Filter)
            // ---------------------------------------------------------
            var query = ApplyHistoryFilters(
                allTasksQuery, search, floor, employeeId, status, singleDate, dateFrom, dateTo, issueType);

            var historyList = await ProjectTasks(
                    query.OrderByDescending(t => t.CompletedAt ?? t.StartedAt ?? t.CreatedAt).Take(500), uid)
                .ToListAsync();

            // ---------------------------------------------------------
            // 4. คำนวณข้อมูลกราฟประสิทธิภาพย้อนหลัง 7 วันจริง (Daily Performance)
            // ---------------------------------------------------------
            var sevenDaysAgo = todayStart.AddDays(-6);
            var recent7DaysTasks = await allTasksQuery
                .Where(t => (t.CompletedAt ?? t.CreatedAt) >= sevenDaysAgo)
                .Select(t => new
                {
                    Date = (t.CompletedAt ?? t.CreatedAt).Date,
                    t.Status,
                    t.StartedAt,
                    t.CompletedAt,
                    IsIssue = t.Room.Status == "maintenance" ||
                              (t.Note != null && (t.Note.Contains("แจ้งซ่อม") || t.Note.Contains("ชำรุด") || t.Note.Contains("เสีย")))
                })
                .ToListAsync();

            var dailyStats = new List<HousekeepingViewModel.DailyPerformanceItem>();
            var thaiCulture = new System.Globalization.CultureInfo("th-TH");

            for (int i = 0; i < 7; i++)
            {
                var d = sevenDaysAgo.AddDays(i);
                var dayItems = recent7DaysTasks.Where(x => x.Date == d).ToList();
                var completedDay = dayItems.Count(x => x.Status == StatusDone);
                var issueDay = dayItems.Count(x => x.IsIssue);

                var validDurations = dayItems
                    .Where(x => x.Status == StatusDone && x.StartedAt.HasValue && x.CompletedAt.HasValue && x.CompletedAt.Value >= x.StartedAt.Value)
                    .Select(x => (x.CompletedAt!.Value - x.StartedAt!.Value).TotalMinutes)
                    .Where(m => m >= 0 && m <= 720)
                    .ToList();

                dailyStats.Add(new HousekeepingViewModel.DailyPerformanceItem
                {
                    DateLabel = d.ToString("dd MMM", thaiCulture),
                    CompletedCount = completedDay,
                    IssueCount = issueDay,
                    AvgMinutes = validDurations.Count > 0 ? Math.Round(validDurations.Average(), 1) : 0
                });
            }

            var avgRoomsPerDay = dailyStats.Count > 0
                ? (int)Math.Round(dailyStats.Average(x => x.CompletedCount))
                : 0;

            var timelineActivities = await BuildRecentActivitiesAsync(15);

            var model = new HousekeepingViewModel
            {
                CurrentUserId = uid,
                CurrentUserName = userInfo.Value.FullName,
                CurrentDate = DateTime.Now,

                // Summary Cards 4 ใบ
                HistoryTotalCompleted = totalCompletedAll,
                HistoryCompletedToday = completedTodayCount,
                AverageCleaningMinutes = avgMinutes,
                ReportedIssuesCount = reportedIssuesTotal,

                // Filter Values
                SearchKeyword = search,
                FloorFilter = floor,
                EmployeeFilter = employeeId,
                StatusFilter = status,
                SingleDateFilter = singleDate,
                DateFrom = dateFrom,
                DateTo = dateTo,
                IssueTypeFilter = issueType,
                AvailableFloors = floors,
                HousekeeperList = housekeepers,

                // Table & Performance
                HistoryTasks = historyList,
                HistoryTimeline = timelineActivities,
                RoomsCleanedPerDayAvg = avgRoomsPerDay,
                OnTimeCompletedCount = onTimeCount,
                DailyPerformanceStats = dailyStats
            };

            return View(model);
        }

        // ==========================================
        // 6.1 EXPORT CLEANING HISTORY (CSV เปิดใน Excel ได้)
        // เดิม JS เรียก /ExportCleaningHistory แต่ Controller ไม่มี action นี้ -> 404
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> ExportCleaningHistory(
            string? search, byte? floor, uint? employeeId, string? status,
            DateOnly? singleDate, DateOnly? dateFrom, DateOnly? dateTo, string? issueType)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();

                var query = ApplyHistoryFilters(
                    _context.HousekeepingTasks.AsNoTracking(),
                    search, floor, employeeId, status, singleDate, dateFrom, dateTo, issueType);

                var rows = await ProjectTasks(
                        query.OrderByDescending(t => t.CompletedAt ?? t.StartedAt ?? t.CreatedAt).Take(5000),
                        userInfo.Value.UserId)
                    .ToListAsync();

                var sb = new StringBuilder();
                sb.AppendLine("TaskId,Room,Floor,RoomType,TaskStatus,AssignedTo,CreatedAt,StartedAt,CompletedAt,DurationMinutes,HasIssue,PassedChecklist,Note");
                foreach (var r in rows)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(r.TaskId.ToString()),
                        Csv(r.RoomNumber),
                        Csv(r.FloorNo.ToString()),
                        Csv(r.RoomTypeName),
                        Csv(r.StatusLabel),
                        Csv(r.AssignedToName),
                        Csv(r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(r.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(r.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(r.DurationMinutes?.ToString()),
                        Csv(r.HasIssue ? "Y" : "N"),
                        Csv(r.IsCompletedPassedChecklist ? "Y" : "N"),
                        Csv(r.Note)
                    }));
                }

                // UTF-8 BOM เพื่อให้ Excel อ่านภาษาไทยถูกต้อง
                var bytes = new UTF8Encoding(true).GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
                    .ToArray();

                return File(bytes, "text/csv; charset=utf-8", $"cleaning-history-{DateTime.Now:yyyyMMdd-HHmm}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExportCleaningHistory failed");
                TempData["ErrorMessage"] = "ไม่สามารถส่งออกรายงานได้ กรุณาลองใหม่อีกครั้ง";
                return RedirectToAction(nameof(History));
            }
        }

        // ==========================================
        // 7. PROFILE
        // ==========================================

        public async Task<IActionResult> Profile()
        {
            var userInfo = await GetValidHousekeeperAsync();
            if (userInfo == null) return Denied();
            var uid = userInfo.Value.UserId;

            var employee = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == uid)
                .Select(u => new HousekeepingViewModel.EmployeeInfo
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Phone = u.Phone,
                    IsActive = u.IsActive == true,
                    CreatedAt = u.CreatedAt,
                    RoleName = u.UserRoles
                        .Where(ur => ur.Role.RoleCode == HousekeepingRole)
                        .Select(ur => ur.Role.RoleName)
                        .FirstOrDefault() ?? "",
                    CompletedTaskCount = u.HousekeepingTasks.Count(t => t.Status == StatusDone),
                    ActiveTaskCount = u.HousekeepingTasks.Count(t => t.Status != StatusDone)
                })
                .FirstOrDefaultAsync();

            if (employee == null) return Denied();

            return View(new HousekeepingViewModel
            {
                CurrentUserId = uid,
                CurrentUserName = employee.FullName,
                Employee = employee
            });
        }

        // 7.1 UPDATE PROFILE (แก้ไขได้เฉพาะชื่อ และเบอร์โทร)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, string? phone)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                var cleanName = (fullName ?? "").Trim();
                var cleanPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

                if (cleanName.Length == 0)
                {
                    TempData["ErrorMessage"] = "กรุณากรอกชื่อ";
                    return RedirectToAction(nameof(Profile));
                }
                if (cleanName.Length > 120) cleanName = cleanName.Substring(0, 120);

                if (cleanPhone != null &&
                    !System.Text.RegularExpressions.Regex.IsMatch(cleanPhone, @"^[0-9+\-\s]{6,20}$"))
                {
                    TempData["ErrorMessage"] = "รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง";
                    return RedirectToAction(nameof(Profile));
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == uid);
                if (user == null) return Denied();

                user.FullName = cleanName;
                user.Phone = cleanPhone;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "บันทึกข้อมูลส่วนตัวเรียบร้อยแล้ว";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateProfile failed");
                TempData["ErrorMessage"] = "เกิดข้อผิดพลาดในการบันทึกข้อมูล กรุณาลองใหม่อีกครั้ง";
            }

            return RedirectToAction(nameof(Profile));
        }

        // 7.2 CHANGE PASSWORD
        // *** สมมติว่า Users มี property PasswordHash และใช้ BCrypt.Net-Next ***
        // *** ถ้าโปรเจกต์ใช้วิธี hash อื่น (เช่น PasswordHasher<T>) ให้แก้ 2 จุดที่มีคอมเมนต์ ***
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, string confirmPassword)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                if (string.IsNullOrWhiteSpace(oldPassword) ||
                    string.IsNullOrWhiteSpace(newPassword) ||
                    newPassword.Length < 6)
                {
                    TempData["ErrorMessage"] = "กรุณากรอกข้อมูลให้ครบ และรหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                if (newPassword != confirmPassword)
                {
                    TempData["ErrorMessage"] = "รหัสผ่านใหม่และการยืนยันรหัสผ่านไม่ตรงกัน";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == uid);
                if (user == null) return Denied();

                // (1) ตรวจรหัสผ่านเดิม — แก้ตามวิธี hash ที่ใช้ใน AccountController
                if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
                {
                    TempData["ErrorMessage"] = "รหัสผ่านเดิมไม่ถูกต้อง";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                // (2) บันทึกรหัสผ่านใหม่
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChangePassword failed");
                TempData["ErrorMessage"] = "เกิดข้อผิดพลาดในการเปลี่ยนรหัสผ่าน กรุณาลองใหม่อีกครั้ง";
                TempData["OpenPasswordModal"] = true;
            }

            return RedirectToAction(nameof(Profile));
        }

        // ==========================================
        // 8. ACCEPT TASK (Pending, ยังไม่มีผู้รับ -> บันทึก AssignedTo)
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptTask(uint taskId)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                var check = await PreCheckAsync(taskId, uid, StatusPending, isAccept: true);
                if (check.Error != null) return Respond(false, check.Error);

                var affected = await _context.HousekeepingTasks
                    .Where(t => t.TaskId == taskId && t.Status == StatusPending && t.AssignedTo == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssignedTo, (uint?)uid));

                if (affected == 0)
                {
                    return Respond(false, "งานนี้ถูกรับไปแล้วหรือสถานะเปลี่ยนไป กรุณารีเฟรชหน้า");
                }

                return Respond(true, $"รับงานห้อง {check.RoomNumber} เรียบร้อยแล้ว", new { taskId, status = StatusPending });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AcceptTask failed (taskId={TaskId})", taskId);
                return Respond(false, "เกิดข้อผิดพลาดขณะบันทึกการรับงาน กรุณาลองใหม่อีกครั้ง");
            }
        }

        // ==========================================
        // 9. START TASK (Pending -> In Progress, รองรับ Auto-Assign หากยังไม่มีผู้รับ)
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartTask(uint taskId)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                var taskSnap = await _context.HousekeepingTasks
                    .AsNoTracking()
                    .Where(x => x.TaskId == taskId)
                    .Select(x => new { x.Status, x.AssignedTo, RoomNumber = x.Room.RoomNumber, RoomStatus = x.Room.Status })
                    .FirstOrDefaultAsync();

                if (taskSnap == null) return Respond(false, "ไม่พบงานแม่บ้านที่ระบุ");
                if (taskSnap.Status == StatusDone) return Respond(false, "งานนี้ทำความสะอาดเสร็จสิ้นแล้ว");
                if (taskSnap.Status != StatusPending)
                {
                    return Respond(false, $"ไม่สามารถเริ่มงานได้ สถานะปัจจุบันคือ {HousekeepingViewModel.ToStatusLabel(taskSnap.Status)}");
                }
                if (taskSnap.AssignedTo != null && taskSnap.AssignedTo != uid)
                {
                    return Respond(false, "งานนี้มีพนักงานท่านอื่นรับผิดชอบอยู่แล้ว");
                }
                // ห้องที่อยู่ระหว่างซ่อมบำรุงต้องซ่อมให้เสร็จก่อน จึงจะเริ่มทำความสะอาดได้ (ตรงกับ CASE A ใน UpdateRoomStatusWorkflow)
                if (taskSnap.RoomStatus == "maintenance")
                {
                    return Respond(false, $"ห้อง {taskSnap.RoomNumber} อยู่ในสถานะซ่อมบำรุง ต้องยืนยันการซ่อมเสร็จก่อนเริ่มทำความสะอาด");
                }

                var now = DateTime.Now;
                var affected = await _context.HousekeepingTasks
                    .Where(t => t.TaskId == taskId &&
                                t.Status == StatusPending &&
                                (t.AssignedTo == null || t.AssignedTo == uid))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.AssignedTo, (uint?)uid)
                        .SetProperty(t => t.Status, StatusInProgress)
                        .SetProperty(t => t.StartedAt, (DateTime?)now));

                if (affected == 0)
                {
                    return Respond(false, "ไม่สามารถเริ่มงานได้ สถานะงานเปลี่ยนไป กรุณารีเฟรชหน้า");
                }

                return Respond(true, $"เริ่มทำความสะอาดห้อง {taskSnap.RoomNumber} เรียบร้อยแล้ว",
                    new
                    {
                        taskId,
                        status = StatusInProgress,
                        statusLabel = "In Progress",
                        statusThaiLabel = "กำลังทำความสะอาด",
                        assignedToName = userInfo.Value.FullName,
                        startedAt = now.ToString("yyyy-MM-ddTHH:mm:ss")
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartTask failed (taskId={TaskId})", taskId);
                return Respond(false, "เกิดข้อผิดพลาดขณะเริ่มงานทำความสะอาด กรุณาลองใหม่อีกครั้ง");
            }
        }

        // ==========================================
        // 10. COMPLETE TASK (In Progress -> Done + ตรวจเงื่อนไขก่อนปล่อยห้อง)
        // หมายเหตุ: Action นี้ "ไม่ผ่าน Checklist" (ใช้จากปุ่มในหน้า Cleaning Tasks)
        //           ถ้าต้องการบังคับ Checklist ให้ใช้ UpdateRoomStatusWorkflow(complete_cleaning) จากหน้า Task Details / Room Status
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteTask(uint taskId)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                var check = await PreCheckAsync(taskId, uid, StatusInProgress, isAccept: false);
                if (check.Error != null) return Respond(false, check.Error);

                var now = DateTime.Now;

                await using var tx = await _context.Database.BeginTransactionAsync();

                var affected = await _context.HousekeepingTasks
                    .Where(t => t.TaskId == taskId && t.Status == StatusInProgress && t.AssignedTo == uid)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Status, StatusDone)
                        .SetProperty(t => t.CompletedAt, (DateTime?)now));

                if (affected == 0)
                {
                    await tx.RollbackAsync();
                    return Respond(false, "ไม่สามารถปิดงานได้ สถานะงานเปลี่ยนไป กรุณารีเฟรชหน้า");
                }

                var taskInfo = await _context.HousekeepingTasks
                    .AsNoTracking()
                    .Where(t => t.TaskId == taskId)
                    .Select(t => new { t.RoomId, t.BookingId })
                    .FirstAsync();

                var room = await _context.Rooms.FirstOrDefaultAsync(r => r.RoomId == taskInfo.RoomId);

                var roomReleased = false;
                string roomMessage;

                if (room == null)
                {
                    roomMessage = "ไม่พบห้องพักของงานนี้ จึงไม่ได้เปลี่ยนสถานะห้อง";
                }
                else if (room.IsActive != true)
                {
                    roomMessage = "ห้องนี้ถูกปิดใช้งาน จึงไม่ได้เปลี่ยนสถานะห้อง";
                }
                else if (room.Status != "needs_cleaning")
                {
                    roomMessage = $"สถานะห้องปัจจุบันคือ '{HousekeepingViewModel.ToRoomStatusLabel(room.Status)}' จึงไม่ได้เปลี่ยนสถานะห้อง";
                }
                else
                {
                    var hasActiveStay = await _context.StayRecords
                        .AnyAsync(s => s.RoomId == room.RoomId && s.CheckedOutAt == null);

                    var hasOtherOpenTasks = await _context.HousekeepingTasks
                        .AnyAsync(t => t.RoomId == room.RoomId && t.TaskId != taskId && t.Status != StatusDone);

                    if (hasActiveStay)
                    {
                        roomMessage = "ห้องยังมีผู้เข้าพักอยู่ จึงยังไม่เปลี่ยนสถานะเป็นว่าง";
                    }
                    else if (hasOtherOpenTasks)
                    {
                        roomMessage = "ห้องยังมีงานแม่บ้านอื่นค้างอยู่ จึงยังไม่เปลี่ยนสถานะเป็นว่าง";
                    }
                    else
                    {
                        room.Status = "available";

                        _context.RoomStatusHistories.Add(new RoomStatusHistory
                        {
                            RoomId = room.RoomId,
                            OldStatus = "needs_cleaning",
                            NewStatus = "available",
                            BookingId = taskInfo.BookingId,
                            ChangedBy = uid,
                            ChangedAt = now,
                            Note = $"แม่บ้านทำความสะอาดเสร็จ (งานเลขที่ #{taskId})"
                        });

                        roomReleased = true;
                        roomMessage = "เปลี่ยนสถานะห้องเป็นว่าง (Available) พร้อมขายแล้ว";
                    }
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Respond(true, $"ปิดงานห้อง {check.RoomNumber} สำเร็จ — {roomMessage}",
                    new { taskId, status = StatusDone, completedAt = now, roomReleased });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompleteTask failed (taskId={TaskId})", taskId);
                return Respond(false, "เกิดข้อผิดพลาดขณะบันทึกปิดงาน กรุณาลองใหม่อีกครั้ง");
            }
        }

        // ==========================================
        // 11. REPORT MAINTENANCE (แจ้งอุปกรณ์ชำรุด / ระงับการขายห้อง)
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReportMaintenance(uint roomId, uint? taskId, string issueNote)
        {
            try
            {
                var userInfo = await GetValidHousekeeperAsync();
                if (userInfo == null) return Denied();
                var uid = userInfo.Value.UserId;

                if (string.IsNullOrWhiteSpace(issueNote))
                {
                    return Respond(false, "กรุณาระบุรายละเอียดอุปกรณ์ชำรุดหรือปัญหาที่พบ");
                }

                var cleanNote = issueNote.Trim();
                if (cleanNote.Length > 220) cleanNote = cleanNote.Substring(0, 220);

                await using var tx = await _context.Database.BeginTransactionAsync();

                var room = await _context.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId && r.IsActive == true);
                if (room == null)
                {
                    await tx.RollbackAsync();
                    return Respond(false, "ไม่พบข้อมูลห้องพักที่ระบุ");
                }

                // ห้ามระงับห้องหรือเปลี่ยนสถานะห้องที่มีลูกค้าเข้าพักอยู่
                var hasActiveStay = await _context.StayRecords
                    .AnyAsync(s => s.RoomId == room.RoomId && s.CheckedOutAt == null);

                if (room.Status == "occupied" || hasActiveStay)
                {
                    await tx.RollbackAsync();
                    return Respond(false, $"ห้อง {room.RoomNumber} มีลูกค้าเข้าพักอยู่ ไม่สามารถเปลี่ยนสถานะเป็นซ่อมบำรุงได้โดยตรง");
                }

                if (room.Status == "maintenance")
                {
                    await tx.RollbackAsync();
                    return Respond(false, $"ห้อง {room.RoomNumber} อยู่ในสถานะซ่อมบำรุงอยู่แล้ว");
                }

                var now = DateTime.Now;
                var oldStatus = room.Status;
                room.Status = "maintenance";

                _context.RoomStatusHistories.Add(new RoomStatusHistory
                {
                    RoomId = room.RoomId,
                    OldStatus = oldStatus,
                    NewStatus = "maintenance",
                    ChangedBy = uid,
                    ChangedAt = now,
                    Note = $"แจ้งซ่อมบำรุงโดยฝ่ายแม่บ้าน: {cleanNote}"
                });

                if (taskId.HasValue)
                {
                    var task = await _context.HousekeepingTasks.FirstOrDefaultAsync(t => t.TaskId == taskId.Value && t.RoomId == roomId);
                    if (task != null)
                    {
                        task.Note = AppendNote(task.Note, $"[แจ้งซ่อม: {cleanNote}]");
                    }
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Respond(true, $"แจ้งซ่อมบำรุงห้อง {room.RoomNumber} และระงับการขายชั่วคราวเรียบร้อยแล้ว",
                    new { roomId = room.RoomId, roomNumber = room.RoomNumber, status = "maintenance" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReportMaintenance failed (roomId={RoomId})", roomId);
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกแจ้งซ่อมบำรุง กรุณาลองใหม่อีกครั้ง");
            }
        }

        // ==========================================
        // HELPERS & DASHBOARD BUILDERS
        // ==========================================

        private async Task<HousekeepingViewModel> BuildDashboardViewModelAsync(uint uid, string fullName)
        {
            var todayStart = DateTime.Today;
            var tomorrowStart = todayStart.AddDays(1);

            var tasksQuery = _context.HousekeepingTasks.AsNoTracking();

            // นับจำนวนตามสถานะจริง
            var pendingCount = await tasksQuery.CountAsync(t => t.Status == StatusPending);
            var inProgressCount = await tasksQuery.CountAsync(t => t.Status == StatusInProgress);
            var completedTodayCount = await tasksQuery.CountAsync(t =>
                t.Status == StatusDone &&
                t.CompletedAt.HasValue &&
                t.CompletedAt.Value >= todayStart &&
                t.CompletedAt.Value < tomorrowStart);

            var totalTasksCount = await tasksQuery.CountAsync();
            var totalCompletedAllTime = await tasksQuery.CountAsync(t => t.Status == StatusDone);
            var totalAssignedTodayOrActive = pendingCount + inProgressCount + completedTodayCount;

            var unassignedPending = await tasksQuery.CountAsync(t => t.Status == StatusPending && t.AssignedTo == null);
            var myActive = await tasksQuery.CountAsync(t => t.AssignedTo == uid && t.Status != StatusDone);

            var roomsNeedingCleaning = await _context.Rooms
                .AsNoTracking()
                .CountAsync(r => r.IsActive == true && r.Status == "needs_cleaning");

            // จำนวนห้องที่แจ้งปัญหา (Maintenance) — เป็นหนึ่งใน 4 ตัวเลขหลักของ Dashboard ตามเอกสารสรุประบบ
            var maintenanceRoomsCount = await _context.Rooms
                .AsNoTracking()
                .CountAsync(r => r.IsActive == true && r.Status == "maintenance");

            // งานล่าสุดและงานที่กำลังดำเนินการสำหรับตาราง Cleaning Tasks Table
            var roomTasks = await ProjectTasks(
                    tasksQuery
                        .OrderBy(t => t.Status == StatusDone ? 1 : 0)
                        .ThenByDescending(t => t.CreatedAt)
                        .Take(25), uid)
                .ToListAsync();

            // ห้องพักที่ต้องดูแล (รอทำความสะอาด, กำลังทำ, ซ่อมบำรุง, หรือเพิ่งทำเสร็จวันนี้)
            var roomsToDisplay = await ProjectRooms(
                    _context.Rooms.AsNoTracking()
                        .Where(r => r.IsActive == true &&
                                   (r.Status == "needs_cleaning" ||
                                    r.Status == "maintenance" ||
                                    r.HousekeepingTasks.Any(t =>
                                        t.Status != StatusDone ||
                                        (t.CompletedAt.HasValue && t.CompletedAt.Value >= todayStart))))
                        .OrderBy(r => r.FloorNo)
                        .ThenBy(r => r.RoomNumber)
                        .Take(16), uid)
                .ToListAsync();

            var recentActivities = await BuildRecentActivitiesAsync(10);

            string summaryText;
            if (pendingCount == 0 && inProgressCount == 0)
            {
                summaryText = completedTodayCount > 0
                    ? $"ยอดเยี่ยมมาก! วันนี้ทำความสะอาดครบทั้ง {completedTodayCount} ห้องเรียบร้อยแล้ว"
                    : "ขณะนี้ไม่มีห้องค้างทำความสะอาด ระบบพร้อมรับงานใหม่";
            }
            else
            {
                summaryText = $"วันนี้มีห้องรอดำเนินการ {pendingCount} ห้อง, กำลังทำความสะอาด {inProgressCount} ห้อง และเสร็จแล้ว {completedTodayCount} ห้อง";
            }

            if (maintenanceRoomsCount > 0)
            {
                summaryText += $" (มีห้องแจ้งซ่อมบำรุง {maintenanceRoomsCount} ห้อง)";
            }

            return new HousekeepingViewModel
            {
                CurrentUserId = uid,
                CurrentUserName = fullName,
                CurrentDate = DateTime.Now,
                DailyStatusSummary = summaryText,

                // KPI Cards
                TotalAssignedRooms = totalAssignedTodayOrActive,
                PendingRooms = pendingCount,
                InProgressRooms = inProgressCount,
                CompletedToday = completedTodayCount,
                MaintenanceRooms = maintenanceRoomsCount,

                // Summary Properties
                TotalTasks = totalTasksCount,
                PendingTasks = pendingCount,
                InProgressTasks = inProgressCount,
                CompletedTasks = totalCompletedAllTime,
                UnassignedPendingTasks = unassignedPending,
                MyActiveTasks = myActive,
                RoomsNeedingCleaning = roomsNeedingCleaning,

                RoomTasks = roomTasks,
                RecentTasks = roomTasks.Take(10).ToList(),
                RoomsToClean = roomsToDisplay,
                RecentActivities = recentActivities
            };
        }

        /// <summary>
        /// ดึงประวัติกิจกรรมจริงจาก RoomStatusHistories และ HousekeepingTasks
        /// </summary>
        private async Task<List<HousekeepingViewModel.ActivityItem>> BuildRecentActivitiesAsync(int takeCount)
        {
            var list = new List<HousekeepingViewModel.ActivityItem>();

            // 1. ประวัติจากการเริ่มงานและปิดงานใน HousekeepingTasks
            var recentTaskEvents = await _context.HousekeepingTasks
                .AsNoTracking()
                .Where(t => t.StartedAt != null || t.CompletedAt != null)
                .OrderByDescending(t => t.CompletedAt ?? t.StartedAt ?? t.CreatedAt)
                .Take(takeCount)
                .Select(t => new
                {
                    t.TaskId,
                    RoomNumber = t.Room.RoomNumber,
                    t.Status,
                    t.Note,
                    ActorName = t.AssignedToNavigation != null ? t.AssignedToNavigation.FullName : "พนักงานแม่บ้าน",
                    t.StartedAt,
                    t.CompletedAt
                })
                .ToListAsync();

            foreach (var ev in recentTaskEvents)
            {
                if (ev.Status == StatusDone && ev.CompletedAt.HasValue)
                {
                    list.Add(new HousekeepingViewModel.ActivityItem
                    {
                        ActivityType = "complete",
                        Title = $"ทำความสะอาดห้อง {ev.RoomNumber} เสร็จแล้ว",
                        RoomNumber = ev.RoomNumber,
                        ActorName = ev.ActorName,
                        DetailNote = string.IsNullOrWhiteSpace(ev.Note) ? $"งานเลขที่ #{ev.TaskId}" : ev.Note,
                        Timestamp = ev.CompletedAt.Value
                    });
                }
                else if (ev.StartedAt.HasValue)
                {
                    list.Add(new HousekeepingViewModel.ActivityItem
                    {
                        ActivityType = "start",
                        Title = $"เริ่มทำความสะอาดห้อง {ev.RoomNumber}",
                        RoomNumber = ev.RoomNumber,
                        ActorName = ev.ActorName,
                        DetailNote = string.IsNullOrWhiteSpace(ev.Note) ? $"งานเลขที่ #{ev.TaskId}" : ev.Note,
                        Timestamp = ev.StartedAt.Value
                    });
                }
            }

            // 2. ประวัติการแจ้งซ่อม / ส่งงานทำความสะอาด จาก RoomStatusHistories
            // (เดิมดึง "available" มาด้วยแต่ไม่ได้ใช้ ทำให้แถวที่ไม่ใช้ไปแย่งโควตา Take ของแถวที่ใช้จริง จึงตัดออก)
            var statusHistories = await _context.RoomStatusHistories
                .AsNoTracking()
                .Where(h => h.NewStatus == "maintenance" || h.NewStatus == "needs_cleaning")
                .OrderByDescending(h => h.ChangedAt)
                .Take(takeCount)
                .Select(h => new
                {
                    RoomNumber = h.Room.RoomNumber,
                    h.OldStatus,
                    h.NewStatus,
                    h.Note,
                    h.ChangedAt,
                    ActorName = h.ChangedByNavigation != null ? h.ChangedByNavigation.FullName : "ระบบ"
                })
                .ToListAsync();

            foreach (var h in statusHistories)
            {
                if (h.NewStatus == "maintenance")
                {
                    list.Add(new HousekeepingViewModel.ActivityItem
                    {
                        ActivityType = "maintenance",
                        Title = $"แจ้งปัญหาอุปกรณ์ห้อง {h.RoomNumber}",
                        RoomNumber = h.RoomNumber,
                        ActorName = h.ActorName,
                        DetailNote = h.Note ?? "เปลี่ยนสถานะห้องเป็นซ่อมบำรุง",
                        Timestamp = h.ChangedAt
                    });
                }
                else
                {
                    list.Add(new HousekeepingViewModel.ActivityItem
                    {
                        ActivityType = "pending",
                        Title = $"ส่งงานทำความสะอาดห้อง {h.RoomNumber}",
                        RoomNumber = h.RoomNumber,
                        ActorName = h.ActorName,
                        DetailNote = h.Note ?? "ลูกค้า Check-out / รอทำความสะอาด",
                        Timestamp = h.ChangedAt
                    });
                }
            }

            return list
                .OrderByDescending(a => a.Timestamp)
                .Take(takeCount)
                .ToList();
        }

        private static IQueryable<HousekeepingViewModel.TaskItem> ProjectTasks(
            IQueryable<HousekeepingTask> source, uint userId)
        {
            return source.Select(t => new HousekeepingViewModel.TaskItem
            {
                TaskId = t.TaskId,
                RoomId = t.RoomId,
                RoomNumber = t.Room.RoomNumber,
                FloorNo = t.Room.FloorNo,
                RoomTypeName = t.Room.RoomType.TypeName,
                RoomStatus = t.Room.Status,
                BookingId = t.BookingId,
                BookingCode = t.Booking != null ? t.Booking.BookingCode : null,
                Note = t.Note,
                Status = t.Status,
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedToNavigation != null ? t.AssignedToNavigation.FullName : null,
                IsMine = t.AssignedTo == userId,
                CreatedAt = t.CreatedAt,
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt
            });
        }

        private static IQueryable<HousekeepingViewModel.RoomItem> ProjectRooms(
            IQueryable<Room> source, uint userId = 0)
        {
            return source.Select(r => new HousekeepingViewModel.RoomItem
            {
                RoomId = r.RoomId,
                RoomNumber = r.RoomNumber,
                FloorNo = r.FloorNo,
                RoomTypeName = r.RoomType.TypeName,
                Status = r.Status,
                HasActiveStay = r.StayRecords.Any(s => s.CheckedOutAt == null),
                OpenTaskId = r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => (uint?)t.TaskId)
                    .FirstOrDefault(),
                OpenTaskStatus = r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => t.Status)
                    .FirstOrDefault(),
                OpenTaskNote = r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => t.Note)
                    .FirstOrDefault(),
                OpenTaskCreatedAt = r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => (DateTime?)t.CreatedAt)
                    .FirstOrDefault(),
                OpenTaskAssignedTo = r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => t.AssignedTo)
                    .FirstOrDefault(),
                OpenTaskAssignedToName = r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => t.AssignedToNavigation != null ? t.AssignedToNavigation.FullName : null)
                    .FirstOrDefault(),
                IsOpenTaskMine = userId != 0 && r.HousekeepingTasks
                    .Where(t => t.Status != StatusDone)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => t.AssignedTo == userId)
                    .FirstOrDefault(),
                LastUpdatedAt = r.RoomStatusHistories
                    .OrderByDescending(h => h.ChangedAt)
                    .Select(h => (DateTime?)h.ChangedAt)
                    .FirstOrDefault(),
                LastUpdatedBy = r.RoomStatusHistories
                    .OrderByDescending(h => h.ChangedAt)
                    .Select(h => h.ChangedByNavigation != null ? h.ChangedByNavigation.FullName : null)
                    .FirstOrDefault(),
                // เดิมมี property นี้ใน ViewModel แต่ไม่เคยถูกเติมค่า
                LastStatusNote = r.RoomStatusHistories
                    .OrderByDescending(h => h.ChangedAt)
                    .Select(h => h.Note)
                    .FirstOrDefault()
            });
        }

        private static string? NormalizeStatus(string? status)
        {
            return status?.Trim().ToLowerInvariant() switch
            {
                "pending" => StatusPending,
                "in_progress" or "inprogress" or "in progress" => StatusInProgress,
                "done" or "completed" => StatusDone,
                _ => null
            };
        }

        private static IQueryable<HousekeepingTask> ApplyHistoryFilters(
            IQueryable<HousekeepingTask> query,
            string? search,
            byte? floor,
            uint? employeeId,
            string? status,
            DateOnly? singleDate,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            string? issueType)
        {
            // 1. ค้นหาจากเลขห้อง หรือหมายเลขงาน
            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim().TrimStart('#');
                var isId = uint.TryParse(keyword, out var searchId);

                query = query.Where(t =>
                    t.Room.RoomNumber.Contains(keyword) ||
                    (isId && t.TaskId == searchId));
            }

            // 2. กรองตามชั้น
            if (floor.HasValue)
            {
                query = query.Where(t => t.Room.FloorNo == floor.Value);
            }

            // 3. กรองตามพนักงานแม่บ้าน
            if (employeeId.HasValue)
            {
                query = query.Where(t => t.AssignedTo == employeeId.Value);
            }

            // 4. กรองตามสถานะ
            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = NormalizeStatus(status);

                if (normalizedStatus != null)
                {
                    query = query.Where(t => t.Status == normalizedStatus);
                }
                else if (status.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(t => t.Room.Status == "maintenance");
                }
            }

            // 5. กรองวันที่เดียว
            if (singleDate.HasValue)
            {
                var start = singleDate.Value.ToDateTime(TimeOnly.MinValue);
                var end = singleDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);

                query = query.Where(t =>
                    (t.CompletedAt ?? t.CreatedAt) >= start &&
                    (t.CompletedAt ?? t.CreatedAt) < end);
            }
            else
            {
                // 6. กรองวันที่เริ่มต้น
                if (dateFrom.HasValue)
                {
                    var from = dateFrom.Value.ToDateTime(TimeOnly.MinValue);
                    query = query.Where(t => (t.CompletedAt ?? t.CreatedAt) >= from);
                }

                // 7. กรองวันที่สิ้นสุด
                if (dateTo.HasValue)
                {
                    var to = dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                    query = query.Where(t => (t.CompletedAt ?? t.CreatedAt) < to);
                }
            }

            // 8. กรองประเภทปัญหา
            // เดิม Controller รับแค่ "maintenance"/"normal" แต่ JS + ViewModel ใช้ "issue_only"/"normal_only"
            // ทำให้ตัวกรองฝั่ง Server ไม่ทำงาน -> รองรับทั้งสองชุดค่า
            if (!string.IsNullOrWhiteSpace(issueType))
            {
                switch (issueType.Trim().ToLowerInvariant())
                {
                    case "issue_only":
                    case "maintenance":
                        query = query.Where(t =>
                            t.Room.Status == "maintenance" ||
                            (t.Note != null &&
                             (t.Note.Contains("แจ้งซ่อม") ||
                              t.Note.Contains("ชำรุด") ||
                              t.Note.Contains("เสีย"))));
                        break;

                    case "normal_only":
                    case "normal":
                        query = query.Where(t =>
                            t.Room.Status != "maintenance" &&
                            (t.Note == null ||
                             (!t.Note.Contains("แจ้งซ่อม") &&
                              !t.Note.Contains("ชำรุด") &&
                              !t.Note.Contains("เสีย"))));
                        break;
                }
            }

            return query;
        }

        /// <summary>
        /// ต่อท้าย Note โดยไม่เกิน 255 ตัวอักษร (ตามขนาดคอลัมน์) และรักษาข้อความใหม่ไว้เสมอ
        /// (ตัดส่วนเก่าแทน เพื่อไม่ให้เครื่องหมาย [ตรวจห้องผ่าน] / [แจ้งซ่อม] ถูกตัดหาย)
        /// </summary>
        private static string AppendNote(string? existing, string addition)
        {
            const int max = 255;
            if (addition.Length >= max) return addition.Substring(0, max);
            if (string.IsNullOrWhiteSpace(existing)) return addition;

            var room = max - addition.Length - 3;
            var head = room <= 0 ? string.Empty
                : existing.Length > room ? existing.Substring(0, room) : existing;
            return string.IsNullOrEmpty(head) ? addition : $"{head} | {addition}";
        }

        /// <summary>Escape ค่าสำหรับ CSV และกัน CSV/Formula Injection (ค่าที่ขึ้นต้นด้วย = + - @)</summary>
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            var v = value.Replace("\r", " ").Replace("\n", " ");
            if (v.Length > 0 && (v[0] == '=' || v[0] == '+' || v[0] == '-' || v[0] == '@'))
            {
                v = "'" + v;
            }
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        private uint? GetCurrentUserId()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return uint.TryParse(value, out var id) ? id : null;
        }

        private async Task<(uint UserId, string FullName)?> GetValidHousekeeperAsync()
        {
            var id = GetCurrentUserId();
            if (id == null) return null;
            var uid = id.Value;

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == uid &&
                            u.IsActive == true &&
                            u.UserRoles.Any(ur => ur.Role.RoleCode == HousekeepingRole))
                .Select(u => new { u.UserId, u.FullName })
                .FirstOrDefaultAsync();

            if (user == null) return null;
            return (user.UserId, user.FullName);
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                   || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }

        private IActionResult Denied()
        {
            if (IsAjaxRequest())
            {
                return new JsonResult(new { success = false, message = "ไม่มีสิทธิ์ดำเนินการ หรือเซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่" })
                {
                    StatusCode = 403
                };
            }
            return RedirectToAction("Login", "Account");
        }

        private IActionResult Respond(bool success, string message, object? extra = null)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success, message, data = extra });
            }

            TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
            return RedirectToAction(nameof(Index));
        }

        private async Task<(string? Error, string? RoomNumber)> PreCheckAsync(
            uint taskId, uint userId, string expectedStatus, bool isAccept)
        {
            var t = await _context.HousekeepingTasks
                .AsNoTracking()
                .Where(x => x.TaskId == taskId)
                .Select(x => new { x.Status, x.AssignedTo, RoomNumber = x.Room.RoomNumber })
                .FirstOrDefaultAsync();

            if (t == null) return ("ไม่พบงานแม่บ้านที่ระบุ", null);

            if (t.Status == StatusDone) return ("งานนี้เสร็จสิ้นแล้ว", t.RoomNumber);

            if (t.Status != expectedStatus)
            {
                return ($"ไม่สามารถดำเนินการได้ สถานะงานปัจจุบันคือ {HousekeepingViewModel.ToStatusLabel(t.Status)}", t.RoomNumber);
            }

            if (isAccept)
            {
                if (t.AssignedTo == userId) return ("คุณรับงานนี้แล้ว", t.RoomNumber);
                if (t.AssignedTo != null) return ("งานนี้มีพนักงานท่านอื่นรับไปแล้ว", t.RoomNumber);
            }
            else
            {
                if (t.AssignedTo == null) return ("ต้องกดรับงานก่อนดำเนินการ", t.RoomNumber);
                if (t.AssignedTo != userId) return ("งานนี้เป็นของพนักงานท่านอื่น", t.RoomNumber);
            }

            return (null, t.RoomNumber);
        }
    }
}