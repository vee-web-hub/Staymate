using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models.ViewModels
{
    /// <summary>
    /// ViewModel หลักของระบบ Housekeeping (รองรับ Dashboard, CleaningTasks, RoomStatus, TaskDetails, History, Profile)
    /// อ้างอิงจากคอลัมน์จริงใน housekeeping_tasks / rooms / users / room_status_history
    /// </summary>
    public class HousekeepingViewModel
    {
        // ค่าสถานะตาม enum ในฐานข้อมูล (housekeeping_tasks.status)
        public const string StatusPending = "pending";
        public const string StatusInProgress = "in_progress";
        public const string StatusDone = "done";

        /// <summary>
        /// เครื่องหมายที่ Controller เติมลงใน Note ของงาน เมื่อผ่าน Checklist ตรวจห้องครบ 4 ข้อจริง
        /// ใช้เป็นหลักฐานเดียวที่บอกว่า "ผ่าน Checklist" (ไม่เดาจากสถานะงานอีกต่อไป)
        /// </summary>
        public const string ChecklistPassedMarker = "[ตรวจห้องผ่าน]";

        public static string ToStatusLabel(string? status) => status switch
        {
            StatusPending => "Pending",
            StatusInProgress => "In Progress",
            StatusDone => "Completed",
            _ => status ?? "-"
        };

        public static string ToStatusThaiLabel(string? status) => status switch
        {
            StatusPending => "รอทำความสะอาด",
            StatusInProgress => "กำลังทำความสะอาด",
            StatusDone => "ทำความสะอาดเสร็จแล้ว",
            _ => status ?? "-"
        };

        public static string ToRoomStatusLabel(string? status) => status switch
        {
            "available" => "Available (พร้อมให้บริการ)",
            "occupied" => "Occupied (มีลูกค้าเข้าพัก)",
            "needs_cleaning" => "Pending Cleaning (รอทำความสะอาด)",
            "maintenance" => "Maintenance (ซ่อมบำรุง)",
            _ => status ?? "-"
        };

        public static string CalculatePriority(string? status, string? note, DateTime createdAt)
        {
            if (status == StatusDone) return "Normal";

            if (!string.IsNullOrWhiteSpace(note))
            {
                var n = note.ToLowerInvariant();
                if (n.Contains("ด่วน") || n.Contains("urgent") || n.Contains("vip") || n.Contains("ชำรุด") || n.Contains("เสีย"))
                {
                    return "High";
                }
            }

            var waitMinutes = (DateTime.Now - createdAt).TotalMinutes;
            if (waitMinutes >= 120) return "High";
            if (waitMinutes >= 45 || status == StatusInProgress) return "Medium";
            return "Normal";
        }

        public static string ToPriorityThaiLabel(string? priority) => priority switch
        {
            "High" => "ด่วนมาก",
            "Medium" => "ปานกลาง",
            _ => "ปกติ"
        };

        /// <summary>
        /// กติกาตรวจ "มีปัญหา" ต้องตรงกับเงื่อนไขใน Controller (BuildDashboard / History / ApplyHistoryFilters)
        /// คำที่ใช้: แจ้งซ่อม, ชำรุด, เสีย  (ตัดคำว่า "ปัญหา" ออก เพราะ Controller ไม่ได้นับด้วย ทำให้ตัวเลขไม่ตรงกัน)
        /// </summary>
        private static bool NoteIndicatesIssue(string? note) =>
            !string.IsNullOrWhiteSpace(note) &&
            (note.Contains("แจ้งซ่อม") || note.Contains("ชำรุด") || note.Contains("เสีย"));

        private static bool NoteHasChecklistMarker(string? note) =>
            !string.IsNullOrWhiteSpace(note) && note.Contains(ChecklistPassedMarker);

        // =====================================
        // USER & WELCOME SECTION
        // =====================================
        public uint CurrentUserId { get; set; }
        public string CurrentUserName { get; set; } = string.Empty;
        public DateTime CurrentDate { get; set; } = DateTime.Now;
        public string DailyStatusSummary { get; set; } = string.Empty;

        // =====================================
        // DASHBOARD KPI & PROGRESS
        // =====================================
        public int TotalAssignedRooms { get; set; }
        public int PendingRooms { get; set; }
        public int InProgressRooms { get; set; }
        public int CompletedToday { get; set; }

        public int CleaningProgress =>
            TotalAssignedRooms > 0
                ? (int)Math.Round((double)CompletedToday / TotalAssignedRooms * 100.0)
                : 0;

        public int RemainingTasksCount => Math.Max(0, TotalAssignedRooms - CompletedToday);

        public List<TaskItem> RoomTasks { get; set; } = new();
        public List<ActivityItem> RecentActivities { get; set; } = new();

        // =====================================
        // DASHBOARD SUMMARY
        // (ใช้ MaintenanceRooms ในส่วน Room Status ด้านล่างเป็น "จำนวนห้องที่แจ้งปัญหา" บน Dashboard ด้วย)
        // =====================================
        public int TotalTasks { get; set; }
        public int PendingTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int UnassignedPendingTasks { get; set; }
        public int MyActiveTasks { get; set; }
        public int RoomsNeedingCleaning { get; set; }
        public List<TaskItem> RecentTasks { get; set; } = new();
        public List<RoomItem> RoomsToClean { get; set; } = new();

        // =====================================
        // CLEANING TASKS
        // =====================================
        public string? SearchKeyword { get; set; }
        public string? StatusFilter { get; set; }
        public byte? FloorFilter { get; set; }
        public string? PriorityFilter { get; set; }
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
        public string ActiveTab { get; set; } = "open";
        public List<byte> AvailableFloors { get; set; } = new();
        public List<TaskItem> AllCleaningTasks { get; set; } = new();
        public List<TaskItem> OpenTasks { get; set; } = new();
        public List<TaskItem> CompletedTaskList { get; set; } = new();

        // =====================================
        // TASK DETAILS
        // =====================================
        public TaskDetailItem? Detail { get; set; }

        // =====================================
        // ROOM STATUS MANAGEMENT (Summary Cards + Filters + History)
        // =====================================
        public int TotalRooms { get; set; }
        public int AvailableRooms { get; set; }
        public int OccupiedRooms { get; set; }
        public int NeedsCleaningRooms { get; set; }
        public int PendingCleaningRooms { get; set; }
        public int InProgressCleaningRooms { get; set; }
        public int MaintenanceRooms { get; set; }
        public List<string> AvailableRoomTypes { get; set; } = new();
        public List<RoomItem> Rooms { get; set; } = new();
        public List<RoomHistoryItem> RoomStatusHistories { get; set; } = new();

        // =====================================
        // TASK HISTORY (หน้า Cleaning History Management)
        // =====================================
        public List<TaskItem> HistoryTasks { get; set; } = new();

        // Summary Cards สำหรับหน้า Cleaning History (คำนวณจาก Database จริง)
        public int HistoryTotalCompleted { get; set; }
        public int HistoryCompletedToday { get; set; }
        public double AverageCleaningMinutes { get; set; }
        public int ReportedIssuesCount { get; set; }

        // Filters เพิ่มเติมสำหรับหน้า History
        public uint? EmployeeFilter { get; set; }
        public string? IssueTypeFilter { get; set; } // all | issue_only | normal_only
        public DateOnly? SingleDateFilter { get; set; }
        public List<HousekeeperOption> HousekeeperList { get; set; } = new();

        // Activity Timeline & Cleaning Performance
        public List<ActivityItem> HistoryTimeline { get; set; } = new();
        public int RoomsCleanedPerDayAvg { get; set; }
        public int OnTimeCompletedCount { get; set; } // งานที่เสร็จภายในเกณฑ์มาตรฐาน (<= 45 นาที)
        public List<DailyPerformanceItem> DailyPerformanceStats { get; set; } = new();

        // =====================================
        // EMPLOYEE
        // =====================================
        public EmployeeInfo? Employee { get; set; }

        // =====================================
        // NESTED CLASSES
        // =====================================

        public class HousekeeperOption
        {
            public uint UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
        }

        public class DailyPerformanceItem
        {
            public string DateLabel { get; set; } = string.Empty;
            public int CompletedCount { get; set; }
            public int IssueCount { get; set; }
            public double AvgMinutes { get; set; }
        }

        public class TaskTimelineEvent
        {
            public string EventType { get; set; } = "info"; // assigned | started | maintenance | completed
            public string Title { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? ActorName { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class TaskItem
        {
            public uint TaskId { get; set; }
            public uint RoomId { get; set; }
            public string RoomNumber { get; set; } = string.Empty;
            public byte FloorNo { get; set; }
            public string? RoomTypeName { get; set; }
            public string? RoomStatus { get; set; }
            public uint? BookingId { get; set; }
            public string? BookingCode { get; set; }
            public string? Note { get; set; }
            public string Status { get; set; } = StatusPending;
            public uint? AssignedTo { get; set; }
            public string? AssignedToName { get; set; }
            public bool IsMine { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? CompletedAt { get; set; }

            public string StatusLabel => ToStatusLabel(Status);
            public string StatusThaiLabel => ToStatusThaiLabel(Status);
            public string RoomStatusLabel => ToRoomStatusLabel(RoomStatus);
            public string Priority => CalculatePriority(Status, Note, CreatedAt);
            public string PriorityLabel => ToPriorityThaiLabel(Priority);

            public string EffectiveVisualStatus =>
                RoomStatus == "maintenance" ? "maintenance" : Status;

            public string EffectiveStatusLabel => EffectiveVisualStatus switch
            {
                "maintenance" => "Maintenance (ซ่อมบำรุง)",
                StatusPending => "Pending (รอทำความสะอาด)",
                StatusInProgress => "In Progress (กำลังทำความสะอาด)",
                StatusDone => "Completed (ทำความสะอาดเสร็จแล้ว)",
                _ => StatusLabel
            };

            public int SortStatusOrder => EffectiveVisualStatus switch
            {
                "maintenance" => 0,
                StatusInProgress => 1,
                StatusPending => 2,
                StatusDone => 3,
                _ => 4
            };

            public int SortPriorityOrder => Priority switch
            {
                "High" => 0,
                "Medium" => 1,
                _ => 2
            };

            public bool CanAccept => Status == StatusPending && AssignedTo == null;
            // รองรับ Auto-Assign: เริ่มงานได้ทั้งงานของตัวเองและงานที่ยังไม่มีผู้รับ
            public bool CanStart => Status == StatusPending && (IsMine || AssignedTo == null);
            public bool CanComplete => Status == StatusInProgress && IsMine;
            public bool CanReportMaintenance => RoomStatus != "occupied" && RoomStatus != "maintenance";

            // --- สำหรับหน้า Cleaning History ---
            public int? DurationMinutes =>
                StartedAt.HasValue && CompletedAt.HasValue && CompletedAt.Value >= StartedAt.Value
                    ? (int)Math.Round((CompletedAt.Value - StartedAt.Value).TotalMinutes)
                    : null;

            public string DurationText =>
                DurationMinutes.HasValue ? $"{DurationMinutes.Value} นาที" : "-";

            public bool HasIssue => RoomStatus == "maintenance" || NoteIndicatesIssue(Note);

            /// <summary>ผ่าน Checklist จริง = งานเสร็จ และมีเครื่องหมาย [ตรวจห้องผ่าน] ที่ระบบบันทึกไว้</summary>
            public bool IsCompletedPassedChecklist =>
                Status == StatusDone && NoteHasChecklistMarker(Note);
        }

        public class TaskDetailItem
        {
            public uint TaskId { get; set; }
            public uint RoomId { get; set; }
            public string RoomNumber { get; set; } = string.Empty;
            public byte FloorNo { get; set; }
            public string? RoomTypeName { get; set; }
            public string? RoomStatus { get; set; }
            public uint? BookingId { get; set; }
            public string? BookingCode { get; set; }
            public string? Note { get; set; }
            public string Status { get; set; } = StatusPending;
            public uint? AssignedTo { get; set; }
            public string? AssignedToName { get; set; }
            public bool IsMine { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? CompletedAt { get; set; }

            public string StatusLabel => ToStatusLabel(Status);
            public string StatusThaiLabel => ToStatusThaiLabel(Status);
            public string RoomStatusLabel => ToRoomStatusLabel(RoomStatus);
            public string Priority => CalculatePriority(Status, Note, CreatedAt);
            public string PriorityLabel => ToPriorityThaiLabel(Priority);

            public bool CanAccept => Status == StatusPending && AssignedTo == null;
            // รองรับ Auto-Assign: เริ่มงานได้ทั้งงานของตัวเองและงานที่ยังไม่มีผู้รับ
            public bool CanStart => Status == StatusPending && (IsMine || AssignedTo == null);
            public bool CanComplete => Status == StatusInProgress && IsMine;
            public bool CanReportMaintenance => RoomStatus != "occupied" && RoomStatus != "maintenance";

            public int? DurationMinutes =>
                StartedAt.HasValue && CompletedAt.HasValue && CompletedAt.Value >= StartedAt.Value
                    ? (int)Math.Round((CompletedAt.Value - StartedAt.Value).TotalMinutes)
                    : null;

            // --- สำหรับหน้า Cleaning History / Task Details ---
            public bool HasIssue => RoomStatus == "maintenance" || NoteIndicatesIssue(Note);

            public bool PassedChecklist =>
                Status == StatusDone && NoteHasChecklistMarker(Note);

            public List<TaskTimelineEvent> TimelineEvents { get; set; } = new();
        }

        public class RoomItem
        {
            public uint RoomId { get; set; }
            public string RoomNumber { get; set; } = string.Empty;
            public byte FloorNo { get; set; }
            public string? RoomTypeName { get; set; }
            public string Status { get; set; } = string.Empty;
            public bool HasActiveStay { get; set; }
            public uint? OpenTaskId { get; set; }
            public string? OpenTaskStatus { get; set; }
            public string? OpenTaskNote { get; set; }
            public DateTime? OpenTaskCreatedAt { get; set; }
            public uint? OpenTaskAssignedTo { get; set; }
            public string? OpenTaskAssignedToName { get; set; }
            public bool IsOpenTaskMine { get; set; }
            public string? LastUpdatedBy { get; set; }
            public DateTime? LastUpdatedAt { get; set; }
            public string? LastStatusNote { get; set; }

            public string StatusLabel => ToRoomStatusLabel(Status);
            public bool IsReadyForSale => Status == "available";

            /// <summary>
            /// สถานะทั้ง 5 สำหรับหน้า Room Status Management:
            /// available (Green), occupied (Blue), pending_cleaning (Orange), in_progress (Purple), maintenance (Red)
            /// </summary>
            public string ManagementState
            {
                get
                {
                    if (Status == "maintenance") return "maintenance";
                    if (Status == "occupied" || HasActiveStay) return "occupied";
                    if (Status == "needs_cleaning")
                    {
                        return OpenTaskStatus == StatusInProgress ? "in_progress" : "pending_cleaning";
                    }
                    if (OpenTaskStatus == StatusInProgress) return "in_progress";
                    if (OpenTaskStatus == StatusPending) return "pending_cleaning";
                    return "available";
                }
            }

            public string ManagementStateLabel => ManagementState switch
            {
                "available" => "Available (พร้อมให้บริการ)",
                "occupied" => "Occupied (มีผู้เข้าพัก)",
                "pending_cleaning" => "Pending Cleaning (รอทำความสะอาด)",
                "in_progress" => "In Progress (กำลังทำความสะอาด)",
                "maintenance" => "Maintenance (ซ่อมบำรุง)",
                _ => StatusLabel
            };

            public string CleaningTaskBadgeText => OpenTaskStatus switch
            {
                StatusPending => "งานแม่บ้าน: รอเริ่มทำความสะอาด (Pending)",
                StatusInProgress => "งานแม่บ้าน: กำลังทำความสะอาด (In Progress)",
                _ => Status == "needs_cleaning" ? "รอสร้าง/เปิดงานทำความสะอาด" : "ไม่มีงานทำความสะอาดค้าง"
            };

            public int ManagementSortOrder => ManagementState switch
            {
                "in_progress" => 1,
                "pending_cleaning" => 2,
                "maintenance" => 3,
                "occupied" => 4,
                "available" => 5,
                _ => 6
            };

            // สำหรับหน้า Dashboard
            public string CardVisualState
            {
                get
                {
                    if (Status == "maintenance") return "maintenance";
                    if (OpenTaskStatus == StatusInProgress) return "in_progress";
                    if (OpenTaskStatus == StatusPending || Status == "needs_cleaning") return "pending";
                    return "completed";
                }
            }

            public string CardVisualLabel => CardVisualState switch
            {
                "maintenance" => "ซ่อมบำรุง (Maintenance)",
                "in_progress" => "กำลังทำความสะอาด (In Progress)",
                "pending" => "รอทำความสะอาด (Pending)",
                _ => "ทำความสะอาดเสร็จ / พร้อมขาย (Completed)"
            };

            public string Priority => OpenTaskCreatedAt.HasValue
                ? CalculatePriority(OpenTaskStatus, OpenTaskNote, OpenTaskCreatedAt.Value)
                : (Status == "maintenance" ? "High" : "Normal");

            public string PriorityLabel => ToPriorityThaiLabel(Priority);
        }

        public class RoomHistoryItem
        {
            public ulong HistoryId { get; set; }
            public uint RoomId { get; set; }
            public string RoomNumber { get; set; } = string.Empty;
            public byte FloorNo { get; set; }
            public string? OldStatus { get; set; } // null ได้ (บันทึกประวัติครั้งแรกของห้องไม่มีสถานะเดิม)
            public string NewStatus { get; set; } = string.Empty;
            public string OldStatusLabel => ToRoomStatusLabel(OldStatus);
            public string NewStatusLabel => ToRoomStatusLabel(NewStatus);
            public string ChangedByName { get; set; } = string.Empty;
            public DateTime ChangedAt { get; set; }
            public string? Note { get; set; }
        }

        public class ActivityItem
        {
            public string ActivityType { get; set; } = "info";
            public string Title { get; set; } = string.Empty;
            public string RoomNumber { get; set; } = string.Empty;
            public string? ActorName { get; set; }
            public string? DetailNote { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class EmployeeInfo
        {
            public uint UserId { get; set; }
            public string Username { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedAt { get; set; }
            public string RoleName { get; set; } = string.Empty;
            public int CompletedTaskCount { get; set; }
            public int ActiveTaskCount { get; set; }
        }
    }
}