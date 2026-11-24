/**
 * StayMate Booking Management & Zero-Trust QR Scanner Script
 * File: wwwroot/js/reception-bookings.js
 */
document.addEventListener("DOMContentLoaded", function () {

    // 1. Refresh Page
    const btnRefresh = document.getElementById("btnRefreshBookings");
    btnRefresh?.addEventListener("click", function () {
        this.classList.add("is-loading");
        window.location.reload();
    });

    // 2. Client-Side Instant Filter & Pagination over Real Server Rows
    const keywordInput = document.getElementById("bookingKeywordInput");
    const filterCreated = document.getElementById("filterCreatedDate");
    const filterCheckIn = document.getElementById("filterCheckInDate");
    const filterCheckOut = document.getElementById("filterCheckOutDate");
    const filterRoomType = document.getElementById("filterRoomType");
    const filterBookingStatus = document.getElementById("filterBookingStatus");
    const filterPaymentStatus = document.getElementById("filterPaymentStatus");
    const visibleCountEl = document.getElementById("visibleBookingCount");

    const allRows = Array.from(document.querySelectorAll(".reception-booking-row"));
    const pageSize = 10;
    let currentPage = 1;
    let filteredRows = [...allRows];

    function applyTableFilters() {
        const kw = (keywordInput?.value || "").trim().toLowerCase();
        const cDate = filterCreated?.value || "";
        const inDate = filterCheckIn?.value || "";
        const outDate = filterCheckOut?.value || "";
        const rType = (filterRoomType?.value || "all").toLowerCase();
        const bStatus = (filterBookingStatus?.value || "all").toLowerCase();
        const pStatus = (filterPaymentStatus?.value || "all").toLowerCase();

        filteredRows = allRows.filter((row) => {
            const blob = row.getAttribute("data-search-blob") || "";
            const rowCreated = row.getAttribute("data-created-iso") || "";
            const rowIn = row.getAttribute("data-checkin-iso") || "";
            const rowOut = row.getAttribute("data-checkout-iso") || "";
            const rowTypes = (row.getAttribute("data-room-types") || "").toLowerCase();
            const rowBStatus = (row.getAttribute("data-booking-status") || "").toLowerCase();
            const rowPStatus = (row.getAttribute("data-payment-status") || "").toLowerCase();

            const matchKw = !kw || blob.includes(kw);
            const matchCreated = !cDate || rowCreated === cDate;
            const matchIn = !inDate || rowIn === inDate;
            const matchOut = !outDate || rowOut === outDate;
            const matchType = rType === "all" || rowTypes.includes(rType);
            const matchBStatus = bStatus === "all" || rowBStatus === bStatus;
            const matchPStatus = pStatus === "all" || rowPStatus === pStatus;

            return matchKw && matchCreated && matchIn && matchOut && matchType && matchBStatus && matchPStatus;
        });

        if (visibleCountEl) {
            visibleCountEl.textContent = filteredRows.length.toString();
        }

        currentPage = 1;
        renderPage();
    }

    function renderPage() {
        allRows.forEach((r) => (r.style.display = "none"));

        const totalPages = Math.max(1, Math.ceil(filteredRows.length / pageSize));
        if (currentPage > totalPages) currentPage = totalPages;

        const startIdx = (currentPage - 1) * pageSize;
        const endIdx = startIdx + pageSize;

        filteredRows.slice(startIdx, endIdx).forEach((r) => {
            r.style.display = "";
        });

        const summaryEl = document.getElementById("paginationSummaryText");
        if (summaryEl) {
            summaryEl.textContent = filteredRows.length > 0
                ? `แสดง ${startIdx + 1} - ${Math.min(endIdx, filteredRows.length)} จากทั้งหมด ${filteredRows.length} รายการ`
                : "ไม่พบรายการที่ตรงกับเงื่อนไข";
        }

        const btnContainer = document.getElementById("paginationButtons");
        if (btnContainer) {
            btnContainer.innerHTML = "";
            for (let p = 1; p <= totalPages; p++) {
                const btn = document.createElement("button");
                btn.type = "button";
                btn.className = `reception-page-btn ${p === currentPage ? "active" : ""}`;
                btn.textContent = p.toString();
                btn.addEventListener("click", () => {
                    currentPage = p;
                    renderPage();
                });
                btnContainer.appendChild(btn);
            }
        }
    }

    [keywordInput, filterCreated, filterCheckIn, filterCheckOut, filterRoomType, filterBookingStatus, filterPaymentStatus].forEach((el) => {
        el?.addEventListener("input", applyTableFilters);
        el?.addEventListener("change", applyTableFilters);
    });

    if (allRows.length > 0) {
        renderPage();
    }

    // 3. Booking Details Modal Population
    const detailsModal = document.getElementById("bookingDetailsModal");
    const btnCloseDetails = document.getElementById("btnCloseDetailsModal");
    const btnCloseDetailsFooter = document.getElementById("btnCloseDetailsFooter");

    function setTimelineStep(stepId, timeId, valueText) {
        const stepEl = document.getElementById(stepId);
        const timeEl = document.getElementById(timeId);
        const isCompleted = valueText && valueText !== "Waiting" && valueText !== "-";

        if (stepEl) stepEl.classList.toggle("is-done", Boolean(isCompleted));
        if (timeEl) timeEl.textContent = isCompleted ? valueText : "Waiting";
    }

    function openBookingDetailsFromData(data) {
        document.getElementById("detailHeaderCode").textContent = data.bookingCode || `#${data.bookingId}`;
        document.getElementById("detBookingCode").textContent = data.bookingCode || `#${data.bookingId}`;
        document.getElementById("detGuestName").textContent = data.guestName || "-";
        document.getElementById("detGuestPhone").textContent = data.guestPhone || "-";
        document.getElementById("detGuestEmail").textContent = data.guestEmail || "-";

        document.getElementById("detRoomNumber").textContent = data.roomNumbers || "รอกำหนดห้อง";
        document.getElementById("detRoomType").textContent = data.roomTypes || "-";
        document.getElementById("detCheckInDate").textContent = data.checkInDisplay || "-";
        document.getElementById("detCheckOutDate").textContent = data.checkOutDisplay || "-";
        document.getElementById("detNumGuests").textContent = `${data.guests || 1} ท่าน`;
        document.getElementById("detNumNights").textContent = `${data.nights || 1} คืน`;

        document.getElementById("detRoomPrice").textContent = `฿${data.totalAmount || "0.00"}`;
        document.getElementById("detDiscount").textContent = `฿${data.discountAmount || "0.00"}`;
        document.getElementById("detTax").textContent = `฿${data.taxAmount || "0.00"}`;
        document.getElementById("detFinalAmount").textContent = `฿${data.totalAmount || "0.00"}`;

        const payBadgeEl = document.getElementById("detPaymentBadge");
        if (payBadgeEl) {
            payBadgeEl.className = `reception-status-badge reception-pay-badge-${data.paymentStatus || "pending"}`;
            payBadgeEl.textContent = data.paymentLabel || "Pending";
        }

        const bookBadgeEl = document.getElementById("detBookingBadge");
        if (bookBadgeEl) {
            bookBadgeEl.className = `reception-status-badge reception-bstatus-${(data.bookingStatus || "pending").replace("_", "")}`;
            bookBadgeEl.textContent = data.bookingLabel || "Pending";
        }

        document.getElementById("detCreatedDate").textContent = data.createdAt || "-";
        document.getElementById("detApprovedDate").textContent = data.paymentApprovedAt || "Waiting";

        // ใช้เฉพาะ Booking Reference (ไม่ฝังชื่อหรือยอดเงินใน QR)
        const refToken = data.bookingCode || `BK-${data.bookingId}`;
        document.getElementById("detQrReference").textContent = refToken;
        const qrImg = document.getElementById("detQrImage");
        if (qrImg) {
            qrImg.src = `https://api.qrserver.com/v1/create-qr-code/?size=140x140&data=${encodeURIComponent(refToken)}`;
        }

        // อัปเดต 6 ขั้นตอนของ Booking Lifecycle Timeline
        setTimelineStep("stepCreated", "timeCreated", data.createdAt);
        setTimelineStep("stepPaySubmitted", "timePaySubmitted", data.paymentSubmittedAt);
        setTimelineStep("stepPayApproved", "timePayApproved", data.paymentApprovedAt);
        setTimelineStep("stepConfirmed", "timeConfirmed", data.bookingStatus !== "pending" && data.bookingStatus !== "cancelled" ? (data.paymentApprovedAt || "Confirmed") : "Waiting");
        setTimelineStep("stepCheckedIn", "timeCheckedIn", data.checkedInAt);
        setTimelineStep("stepCheckedOut", "timeCheckedOut", data.checkedOutAt);

        // [บรรทัดแก้ไข]: แสดงปุ่ม Check-in / Check-out ตามเงื่อนไขด้วย JS โดยดึงค่าสถานะสิทธิ์จาก Element Data
        const goCheckIn = document.getElementById("btnModalGoCheckIn");
        const formCheckOut = document.getElementById("formModalCheckOut");
        const outInput = document.getElementById("inputCheckOutBookingId");

        if (outInput) outInput.value = data.bookingId || "";

        if (goCheckIn) {
            goCheckIn.href = `/Reception/CheckIn?bookingCode=${encodeURIComponent(data.bookingCode || "")}`;
            goCheckIn.classList.toggle("d-none", !data.canCheckIn);
        }
        formCheckOut?.classList.toggle("d-none", !data.canCheckOut);

        detailsModal?.classList.add("is-open");
    }

    function openDetailsFromRow(row) {
        openBookingDetailsFromData({
            bookingId: row.getAttribute("data-booking-id"),
            bookingCode: row.getAttribute("data-booking-code"),
            guestName: row.getAttribute("data-guest-name"),
            guestPhone: row.getAttribute("data-guest-phone"),
            guestEmail: row.getAttribute("data-guest-email"),
            roomNumbers: row.getAttribute("data-room-numbers"),
            roomTypes: row.getAttribute("data-room-types"),
            checkInDisplay: row.getAttribute("data-checkin-display"),
            checkOutDisplay: row.getAttribute("data-checkout-display"),
            nights: row.getAttribute("data-nights"),
            guests: row.getAttribute("data-guests"),
            totalAmount: row.getAttribute("data-total-amount"),
            discountAmount: row.getAttribute("data-discount-amount"),
            taxAmount: row.getAttribute("data-tax-amount"),
            paymentStatus: row.getAttribute("data-payment-status"),
            paymentLabel: row.getAttribute("data-payment-label"),
            bookingStatus: row.getAttribute("data-booking-status"),
            bookingLabel: row.getAttribute("data-booking-label"),
            createdAt: row.getAttribute("data-created-at"),
            paymentSubmittedAt: row.getAttribute("data-payment-submitted-at"),
            paymentApprovedAt: row.getAttribute("data-payment-approved-at"),
            checkedInAt: row.getAttribute("data-checked-in-at"),
            checkedOutAt: row.getAttribute("data-checked-out-at"),
            canCheckIn: row.getAttribute("data-can-checkin") === "true",
            canCheckOut: row.getAttribute("data-can-checkout") === "true"
        });
    }

    document.querySelectorAll(".js-open-booking-details, .js-open-booking-qr").forEach((btn) => {
        btn.addEventListener("click", function () {
            const row = this.closest(".reception-booking-row");
            if (row) openDetailsFromRow(row);
        });
    });

    function closeDetailsModal() {
        detailsModal?.classList.remove("is-open");
    }

    btnCloseDetails?.addEventListener("click", closeDetailsModal);
    btnCloseDetailsFooter?.addEventListener("click", closeDetailsModal);

    // 4. QR Code Scanner & Backend Verification
    const qrModal = document.getElementById("qrScannerModal");
    const btnOpenQr = document.getElementById("btnOpenQrScanner");
    const btnCloseQr = document.getElementById("btnCloseQrModal");
    const btnCloseQrFooter = document.getElementById("btnCloseQrModalFooter");
    const btnStartCam = document.getElementById("btnStartCamera");
    const btnStopCam = document.getElementById("btnStopCamera");
    const facingSelect = document.getElementById("qrCameraFacingSelect");
    const placeholderEl = document.getElementById("qrCameraPlaceholder");
    const statusBox = document.getElementById("qrVerifyStatusBox");
    const manualInput = document.getElementById("manualBookingCodeInput");
    const btnManualVerify = document.getElementById("btnManualVerifyBooking");

    let html5QrScanner = null;
    let isScanning = false;

    function showQrStatus(type, message) {
        if (!statusBox) return;
        statusBox.className = `reception-qr-status-box reception-alert-${type}`;
        statusBox.innerHTML = message;
        statusBox.classList.remove("d-none");
    }

    async function stopCameraStream() {
        if (html5QrScanner && isScanning) {
            try {
                await html5QrScanner.stop();
                await html5QrScanner.clear();
            } catch (err) {
                console.warn("Camera stop warning:", err);
            }
        }
        isScanning = false;
        if (btnStartCam) btnStartCam.disabled = false;
        if (btnStopCam) btnStopCam.disabled = true;
        if (placeholderEl) placeholderEl.style.display = "flex";
    }

    async function startCameraStream() {
        if (typeof Html5Qrcode === "undefined") {
            showQrStatus("danger", "ไม่พบไลบรารีกล้อง QR Scanner กรุณากรอก Booking ID ด้วยตนเอง");
            return;
        }

        await stopCameraStream();
        html5QrScanner = new Html5Qrcode("qrReaderViewport");
        const facingMode = facingSelect?.value || "environment";

        try {
            if (placeholderEl) placeholderEl.style.display = "none";
            await html5QrScanner.start(
                { facingMode: facingMode },
                { fps: 10, qrbox: { width: 210, height: 210 } },
                async (decodedText) => {
                    // เมื่อสแกนสำเร็จ ให้หยุดกล้องทันทีและส่งไปตรวจสอบที่ Backend
                    await stopCameraStream();
                    await verifyQrPayloadWithBackend(decodedText);
                },
                () => {}
            );
            isScanning = true;
            if (btnStartCam) btnStartCam.disabled = true;
            if (btnStopCam) btnStopCam.disabled = false;
        } catch (err) {
            if (placeholderEl) placeholderEl.style.display = "flex";
            showQrStatus("danger", "ไม่สามารถเข้าถึงกล้องได้ กรุณาอนุญาตสิทธิ์การใช้กล้อง (Camera Permission) หรือใช้ช่องกรอกรหัสการจองด้านข้าง");
        }
    }

    // ฟังก์ชันสกัดเฉพาะ Booking Reference/Token แล้วตรวจสอบกับฐานข้อมูลฝั่ง Server
    async function verifyQrPayloadWithBackend(rawPayload) {
        const cleaned = (rawPayload || "").trim();
        if (!cleaned) {
            showQrStatus("danger", "กรุณาระบุรหัส Booking ID หรือสแกน QR Code ที่ถูกต้อง");
            return;
        }

        showQrStatus("success", `<i class="fa-solid fa-spinner fa-spin"></i> กำลังตรวจสอบรหัส <b>${cleaned}</b> กับฐานข้อมูล StayMate...`);

        try {
            const response = await fetch(`/Reception/VerifyBookingQr?qrPayload=${encodeURIComponent(cleaned)}`, {
                method: "GET",
                headers: { "Accept": "application/json" }
            });

            if (response.ok) {
                const result = await response.json();
                if (!result.success) {
                    showQrStatus("danger", `<i class="fa-solid fa-circle-xmark"></i> ${result.message || "ไม่พบข้อมูลการจองในระบบ"}`);
                    return;
                }

                // พบข้อมูลจากฐานข้อมูลจริง -> ปิดหน้าสแกนและเปิด Booking Details Modal
                await closeQrModal();
                openBookingDetailsFromData(result.data);
                return;
            }
        } catch (e) {
            console.warn("VerifyBookingQr endpoint fallback to server-rendered row lookup:", e);
        }

        // Fallback ค้นหาจากแถวข้อมูลที่ Server เรนเดอร์มาจากฐานข้อมูลแล้ว
        const matchedRow = allRows.find((r) => {
            const code = (r.getAttribute("data-booking-code") || "").toLowerCase();
            const id = (r.getAttribute("data-booking-id") || "").toLowerCase();
            const target = cleaned.toLowerCase();
            return code === target || id === target || target.includes(code);
        });

        if (matchedRow) {
            const bStatus = matchedRow.getAttribute("data-booking-status");
            if (bStatus === "cancelled") {
                showQrStatus("danger", `<i class="fa-solid fa-ban"></i> การจองรหัส <b>${cleaned}</b> ถูกยกเลิกแล้ว ไม่สามารถใช้งานได้`);
                return;
            }
            await closeQrModal();
            openDetailsFromRow(matchedRow);
        } else {
            showQrStatus("danger", `<i class="fa-solid fa-triangle-exclamation"></i> ไม่พบรหัสการจอง <b>${cleaned}</b> ในระบบ หรือ QR Code ไม่ถูกต้อง`);
        }
    }

    async function closeQrModal() {
        await stopCameraStream();
        qrModal?.classList.remove("is-open");
    }

    btnOpenQr?.addEventListener("click", function () {
        if (statusBox) statusBox.classList.add("d-none");
        if (manualInput) manualInput.value = "";
        qrModal?.classList.add("is-open");
    });

    btnStartCam?.addEventListener("click", startCameraStream);
    btnStopCam?.addEventListener("click", stopCameraStream);
    btnCloseQr?.addEventListener("click", closeQrModal);
    btnCloseQrFooter?.addEventListener("click", closeQrModal);

    btnManualVerify?.addEventListener("click", function () {
        verifyQrPayloadWithBackend(manualInput?.value || "");
    });

    window.addEventListener("beforeunload", stopCameraStream);
});
