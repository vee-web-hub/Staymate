document.addEventListener("DOMContentLoaded", function () {
    const inputCode = document.getElementById("searchBookingCodeInput");
    const inputGuest = document.getElementById("searchGuestNameInput");
    const inputRoom = document.getElementById("searchRoomNoInput");
    const selectRoomType = document.getElementById("filterCheckInRoomType");
    const selectStatus = document.getElementById("filterCheckInStatus");
    const selectSort = document.getElementById("sortCheckInDate");
    const btnClear = document.getElementById("btnClearCheckInFilters");
    const visibleCountEl = document.getElementById("checkInVisibleCount");
    const tableBody = document.getElementById("checkInTableBody");

    const rows = Array.from(document.querySelectorAll(".reception-checkin-row"));

    function applyCheckInFiltersAndSort() {
        const codeKw = (inputCode?.value || "").trim().toLowerCase();
        const guestKw = (inputGuest?.value || "").trim().toLowerCase();
        const roomKw = (inputRoom?.value || "").trim().toLowerCase();
        const typeVal = (selectRoomType?.value || "all").toLowerCase();
        const statusVal = (selectStatus?.value || "all").toLowerCase();
        const sortDir = selectSort?.value || "asc";

        const sortedRows = [...rows].sort((a, b) => {
            const dateA = a.getAttribute("data-checkin-sort") || "";
            const dateB = b.getAttribute("data-checkin-sort") || "";
            return sortDir === "asc"
                ? dateA.localeCompare(dateB)
                : dateB.localeCompare(dateA);
        });

        let visibleIndex = 1;
        sortedRows.forEach((row) => {
            const rCode = row.getAttribute("data-booking-code") || "";
            const rGuest = row.getAttribute("data-guest-name") || "";
            const rRoom = row.getAttribute("data-room-number") || "";
            const rType = row.getAttribute("data-room-type") || "";
            const rStatus = row.getAttribute("data-booking-status") || "";

            const matchCode = !codeKw || rCode.includes(codeKw);
            const matchGuest = !guestKw || rGuest.includes(guestKw);
            const matchRoom = !roomKw || rRoom.includes(roomKw);
            const matchType = typeVal === "all" || rType.includes(typeVal);
            const matchStatus = statusVal === "all" || rStatus === statusVal;

            if (matchCode && matchGuest && matchRoom && matchType && matchStatus) {
                row.style.display = "";
                const idxCell = row.querySelector(".row-index-cell");
                if (idxCell) idxCell.textContent = visibleIndex.toString();
                visibleIndex++;
            } else {
                row.style.display = "none";
            }

            tableBody?.appendChild(row);
        });

        if (visibleCountEl) {
            visibleCountEl.textContent = (visibleIndex - 1).toString();
        }
    }

    [inputCode, inputGuest, inputRoom, selectRoomType, selectStatus, selectSort].forEach((el) => {
        el?.addEventListener("input", applyCheckInFiltersAndSort);
        el?.addEventListener("change", applyCheckInFiltersAndSort);
    });

    btnClear?.addEventListener("click", function () {
        if (inputCode) inputCode.value = "";
        if (inputGuest) inputGuest.value = "";
        if (inputRoom) inputRoom.value = "";
        if (selectRoomType) selectRoomType.value = "all";
        if (selectStatus) selectStatus.value = "all";
        if (selectSort) selectSort.value = "asc";
        applyCheckInFiltersAndSort();
    });

    // กรองทันทีเมื่อโหลดหน้า (รองรับการกด Check-in มาจากหน้า Bookings)
    applyCheckInFiltersAndSort();

    // Modal ตรวจสอบและยืนยัน Check-in
    const modal = document.getElementById("checkInConfirmModal");
    const btnCloseModal = document.getElementById("btnCloseCheckInModal");
    const btnCancelModal = document.getElementById("btnCancelCheckInModal");
    const btnSubmitCheckIn = document.getElementById("btnSubmitCheckIn");
    const inputBookingId = document.getElementById("modalInputBookingId");
    const warningBanner = document.getElementById("modalCiWarningBanner");
    const warningText = document.getElementById("modalCiWarningText");
    const assignRoomBox = document.getElementById("modalAssignRoomBox");
    const selectRoomId = document.getElementById("modalSelectRoomId");

    function updateChecklistRow(elementId, isPassed) {
        const li = document.getElementById(elementId);
        if (!li) return;
        li.classList.remove("is-passed", "is-failed");
        li.classList.add(isPassed ? "is-passed" : "is-failed");

        const icon = li.querySelector("i");
        if (icon) {
            icon.className = isPassed
                ? "fa-solid fa-circle-check"
                : "fa-solid fa-circle-xmark";
        }
    }

    document.querySelectorAll(".js-open-checkin-modal").forEach((btn) => {
        btn.addEventListener("click", function () {
            const bookingId = this.getAttribute("data-booking-id") || "";
            const bookingCode = this.getAttribute("data-booking-code") || "-";
            const guestName = this.getAttribute("data-guest-name") || "-";
            const roomNumber = this.getAttribute("data-room-number") || "-";
            const roomType = this.getAttribute("data-room-type") || "-";
            const checkInDate = this.getAttribute("data-checkin-date") || "-";
            const checkOutDate = this.getAttribute("data-checkout-date") || "-";
            const numGuests = this.getAttribute("data-num-guests") || "1";

            const canCheckIn = this.getAttribute("data-can-checkin") === "true";
            const needsRoom = this.getAttribute("data-needs-room") === "true";
            const passPay = this.getAttribute("data-check-pay") === "true";
            const passConfirmed = this.getAttribute("data-check-confirmed") === "true";
            const passNotDup = this.getAttribute("data-check-notdup") === "true";
            const passDate = this.getAttribute("data-check-date") === "true";
            const passRoomAssigned = this.getAttribute("data-check-roomassigned") === "true";
            const passRoomAvail = this.getAttribute("data-check-roomavail") === "true";
            const blockReason = this.getAttribute("data-block-reason") || "";

            document.getElementById("modalCiBookingCode").textContent = bookingCode;
            document.getElementById("modalCiGuestName").textContent = guestName;
            document.getElementById("modalCiRoomNumber").textContent = `${roomNumber} (${roomType})`;
            document.getElementById("modalCiNumGuests").textContent = `${numGuests} ท่าน`;
            document.getElementById("modalCiCheckInDate").textContent = checkInDate;
            document.getElementById("modalCiCheckOutDate").textContent = checkOutDate;

            if (inputBookingId) inputBookingId.value = bookingId;

            if (assignRoomBox && selectRoomId) {
                if (needsRoom && canCheckIn) {
                    assignRoomBox.classList.remove("d-none");
                    selectRoomId.required = true;
                    selectRoomId.value = "";

                    const wantType = (roomType || "").trim().toLowerCase();
                    Array.from(selectRoomId.options).forEach((opt) => {
                        if (!opt.value) return;
                        const ok = !wantType || (opt.getAttribute("data-room-type") || "").trim() === wantType;
                        opt.hidden = !ok;
                        opt.disabled = !ok;
                    });
                } else {
                    assignRoomBox.classList.add("d-none");
                    selectRoomId.required = false;
                    selectRoomId.value = "";
                }
            }

            updateChecklistRow("chkItemPayment", passPay);
            updateChecklistRow("chkItemConfirmed", passConfirmed);
            updateChecklistRow("chkItemNotDup", passNotDup);
            updateChecklistRow("chkItemDate", passDate);
            updateChecklistRow("chkItemRoomAssigned", passRoomAssigned);
            updateChecklistRow("chkItemRoomAvail", passRoomAvail);

            if (!canCheckIn) {
                warningBanner?.classList.remove("d-none");
                if (warningText) warningText.textContent = blockReason;
                if (btnSubmitCheckIn) btnSubmitCheckIn.disabled = true;
            } else {
                warningBanner?.classList.add("d-none");
                if (btnSubmitCheckIn) btnSubmitCheckIn.disabled = false;
            }

            modal?.classList.add("is-open");
        });
    });

    function closeCheckInModal() {
        modal?.classList.remove("is-open");
    }

    btnCloseModal?.addEventListener("click", closeCheckInModal);
    btnCancelModal?.addEventListener("click", closeCheckInModal);
});