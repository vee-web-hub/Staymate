/**
 * StayMate Reception Housekeeping Requests Tracker Script
 * File: wwwroot/js/reception-housekeeping.js
 */
document.addEventListener("DOMContentLoaded", function () {

    // 1. Refresh พร้อมแสดง Loading State
    const btnRefresh = document.getElementById("btnRefreshHkPage");
    const loadingState = document.getElementById("hkLoadingState");
    const tableContainer = document.getElementById("hkTableContainer");

    btnRefresh?.addEventListener("click", function () {
        this.classList.add("is-loading");
        if (loadingState) loadingState.classList.remove("d-none");
        if (tableContainer) tableContainer.style.opacity = "0.4";
        window.location.reload();
    });

    // 2. ระบบค้นหา กรองตามสถานะ/วันที่ และสลับ Tab (ทั้งหมด / กำลังติดตาม / ประวัติที่เสร็จสิ้น)
    const inputRoom = document.getElementById("hkSearchRoom");
    const inputKeyword = document.getElementById("hkSearchBookingOrNote");
    const selectStatus = document.getElementById("hkFilterStatus");
    const inputDate = document.getElementById("hkFilterDate");
    const btnApply = document.getElementById("btnApplyHkSearch");
    const btnReset = document.getElementById("btnResetHkSearch");
    const tabBtns = document.querySelectorAll("[data-hk-tab]");
    const rows = document.querySelectorAll(".reception-hk-row");

    let currentTab = "all";

    function filterHousekeepingRows() {
        const roomKw = (inputRoom?.value || "").trim().toLowerCase();
        const searchKw = (inputKeyword?.value || "").trim().toLowerCase();
        const statusVal = (selectStatus?.value || "all").toLowerCase();
        const dateVal = inputDate?.value || "";

        rows.forEach((row) => {
            const rRoom = row.getAttribute("data-room") || "";
            const rSearch = row.getAttribute("data-search") || "";
            const rStatus = row.getAttribute("data-status") || "";
            const rDate = row.getAttribute("data-date-iso") || "";
            const isCompleted = row.getAttribute("data-is-completed") === "true";

            const matchTab =
                currentTab === "all" ||
                (currentTab === "active" && !isCompleted) ||
                (currentTab === "history" && isCompleted);

            const matchRoom = !roomKw || rRoom.includes(roomKw);
            const matchSearch = !searchKw || rSearch.includes(searchKw);
            const matchStatus = statusVal === "all" || rStatus === statusVal;
            const matchDate = !dateVal || rDate === dateVal;

            row.style.display = (matchTab && matchRoom && matchSearch && matchStatus && matchDate) ? "" : "none";
        });
    }

    tabBtns.forEach((btn) => {
        btn.addEventListener("click", function () {
            tabBtns.forEach((b) => b.classList.remove("active"));
            this.classList.add("active");
            currentTab = this.getAttribute("data-hk-tab") || "all";
            filterHousekeepingRows();
        });
    });

    [inputRoom, inputKeyword, selectStatus, inputDate].forEach((el) => {
        el?.addEventListener("input", filterHousekeepingRows);
        el?.addEventListener("change", filterHousekeepingRows);
    });

    btnApply?.addEventListener("click", filterHousekeepingRows);

    btnReset?.addEventListener("click", function () {
        if (inputRoom) inputRoom.value = "";
        if (inputKeyword) inputKeyword.value = "";
        if (selectStatus) selectStatus.value = "all";
        if (inputDate) inputDate.value = "";
        currentTab = "all";
        tabBtns.forEach((b) => b.classList.toggle("active", b.getAttribute("data-hk-tab") === "all"));
        filterHousekeepingRows();
    });

    // 3. เปิด Modal ดูรายละเอียดงานและประวัติการดำเนินงาน (Read-only Inspector)
    const detailsModal = document.getElementById("hkTaskDetailsModal");
    const btnCloseDetails = document.getElementById("btnCloseHkDetailsModal");
    const btnCloseDetailsFooter = document.getElementById("btnCloseHkDetailsFooter");

    document.querySelectorAll(".js-open-hk-details").forEach((btn) => {
        btn.addEventListener("click", function () {
            const taskId = this.getAttribute("data-task-id") || "-";
            const roomNo = this.getAttribute("data-room-number") || "-";
            const bookingCode = this.getAttribute("data-booking-code") || "-";
            const note = this.getAttribute("data-note") || "-";
            const staff = this.getAttribute("data-staff") || "-";
            const created = this.getAttribute("data-created") || "-";
            const started = this.getAttribute("data-started") || "-";
            const completed = this.getAttribute("data-completed") || "-";
            const duration = this.getAttribute("data-duration") || "-";
            const statusKey = this.getAttribute("data-status-key") || "pending";
            const statusLabel = this.getAttribute("data-status-label") || "Pending";

            document.getElementById("modalHkTaskId").textContent = taskId;
            document.getElementById("detHkTaskId").textContent = taskId;
            document.getElementById("detHkRoomNumber").textContent = roomNo;
            document.getElementById("detHkBookingCode").textContent = bookingCode;
            document.getElementById("detHkStaffName").textContent = staff;
            document.getElementById("detHkDuration").textContent = duration;
            document.getElementById("detHkNote").textContent = note;

            const badgeContainer = document.getElementById("detHkStatusBadge");
            if (badgeContainer) {
                const badgeCss = statusKey === "completed"
                    ? "reception-hkbadge-completed"
                    : (statusKey === "in_progress" ? "reception-hkbadge-inprogress" : (statusKey === "cancelled" ? "reception-hkbadge-cancelled" : "reception-hkbadge-pending"));
                badgeContainer.innerHTML = `<span class="reception-status-badge ${badgeCss}">${statusLabel}</span>`;
            }

            // อัปเดต Timeline 3 ขั้นตอน
            document.getElementById("detHkCreatedTime").textContent = created;
            document.getElementById("detHkStartedTime").textContent = started !== "-" ? started : "รอแม่บ้านกดรับงาน";
            document.getElementById("detHkCompletedTime").textContent = completed !== "-" ? completed : "รอดำเนินการเสร็จสิ้น";

            const stepStarted = document.getElementById("hkStepStarted");
            const stepCompleted = document.getElementById("hkStepCompleted");

            stepStarted?.classList.remove("is-completed", "is-active");
            stepCompleted?.classList.remove("is-completed", "is-active");

            if (statusKey === "in_progress") {
                stepStarted?.classList.add("is-active");
            } else if (statusKey === "completed") {
                stepStarted?.classList.add("is-completed");
                stepCompleted?.classList.add("is-completed");
            }

            detailsModal?.classList.add("is-open");
        });
    });

    function closeDetailsModal() {
        detailsModal?.classList.remove("is-open");
    }

    btnCloseDetails?.addEventListener("click", closeDetailsModal);
    btnCloseDetailsFooter?.addEventListener("click", closeDetailsModal);

    // 4. เปิด/ปิด Modal แจ้งงานแม่บ้านใหม่ (POST CreateHousekeepingRequest)
    const createModal = document.getElementById("createHkTaskModal");
    const btnOpenCreate = document.getElementById("btnOpenCreateTaskModal");
    const btnEmptyOpenCreate = document.getElementById("btnEmptyOpenCreateTask");
    const btnCloseCreate = document.getElementById("btnCloseCreateHkModal");
    const btnCancelCreate = document.getElementById("btnCancelCreateHkModal");

    function openCreateModal() {
        createModal?.classList.add("is-open");
    }

    function closeCreateModal() {
        createModal?.classList.remove("is-open");
    }

    btnOpenCreate?.addEventListener("click", openCreateModal);
    btnEmptyOpenCreate?.addEventListener("click", openCreateModal);
    btnCloseCreate?.addEventListener("click", closeCreateModal);
    btnCancelCreate?.addEventListener("click", closeCreateModal);
});
