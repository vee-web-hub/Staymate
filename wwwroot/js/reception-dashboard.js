/**
 * StayMate Reception Dashboard Interaction Script
 * File: wwwroot/js/reception-dashboard.js
 * Note: Strictly preserves server-bound Razor Model values; handles UI interactions only.
 */
document.addEventListener("DOMContentLoaded", function () {

    // 1. KPI Number Counter Animation (อ่านค่าจริงจาก data-target ที่ Razor Binding ไว้เท่านั้น)
    const counters = document.querySelectorAll(".reception-kpi-number[data-target]");
    counters.forEach((counter) => {
        const target = parseInt(counter.getAttribute("data-target"), 10);
        if (isNaN(target) || target <= 0) {
            counter.textContent = isNaN(target) ? "0" : target.toString();
            return;
        }

        let current = 0;
        const duration = 550;
        const stepTime = 16;
        const increment = Math.max(1, Math.ceil(target / (duration / stepTime)));

        const timer = setInterval(() => {
            current += increment;
            if (current >= target) {
                counter.textContent = target.toLocaleString("th-TH");
                clearInterval(timer);
            } else {
                counter.textContent = current.toLocaleString("th-TH");
            }
        }, stepTime);
    });

    // 2. ปุ่ม Refresh พร้อมสถานะ Loading
    const btnRefresh = document.getElementById("btnRefreshDashboard");
    if (btnRefresh) {
        btnRefresh.addEventListener("click", function () {
            btnRefresh.classList.add("is-loading");
            btnRefresh.disabled = true;
            window.location.reload();
        });
    }

   // 3. ตัวกรองสถานะห้องพัก + แบ่งหน้า (Room Status Overview Filter & Pagination)
    const filterButtons = document.querySelectorAll(".reception-filter-chip[data-filter]");
    const allRoomCards = Array.from(document.querySelectorAll("#receptionRoomGrid .reception-room-card"));
    const roomPageSummary = document.getElementById("roomPageSummary");
    const roomPageButtons = document.getElementById("roomPageButtons");

    if (allRoomCards.length > 0) {
        const roomsPerPage = 18; // 2 แถว x 9 ห้อง พอดีกับความสูงของกล่อง Recent Bookings ด้านขวา
        let currentRoomFilter = "all";
        let currentRoomPage = 1;

        function renderRoomOverview() {
            const matchedCards = allRoomCards.filter((card) => {
                const status = card.getAttribute("data-room-status");
                return currentRoomFilter === "all" || status === currentRoomFilter;
            });

            const totalItems = matchedCards.length;
            const totalPages = Math.max(1, Math.ceil(totalItems / roomsPerPage));
            if (currentRoomPage > totalPages) currentRoomPage = totalPages;

            const startIdx = (currentRoomPage - 1) * roomsPerPage;
            const endIdx = startIdx + roomsPerPage;

            // ซ่อนการ์ดทั้งหมดก่อน แล้วแสดงเฉพาะรายการในหน้าปัจจุบัน
            allRoomCards.forEach((card) => (card.style.display = "none"));
            matchedCards.slice(startIdx, endIdx).forEach((card) => {
                card.style.display = "";
            });

            // อัปเดตข้อความสรุปจำนวน
            if (roomPageSummary) {
                roomPageSummary.textContent = totalItems > 0
                    ? `แสดงห้องที่ ${startIdx + 1} - ${Math.min(endIdx, totalItems)} จาก ${totalItems} ห้อง`
                    : "ไม่พบห้องพักในสถานะที่เลือก";
            }

            // สร้างปุ่มเลขหน้า
            if (roomPageButtons) {
                roomPageButtons.innerHTML = "";
                if (totalPages > 1) {
                    for (let p = 1; p <= totalPages; p++) {
                        const btn = document.createElement("button");
                        btn.type = "button";
                        btn.className = `reception-page-btn ${p === currentRoomPage ? "active" : ""}`;
                        btn.textContent = p.toString();
                        btn.addEventListener("click", () => {
                            currentRoomPage = p;
                            renderRoomOverview();
                        });
                        roomPageButtons.appendChild(btn);
                    }
                }
            }
        }

        filterButtons.forEach((btn) => {
            btn.addEventListener("click", function () {
                currentRoomFilter = this.getAttribute("data-filter") || "all";
                currentRoomPage = 1;

                filterButtons.forEach((b) => b.classList.remove("active"));
                this.classList.add("active");

                renderRoomOverview();
            });
        });

        renderRoomOverview();
    }
    // 4. ยืนยันก่อนดำเนินการ Check-in / Check-out (รองรับ SweetAlert2 หากติดตั้งไว้ใน Layout)
    const confirmLinks = document.querySelectorAll(".js-confirm-checkin, .js-confirm-checkout");
    confirmLinks.forEach((link) => {
        link.addEventListener("click", function (e) {
            const isCheckIn = this.classList.contains("js-confirm-checkin");
            const bookingCode = this.getAttribute("data-booking") || "-";
            const guestName = this.getAttribute("data-guest") || "-";
            const roomNumber = this.getAttribute("data-room") || "-";
            const targetUrl = this.getAttribute("href");

            if (typeof Swal !== "undefined") {
                e.preventDefault();
                Swal.fire({
                    title: isCheckIn ? "ยืนยันการเข้าพัก (Check-in)?" : "ยืนยันการคืนห้องพัก (Check-out)?",
                    html: `รหัสการจอง: <b>${bookingCode}</b><br>ลูกค้า: <b>${guestName}</b><br>ห้องพัก: <b>${roomNumber}</b>`,
                    icon: "question",
                    showCancelButton: true,
                    confirmButtonColor: isCheckIn ? "#16A34A" : "#EF4444",
                    cancelButtonColor: "#64748B",
                    confirmButtonText: isCheckIn ? "ดำเนินการ Check-in" : "ดำเนินการ Check-out",
                    cancelButtonText: "ยกเลิก"
                }).then((result) => {
                    if (result.isConfirmed && targetUrl) {
                        window.location.href = targetUrl;
                    }
                });
            }
        });
    });
});
