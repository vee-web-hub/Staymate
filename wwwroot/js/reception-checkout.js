/**
 * StayMate Check-out Management Script
 * File: wwwroot/js/reception-checkout.js
 */
document.addEventListener("DOMContentLoaded", function () {

    // 1. ระบบสลับ Tab ระหว่าง "รอ Check-out" และ "ประวัติ Check-out"
    const tabButtons = document.querySelectorAll(".reception-checkout-tab-btn");
    const tabPanes = document.querySelectorAll(".reception-checkout-tab-pane");

    function switchTab(targetId) {
        tabButtons.forEach((btn) => {
            const isMatch = btn.getAttribute("data-tab-target") === targetId;
            btn.classList.toggle("active", isMatch);
            btn.setAttribute("aria-selected", isMatch ? "true" : "false");
        });

        tabPanes.forEach((pane) => {
            pane.classList.toggle("active", pane.id === targetId);
        });
    }

    tabButtons.forEach((btn) => {
        btn.addEventListener("click", function () {
            const targetId = this.getAttribute("data-tab-target");
            if (targetId) switchTab(targetId);
        });
    });

    // 2. ระบบค้นหาและตัวกรองแบบทันที (Instant Filter)
    const guestInput = document.getElementById("coSearchGuest");
    const codeInput = document.getElementById("coSearchCode");
    const roomInput = document.getElementById("coSearchRoom");
    const statusSelect = document.getElementById("coFilterStatus");

    const allRows = document.querySelectorAll(".reception-checkout-row");

    function applyCheckOutFilter() {
        const guestKw = (guestInput?.value || "").trim().toLowerCase();
        const codeKw = (codeInput?.value || "").trim().toLowerCase();
        const roomKw = (roomInput?.value || "").trim().toLowerCase();
        const statusVal = (statusSelect?.value || "all").toLowerCase();

        // หากเลือกตัวกรอง "completed" ให้สลับไปเปิด Tab ประวัติอัตโนมัติ
        if (statusVal === "completed") {
            switchTab("tabHistoryCheckOut");
        } else if (statusVal !== "all") {
            switchTab("tabPendingCheckOut");
        }

        allRows.forEach((row) => {
            const rGuest = row.getAttribute("data-guest") || "";
            const rCode = row.getAttribute("data-code") || "";
            const rRoom = row.getAttribute("data-room") || "";
            const rStatus = row.getAttribute("data-status") || "";

            const matchGuest = !guestKw || rGuest.includes(guestKw);
            const matchCode = !codeKw || rCode.includes(codeKw);
            const matchRoom = !roomKw || rRoom.includes(roomKw);
            const matchStatus = statusVal === "all" || rStatus === statusVal;

            row.style.display = (matchGuest && matchCode && matchRoom && matchStatus) ? "" : "none";
        });
    }

    [guestInput, codeInput, roomInput, statusSelect].forEach((el) => {
        el?.addEventListener("input", applyCheckOutFilter);
        el?.addEventListener("change", applyCheckOutFilter);
    });

    // 3. ระบบเปิด Modal ยืนยันการ Check-out
    const modal = document.getElementById("checkOutConfirmModal");
    const btnClose = document.getElementById("btnCloseCheckOutModal");
    const btnCancel = document.getElementById("btnCancelCheckOutModal");
    const inputBookingId = document.getElementById("modalCoInputBookingId");

    document.querySelectorAll(".js-open-checkout-modal").forEach((btn) => {
        btn.addEventListener("click", function () {
            const bookingId = this.getAttribute("data-booking-id") || "";
            const bookingCode = this.getAttribute("data-booking-code") || "-";
            const guestName = this.getAttribute("data-guest-name") || "-";
            const guestPhone = this.getAttribute("data-guest-phone") || "-";
            const roomNumber = this.getAttribute("data-room-number") || "-";
            const roomType = this.getAttribute("data-room-type") || "-";
            const checkInDate = this.getAttribute("data-checkin-date") || "-";
            const checkOutDate = this.getAttribute("data-checkout-date") || "-";
            const checkInTime = this.getAttribute("data-checkin-time") || "-";
            const guests = this.getAttribute("data-guests") || "1";
            const statusText = this.getAttribute("data-status-text") || "-";

            document.getElementById("modalCoBookingCode").textContent = bookingCode;
            document.getElementById("modalCoGuestName").textContent = guestName;
            document.getElementById("modalCoRoomNumber").textContent = `ห้อง ${roomNumber} (${roomType})`;
            document.getElementById("modalCoGuestPhone").textContent = guestPhone;
            document.getElementById("modalCoStayDates").textContent = `${checkInDate} - ${checkOutDate}`;
            document.getElementById("modalCoCheckInTime").textContent = checkInTime;
            document.getElementById("modalCoGuests").textContent = `${guests} ท่าน`;
            document.getElementById("modalCoStatusText").textContent = statusText;

            if (inputBookingId) {
                inputBookingId.value = bookingId;
            }

            modal?.classList.add("is-open");
        });
    });

    function closeCheckOutModal() {
        modal?.classList.remove("is-open");
    }

    btnClose?.addEventListener("click", closeCheckOutModal);
    btnCancel?.addEventListener("click", closeCheckOutModal);
});
