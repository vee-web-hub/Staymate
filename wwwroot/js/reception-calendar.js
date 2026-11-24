/**
 * StayMate Room Calendar & Timeline Controller Script
 * File: wwwroot/js/reception-calendar.js
 */
document.addEventListener("DOMContentLoaded", function () {

    // 1. Date Picker Navigation (เปลี่ยนวันที่แล้วโหลดตารางตามช่วงวันที่ใหม่)
    const datePicker = document.getElementById("calendarDatePicker");
    const dateForm = document.getElementById("calendarDateForm");
    if (datePicker && dateForm) {
        datePicker.addEventListener("change", function () {
            dateForm.submit();
        });
    }

    // 2. Refresh Button
    const btnRefresh = document.getElementById("btnCalendarRefresh");
    if (btnRefresh) {
        btnRefresh.addEventListener("click", function () {
            btnRefresh.classList.add("is-loading");
            window.location.reload();
        });
    }

    // 3. Floor Pagination & Instant Search/Filter
    const searchInput = document.getElementById("calendarSearchInput");
    const typeSelect = document.getElementById("filterRoomType");
    const statusSelect = document.getElementById("filterRoomStatus");
    const resetBtn = document.getElementById("btnResetCalendarFilter");

    const floorRows = Array.from(document.querySelectorAll(".reception-timeline-floor-row"));
    const roomRows = Array.from(document.querySelectorAll(".reception-timeline-room-row"));
    const floorSummaryEl = document.getElementById("calendarFloorSummary");
    const floorButtonsEl = document.getElementById("calendarFloorButtons");

    const distinctFloors = [...new Set(floorRows.map((r) => r.getAttribute("data-floor") || ""))]
        .filter((f) => f !== "");
    let currentFloorIndex = 0;

    function renderCalendarView() {
        const keyword = (searchInput?.value || "").trim().toLowerCase();
        const selectedType = (typeSelect?.value || "all").toLowerCase();
        const selectedStatus = (statusSelect?.value || "all").toLowerCase();
        const isFiltering = Boolean(keyword) || selectedType !== "all" || selectedStatus !== "all";

        const activeFloor = distinctFloors[currentFloorIndex] || "";

        // กรองแถวห้องพักตามเงื่อนไข Search/Filter และชั้นที่เลือก
        roomRows.forEach((row) => {
            const rowFloor = row.getAttribute("data-floor") || "";
            const rowSearchText = row.getAttribute("data-search-text") || "";
            const rowType = row.getAttribute("data-room-type") || "";
            const rowStatus = row.getAttribute("data-room-status") || "";

            const matchKeyword = !keyword || rowSearchText.includes(keyword);
            const matchType = selectedType === "all" || rowType.includes(selectedType);
            const matchStatus = selectedStatus === "all" || rowStatus === selectedStatus;
            const matchFloor = isFiltering || rowFloor === activeFloor;

            row.style.display = (matchKeyword && matchType && matchStatus && matchFloor) ? "" : "none";
        });

        // แสดง/ซ่อนหัวข้อชั้น (Floor Banner) เฉพาะชั้นที่มีห้องแสดงอยู่
        floorRows.forEach((fRow) => {
            const fVal = fRow.getAttribute("data-floor") || "";
            const hasVisibleRooms = roomRows.some(
                (r) => r.getAttribute("data-floor") === fVal && r.style.display !== "none"
            );
            fRow.style.display = hasVisibleRooms ? "" : "none";
        });

        // อัปเดตแถบ Pagination ด้านล่าง
        if (floorSummaryEl && floorButtonsEl) {
            if (isFiltering) {
                const totalVisible = roomRows.filter((r) => r.style.display !== "none").length;
                floorSummaryEl.textContent = `พบห้องพักที่ตรงเงื่อนไข ${totalVisible} ห้อง (แสดงรวมทุกชั้น)`;
                floorButtonsEl.innerHTML = "";
            } else if (distinctFloors.length > 0) {
                const roomsOnFloor = roomRows.filter((r) => r.getAttribute("data-floor") === activeFloor).length;
                floorSummaryEl.textContent = `กำลังแสดง ชั้น ${activeFloor} (${roomsOnFloor} ห้อง) · ทั้งหมด ${distinctFloors.length} ชั้น`;

                floorButtonsEl.innerHTML = "";

                // ปุ่มย้อนกลับชั้นก่อนหน้า
                const prevBtn = document.createElement("button");
                prevBtn.type = "button";
                prevBtn.className = "reception-page-btn";
                prevBtn.innerHTML = `<i class="fa-solid fa-chevron-left"></i>`;
                prevBtn.disabled = currentFloorIndex === 0;
                prevBtn.title = "ชั้นก่อนหน้า";
                prevBtn.addEventListener("click", () => {
                    if (currentFloorIndex > 0) {
                        currentFloorIndex--;
                        renderCalendarView();
                    }
                });
                floorButtonsEl.appendChild(prevBtn);

                // ปุ่มเลือกแต่ละชั้น (ชั้น 3, ชั้น 4, ชั้น 5, ...)
                distinctFloors.forEach((floorNo, idx) => {
                    const btn = document.createElement("button");
                    btn.type = "button";
                    btn.className = `reception-page-btn ${idx === currentFloorIndex ? "active" : ""}`;
                    btn.style.width = "auto";
                    btn.style.padding = "0 10px";
                    btn.textContent = `ชั้น ${floorNo}`;
                    btn.addEventListener("click", () => {
                        currentFloorIndex = idx;
                        renderCalendarView();
                    });
                    floorButtonsEl.appendChild(btn);
                });

                // ปุ่มชั้นถัดไป
                const nextBtn = document.createElement("button");
                nextBtn.type = "button";
                nextBtn.className = "reception-page-btn";
                nextBtn.innerHTML = `<i class="fa-solid fa-chevron-right"></i>`;
                nextBtn.disabled = currentFloorIndex >= distinctFloors.length - 1;
                nextBtn.title = "ชั้นถัดไป";
                nextBtn.addEventListener("click", () => {
                    if (currentFloorIndex < distinctFloors.length - 1) {
                        currentFloorIndex++;
                        renderCalendarView();
                    }
                });
                floorButtonsEl.appendChild(nextBtn);
            }
        }
    }

    searchInput?.addEventListener("input", renderCalendarView);
    typeSelect?.addEventListener("change", renderCalendarView);
    statusSelect?.addEventListener("change", renderCalendarView);

    resetBtn?.addEventListener("click", function () {
        if (searchInput) searchInput.value = "";
        if (typeSelect) typeSelect.value = "all";
        if (statusSelect) statusSelect.value = "all";
        currentFloorIndex = 0;
        renderCalendarView();
    });

    if (floorRows.length > 0) {
        renderCalendarView();
    }

    // 4. Room & Booking Information Side Panel (Modal Drawer)
    const drawer = document.getElementById("roomInfoDrawer");
    const backdrop = document.getElementById("roomInfoBackdrop");
    const btnCloseDrawer = document.getElementById("btnCloseDrawer");
    const btnCloseFooter = document.getElementById("btnDrawerCloseFooter");

    const elTitle = document.getElementById("drawerRoomTitle");
    const elRoomNumber = document.getElementById("infoRoomNumber");
    const elRoomFloor = document.getElementById("infoRoomFloor");
    const elRoomType = document.getElementById("infoRoomType");
    const elRoomStatus = document.getElementById("infoRoomStatus");
    const elBookingCode = document.getElementById("infoBookingCode");
    const elGuestName = document.getElementById("infoGuestName");
    const elCheckIn = document.getElementById("infoCheckInDate");
    const elCheckOut = document.getElementById("infoCheckOutDate");
    const elPaymentStatus = document.getElementById("infoPaymentStatus");

    const btnViewDetails = document.getElementById("btnDrawerViewDetails");
    const btnCheckIn = document.getElementById("btnDrawerCheckIn");
    const btnCheckOut = document.getElementById("btnDrawerCheckOut");

    function openDrawer(triggerEl) {
        const roomNo = triggerEl.getAttribute("data-room-number") || "-";
        const roomType = triggerEl.getAttribute("data-room-type") || "-";
        const roomFloor = triggerEl.getAttribute("data-room-floor") || "-";
        const roomStatus = triggerEl.getAttribute("data-room-status") || "-";
        const bookingCode = triggerEl.getAttribute("data-booking-code") || "";
        const guestName = triggerEl.getAttribute("data-guest-name") || "-";
        const checkIn = triggerEl.getAttribute("data-checkin") || "-";
        const checkOut = triggerEl.getAttribute("data-checkout") || "-";
        const paymentStatus = triggerEl.getAttribute("data-payment-status") || "-";
        const canCheckIn = triggerEl.getAttribute("data-can-checkin") === "true";
        const canCheckOut = triggerEl.getAttribute("data-can-checkout") === "true";

        if (elTitle) elTitle.textContent = `Room ${roomNo}`;
        if (elRoomNumber) elRoomNumber.textContent = roomNo;
        if (elRoomFloor) elRoomFloor.textContent = `ชั้น ${roomFloor}`;
        if (elRoomType) elRoomType.textContent = roomType;
        if (elRoomStatus) elRoomStatus.textContent = roomStatus;
        if (elBookingCode) elBookingCode.textContent = bookingCode || "ไม่มีการจองในช่วงที่เลือก";
        if (elGuestName) elGuestName.textContent = guestName || "-";
        if (elCheckIn) elCheckIn.textContent = checkIn || "-";
        if (elCheckOut) elCheckOut.textContent = checkOut || "-";
        if (elPaymentStatus) elPaymentStatus.textContent = paymentStatus || "-";

        if (btnViewDetails) {
            btnViewDetails.href = bookingCode
                ? `/Reception/Bookings?search=${encodeURIComponent(bookingCode)}`
                : `/Reception/Bookings?search=${encodeURIComponent(roomNo)}`;
        }

        if (btnCheckIn) {
            btnCheckIn.href = `/Reception/CheckIn?bookingCode=${encodeURIComponent(bookingCode)}`;
            btnCheckIn.classList.toggle("d-none", !canCheckIn);
        }

        if (btnCheckOut) {
            btnCheckOut.href = `/Reception/CheckOut?bookingCode=${encodeURIComponent(bookingCode)}`;
            btnCheckOut.classList.toggle("d-none", !canCheckOut);
        }

        drawer?.classList.add("is-open");
        backdrop?.classList.add("is-open");
    }

    function closeDrawer() {
        drawer?.classList.remove("is-open");
        backdrop?.classList.remove("is-open");
    }

    document.querySelectorAll(".js-open-room-panel").forEach((btn) => {
        btn.addEventListener("click", function () {
            openDrawer(this);
        });
    });

    btnCloseDrawer?.addEventListener("click", closeDrawer);
    btnCloseFooter?.addEventListener("click", closeDrawer);
    backdrop?.addEventListener("click", closeDrawer);
});