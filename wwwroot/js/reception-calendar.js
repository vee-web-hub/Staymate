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

    // 3. Instant Search & Filter (Room Number, Booking ID, Guest Name, Room Type, Room Status)
    const searchInput = document.getElementById("calendarSearchInput");
    const typeSelect = document.getElementById("filterRoomType");
    const statusSelect = document.getElementById("filterRoomStatus");
    const resetBtn = document.getElementById("btnResetCalendarFilter");
    const roomRows = document.querySelectorAll(".reception-timeline-room-row");

    function applyCalendarFilters() {
        const keyword = (searchInput?.value || "").trim().toLowerCase();
        const selectedType = (typeSelect?.value || "all").toLowerCase();
        const selectedStatus = (statusSelect?.value || "all").toLowerCase();

        roomRows.forEach((row) => {
            const rowSearchText = row.getAttribute("data-search-text") || "";
            const rowType = row.getAttribute("data-room-type") || "";
            const rowStatus = row.getAttribute("data-room-status") || "";

            const matchKeyword = !keyword || rowSearchText.includes(keyword);
            const matchType = selectedType === "all" || rowType.includes(selectedType);
            const matchStatus = selectedStatus === "all" || rowStatus === selectedStatus;

            row.style.display = (matchKeyword && matchType && matchStatus) ? "" : "none";
        });
    }

    searchInput?.addEventListener("input", applyCalendarFilters);
    typeSelect?.addEventListener("change", applyCalendarFilters);
    statusSelect?.addEventListener("change", applyCalendarFilters);

    resetBtn?.addEventListener("click", function () {
        if (searchInput) searchInput.value = "";
        if (typeSelect) typeSelect.value = "all";
        if (statusSelect) statusSelect.value = "all";
        applyCalendarFilters();
    });

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
            btnCheckIn.classList.toggle("d-none", !canCheckIn);
        }

        if (btnCheckOut) {
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
