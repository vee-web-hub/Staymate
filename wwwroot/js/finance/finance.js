/**
 * STAYMATE – FUTURE URBAN HOTEL
 * File: wwwroot/js/finance/finance.js
 * Description: Finance Portal (Dashboard, Payment Approval, Transactions, Receipts, Detail pages, Reports)
 *
 * โครงสร้าง: ทุกโมดูลอยู่ระดับเดียวกัน และเริ่มทำงานผ่าน onReady() เท่านั้น
 * (เดิมโมดูลถูกซ้อนอยู่ใน DOMContentLoaded ของ Dashboard ทำให้ Transactions / Receipts /
 *  Detail / Reports ไม่เคยถูก init)
 */
(function () {
    "use strict";

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------
    const onReady = (fn) => {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", fn);
        else fn();
    };

    const escapeHtml = (unsafe) => {
        if (unsafe === null || unsafe === undefined) return "";
        return String(unsafe)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    };

    const formatTHB = (val) => {
        const num = Number(val) || 0;
        return "฿" + num.toLocaleString("th-TH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    };

    const setChartDefaults = () => {
        if (typeof Chart !== "undefined") {
            Chart.defaults.font.family = "'Prompt', sans-serif";
            Chart.defaults.color = "#64748B";
        }
    };

    const readJsonScript = (id, fallback) => {
        const el = document.getElementById(id);
        if (el && el.textContent) {
            try { return JSON.parse(el.textContent); }
            catch (err) { console.error(`Failed to parse #${id}:`, err); }
        }
        return fallback;
    };

    const downloadCsv = (lines, fileName) => {
        // BOM (\uFEFF) ให้ Excel อ่านภาษาไทยไม่เพี้ยน
        const blob = new Blob(["\uFEFF" + lines.join("\r\n")], { type: "text/csv;charset=utf-8;" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    };

    const csvEsc = (v) => `"${String(v ?? "").replace(/"/g, '""')}"`;
    const todayStamp = () => new Date().toISOString().slice(0, 10).replace(/-/g, "");

    const renderPager = (pg) => {
        let html = `
            <li class="page-item ${!pg.hasPrevious ? "disabled" : ""}">
                <a class="page-link" href="#" data-page="${pg.page - 1}"><i class="fa-solid fa-chevron-left"></i></a>
            </li>`;
        const startPage = Math.max(1, pg.page - 2);
        const endPage = Math.min(pg.totalPages, pg.page + 2);
        for (let p = startPage; p <= endPage; p++) {
            html += `
                <li class="page-item ${p === pg.page ? "active" : ""}">
                    <a class="page-link" href="#" data-page="${p}">${p}</a>
                </li>`;
        }
        html += `
            <li class="page-item ${!pg.hasNext ? "disabled" : ""}">
                <a class="page-link" href="#" data-page="${pg.page + 1}"><i class="fa-solid fa-chevron-right"></i></a>
            </li>`;
        return html;
    };

    // =====================================================================
    // 1. DASHBOARD
    // =====================================================================
    function initDashboard() {
        const revCanvas = document.getElementById("revenueAnalyticsChart");
        const donutCanvas = document.getElementById("paymentStatusDonutChart");
        const refreshBtn = document.getElementById("btnRefreshDashboard");
        const refreshIcon = document.getElementById("refreshIcon");

        // ปุ่ม Refresh ใช้ได้ทุกหน้าที่มีปุ่มนี้
        if (refreshBtn) {
            refreshBtn.addEventListener("click", function () {
                refreshIcon?.classList.add("fin-spin-anim");
                refreshBtn.disabled = true;
                window.location.reload();
            });
        }

        if (!revCanvas && !donutCanvas) return;
        setChartDefaults();

        const dashboardData = Object.assign({
            last7Days: [], last6Months: [], monthIncome: 0, todayIncome: 0, totalIncome: 0,
            statusCounts: { pending: 0, approved: 0, rejected: 0 },
            statusAmounts: { pending: 0, approved: 0, rejected: 0 }
        }, readJsonScript("financeDashboardData", {}));

        const rowLabel = (r) => r.label || r.date || "";

        const buildRevenueDataset = (periodKey) => {
            if (periodKey === "7d") {
                const rows = dashboardData.last7Days || [];
                return {
                    labels: rows.map(rowLabel),
                    grossData: rows.map(r => r.income),
                    netData: rows.map(r => r.net),
                    totalNet: rows.reduce((acc, cur) => acc + (Number(cur.net) || 0), 0),
                    periodTitle: "รายรับสุทธิ (7 วันล่าสุด)",
                    sourceNote: "แสดงข้อมูลจริงรายวัน (7 วันย้อนหลังจากฐานข้อมูล)"
                };
            }
            if (periodKey === "30d") {
                const rows = dashboardData.last7Days || [];
                return {
                    labels: rows.map(rowLabel),
                    grossData: rows.map(r => r.income),
                    netData: rows.map(r => r.net),
                    totalNet: Number(dashboardData.monthIncome) || 0,
                    periodTitle: "รายรับสุทธิ (เดือนปัจจุบัน / 30 วัน)",
                    sourceNote: "แสดงยอดรวมสุทธิของเดือนปัจจุบัน พร้อมแนวโน้มรายวันล่าสุดจากฐานข้อมูลจริง"
                };
            }
            const rows = dashboardData.last6Months || [];
            return {
                labels: rows.map(rowLabel),
                grossData: rows.map(r => r.income),
                netData: rows.map(r => r.net),
                totalNet: rows.reduce((acc, cur) => acc + (Number(cur.net) || 0), 0),
                periodTitle: "รายรับสุทธิ (รายเดือนสะสม)",
                sourceNote: "แสดงข้อมูลจริงจากฐานข้อมูลรายเดือน (6 เดือนล่าสุดจากระบบ)"
            };
        };

        let revenueChartInstance = null;

        if (revCanvas && typeof Chart !== "undefined") {
            const ctx = revCanvas.getContext("2d");
            const blueGrad = ctx.createLinearGradient(0, 0, 0, 280);
            blueGrad.addColorStop(0, "rgba(37, 99, 235, 0.88)");
            blueGrad.addColorStop(1, "rgba(37, 99, 235, 0.25)");
            const cyanFill = ctx.createLinearGradient(0, 0, 0, 280);
            cyanFill.addColorStop(0, "rgba(0, 229, 255, 0.22)");
            cyanFill.addColorStop(1, "rgba(0, 229, 255, 0.0)");

            const info = buildRevenueDataset("12m");

            revenueChartInstance = new Chart(ctx, {
                data: {
                    labels: info.labels,
                    datasets: [
                        {
                            type: "line",
                            label: "รายรับสุทธิ (Net Income)",
                            data: info.netData,
                            borderColor: "#00E5FF",
                            backgroundColor: cyanFill,
                            borderWidth: 3,
                            pointBackgroundColor: "#070D1E",
                            pointBorderColor: "#00E5FF",
                            pointBorderWidth: 2,
                            pointRadius: 4,
                            pointHoverRadius: 6,
                            tension: 0.35,
                            fill: true,
                            order: 1
                        },
                        {
                            type: "bar",
                            label: "รายรับรวม (Gross Income)",
                            data: info.grossData,
                            backgroundColor: blueGrad,
                            borderColor: "#2563EB",
                            borderWidth: 1,
                            borderRadius: 8,
                            maxBarThickness: 38,
                            order: 2
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: {
                            position: "top",
                            align: "end",
                            labels: { usePointStyle: true, boxWidth: 8, font: { size: 12, weight: "500" } }
                        },
                        tooltip: {
                            backgroundColor: "#070D1E",
                            titleColor: "#00E5FF",
                            bodyColor: "#FFFFFF",
                            borderColor: "rgba(0, 229, 255, 0.3)",
                            borderWidth: 1,
                            padding: 12,
                            callbacks: {
                                label: (c) => ` ${c.dataset.label}: ${formatTHB(c.parsed.y)}`
                            }
                        }
                    },
                    scales: {
                        x: { grid: { display: false }, ticks: { font: { size: 11 } } },
                        y: {
                            beginAtZero: true,
                            grid: { color: "#F1F5F9" },
                            ticks: {
                                font: { size: 11 },
                                callback: (v) => "฿" + Number(v).toLocaleString("th-TH")
                            }
                        }
                    }
                }
            });
        }

        const updateRevenueView = (periodKey) => {
            const info = buildRevenueDataset(periodKey);
            const labelEl = document.getElementById("chartPeriodLabel");
            const totalEl = document.getElementById("chartPeriodNetTotal");
            const noteEl = document.getElementById("chartDataSourceNote");

            if (labelEl) labelEl.textContent = info.periodTitle;
            if (totalEl) totalEl.textContent = formatTHB(info.totalNet);
            if (noteEl) noteEl.textContent = info.sourceNote;

            if (revenueChartInstance) {
                revenueChartInstance.data.labels = info.labels;
                revenueChartInstance.data.datasets[0].data = info.netData;
                revenueChartInstance.data.datasets[1].data = info.grossData;
                revenueChartInstance.update();
            }
        };

        const periodButtons = document.querySelectorAll(".fin-period-btn");
        periodButtons.forEach(btn => {
            btn.addEventListener("click", function () {
                periodButtons.forEach(b => b.classList.remove("active"));
                this.classList.add("active");
                updateRevenueView(this.getAttribute("data-period"));
            });
        });

        // Payment Status Donut
        if (donutCanvas && typeof Chart !== "undefined") {
            const counts = dashboardData.statusCounts || { pending: 0, approved: 0, rejected: 0 };
            const amounts = dashboardData.statusAmounts || { pending: 0, approved: 0, rejected: 0 };
            const totalCount = (counts.pending || 0) + (counts.approved || 0) + (counts.rejected || 0);

            const values = totalCount > 0 ? [counts.pending, counts.approved, counts.rejected] : [1, 1, 1];
            const colors = totalCount > 0
                ? ["#F59E0B", "#16A34A", "#DC2626"]   // Pending=Orange, Approved=Green, Rejected=Red
                : ["#E2E8F0", "#E2E8F0", "#E2E8F0"];

            new Chart(donutCanvas.getContext("2d"), {
                type: "doughnut",
                data: {
                    labels: ["Pending (รอตรวจสอบ)", "Approved (อนุมัติแล้ว)", "Rejected (ปฏิเสธ)"],
                    datasets: [{ data: values, backgroundColor: colors, borderColor: "#FFFFFF", borderWidth: 3, hoverOffset: 6 }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: "72%",
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            enabled: totalCount > 0,
                            backgroundColor: "#070D1E",
                            titleColor: "#00E5FF",
                            bodyColor: "#FFFFFF",
                            padding: 12,
                            callbacks: {
                                label: (ctx) => {
                                    const countVal = ctx.raw;
                                    const amtList = [amounts.pending, amounts.approved, amounts.rejected];
                                    const pct = totalCount > 0 ? ((countVal / totalCount) * 100).toFixed(1) : 0;
                                    return [
                                        ` จำนวน: ${countVal} รายการ (${pct}%)`,
                                        ` มูลค่ารวม: ${formatTHB(amtList[ctx.dataIndex])}`
                                    ];
                                }
                            }
                        }
                    }
                }
            });
        }
    }

    // =====================================================================
    // 2. PAYMENT APPROVAL (Payment Slip Approval)
    // =====================================================================
    function initPaymentApproval() {
        const appContainer = document.getElementById("paymentApprovalApp");
        if (!appContainer) return;

        const apiUrls = {
            list: appContainer.getAttribute("data-api-list") || "/Finance/GetPaymentSlipsJson",
            detail: appContainer.getAttribute("data-api-detail") || "/Finance/GetPaymentSlipDetailJson",
            approve: appContainer.getAttribute("data-api-approve") || "/Finance/ApproveAjax",
            reject: appContainer.getAttribute("data-api-reject") || "/Finance/RejectAjax"
        };

        const state = {
            status: appContainer.getAttribute("data-initial-status") || "pending",
            keyword: appContainer.getAttribute("data-initial-keyword") || "",
            fromDate: "",
            toDate: "",
            sortOrder: "fifo",
            page: 1,
            pageSize: 10,
            isSubmitting: false,
            currentDetailZoom: 1,
            currentLightboxZoom: 1
        };

        const $ = (id) => document.getElementById(id);

        const elSummaryPending = $("paSummaryPending");
        const elSummaryApproved = $("paSummaryApproved");
        const elSummaryRejected = $("paSummaryRejected");
        const elSummaryTotal = $("paSummaryTotal");
        const elTotalBadge = $("paTotalRecordsBadge");

        const elTableBody = $("paTableBody");
        const elTableContainer = $("paTableContainer");
        const elLoadingState = $("paLoadingState");
        const elEmptyState = $("paEmptyState");
        const elPaginationBar = $("paPaginationBar");
        const elPaginationInfo = $("paPaginationInfo");
        const elPaginationControls = $("paPaginationControls");

        const elKeywordInput = $("paKeywordInput");
        const elFromDateInput = $("paFromDateInput");
        const elToDateInput = $("paToDateInput");
        const elSortSelect = $("paSortSelect");
        const elPageSizeSelect = $("paPageSizeSelect");

        const detailModalEl = $("paDetailModal");
        const previewModalEl = $("paSlipPreviewModal");
        const rejectModalEl = $("paRejectModal");

        const bsDetailModal = detailModalEl ? new bootstrap.Modal(detailModalEl) : null;
        const bsPreviewModal = previewModalEl ? new bootstrap.Modal(previewModalEl) : null;
        const bsRejectModal = rejectModalEl ? new bootstrap.Modal(rejectModalEl) : null;

        const getCsrfToken = () => {
            const tokenInput = document.querySelector('#paymentApprovalCsrfForm input[name="__RequestVerificationToken"]');
            return tokenInput ? tokenInput.value : "";
        };

        const formatMethodLabel = (method) => {
            const m = (method || "").toLowerCase();
            if (m === "promptpay") return '<i class="fa-solid fa-qrcode text-primary me-1"></i>PromptPay QR';
            if (m === "bank_transfer") return '<i class="fa-solid fa-building-columns text-info me-1"></i>Bank Transfer';
            if (m === "credit_card") return '<i class="fa-regular fa-credit-card text-violet me-1"></i>Credit Card';
            if (m === "cash") return '<i class="fa-solid fa-money-bill-wave text-success me-1"></i>Cash';
            return escapeHtml(method || "N/A");
        };

        const renderStatusBadge = (status) => {
            const s = (status || "pending").toLowerCase();
            if (s === "approved") return '<span class="fin-status-badge status-approved"><i class="fa-solid fa-circle-check me-1"></i>Approved</span>';
            if (s === "rejected") return '<span class="fin-status-badge status-rejected"><i class="fa-solid fa-circle-xmark me-1"></i>Rejected</span>';
            return '<span class="fin-status-badge status-pending"><i class="fa-solid fa-clock me-1"></i>Pending</span>';
        };

        const syncActiveFilterUI = () => {
            document.querySelectorAll(".payment-approval-tab").forEach(tab => {
                tab.classList.toggle("active", tab.getAttribute("data-status") === state.status);
            });
            document.querySelectorAll(".payment-approval-summary-card").forEach(card => {
                card.classList.toggle("active-card", card.getAttribute("data-filter-trigger") === state.status);
            });
        };

        // ---- 1. โหลดรายการ ----
        const loadPaymentSlips = async () => {
            syncActiveFilterUI();

            elLoadingState.classList.remove("d-none");
            elTableContainer.classList.add("d-none");
            elEmptyState.classList.add("d-none");
            elPaginationBar.classList.add("d-none");

            const params = new URLSearchParams({
                status: state.status,
                keyword: state.keyword,
                fromDate: state.fromDate,
                toDate: state.toDate,
                sortOrder: state.sortOrder,
                page: state.page,
                pageSize: state.pageSize
            });

            try {
                const res = await fetch(`${apiUrls.list}?${params.toString()}`, { headers: { "Accept": "application/json" } });
                if (!res.ok) throw new Error(`HTTP Error ${res.status}`);
                const data = await res.json();

                if (data.summary) {
                    const fmt = (n) => Number(n || 0).toLocaleString("th-TH");
                    if (elSummaryPending) elSummaryPending.textContent = fmt(data.summary.pending);
                    if (elSummaryApproved) elSummaryApproved.textContent = fmt(data.summary.approved);
                    if (elSummaryRejected) elSummaryRejected.textContent = fmt(data.summary.rejected);
                    if (elSummaryTotal) elSummaryTotal.textContent = fmt(data.summary.total);
                }

                const items = data.items || [];
                const pg = data.pagination || { page: 1, pageSize: state.pageSize, totalCount: 0, totalPages: 0 };

                if (elTotalBadge) elTotalBadge.textContent = Number(pg.totalCount || 0).toLocaleString("th-TH");
                elLoadingState.classList.add("d-none");

                if (items.length === 0) {
                    elEmptyState.classList.remove("d-none");
                    return;
                }

                renderTableRows(items, pg.page, pg.pageSize);
                renderPagination(pg);

                elTableContainer.classList.remove("d-none");
                elPaginationBar.classList.remove("d-none");
            } catch (err) {
                console.error("Error loading payment slips:", err);
                elLoadingState.classList.add("d-none");
                elEmptyState.classList.remove("d-none");
                if (typeof Swal !== "undefined") {
                    Swal.fire({
                        icon: "error",
                        title: "เกิดข้อผิดพลาดในการโหลดข้อมูล",
                        text: "ไม่สามารถดึงรายการสลิปการเงินได้ กรุณาลองใหม่อีกครั้ง",
                        confirmButtonColor: "#2563EB"
                    });
                }
            }
        };

        const renderTableRows = (items, currentPage, pageSize) => {
            const startNo = (currentPage - 1) * pageSize;

            elTableBody.innerHTML = items.map((item, idx) => {
                const rowNo = startNo + idx + 1;
                const statusLower = (item.status || "pending").toLowerCase();
                const isPending = statusLower === "pending";
                const amountMismatch = Number(item.amount) !== Number(item.bookingTotal);

                let slipCellHtml = `<span class="payment-approval-no-thumb"><i class="fa-regular fa-image me-1"></i>ไม่พบรูปภาพ</span>`;
                if (item.slipUrl) {
                    const safeUrl = escapeHtml(item.slipUrl);
                    slipCellHtml = `
                        <div class="payment-approval-thumb-wrap js-open-lightbox"
                             data-slip-url="${safeUrl}"
                             data-txn-id="${escapeHtml(item.paymentId)}"
                             title="คลิกเพื่อดูรูปสลิปขนาดใหญ่">
                            <img src="${safeUrl}"
                                 alt="Slip #${escapeHtml(item.paymentId)}"
                                 class="payment-approval-thumb-img"
                                 onerror="this.outerHTML='<span class=\\'payment-approval-no-thumb\\'>ไม่พบรูปภาพ</span>'" />
                            <span class="payment-approval-thumb-zoom"><i class="fa-solid fa-magnifying-glass-plus"></i></span>
                        </div>`;
                }

                let reviewerHtml = `<span class="text-muted extra-small">รอการตรวจสอบ</span>`;
                if (!isPending) {
                    reviewerHtml = `<div class="fw-semibold text-dark small">${escapeHtml(item.reviewedByName || "Admin / Finance")}</div>`;
                    if (statusLower === "rejected" && item.rejectReason) {
                        reviewerHtml += `<div class="text-danger extra-small mt-1"><i class="fa-solid fa-comment-dots me-1"></i>${escapeHtml(item.rejectReason)}</div>`;
                    }
                }

                let actionButtonsHtml = `
                    <div class="d-inline-flex align-items-center justify-content-end gap-1 flex-wrap">
                        <button type="button" class="btn fin-btn-view js-btn-view-detail"
                                data-id="${escapeHtml(item.paymentId)}" title="ดูรายละเอียดสลิป">
                            <i class="fa-regular fa-eye me-1"></i>View Details
                        </button>`;

                if (isPending) {
                    actionButtonsHtml += `
                        <button type="button" class="btn payment-approval-btn-approve js-btn-approve"
                                data-id="${escapeHtml(item.paymentId)}"
                                data-booking="${escapeHtml(item.bookingCode)}"
                                data-amount="${escapeHtml(item.amount)}"
                                title="อนุมัติสลิป">
                            <i class="fa-solid fa-check me-1"></i>Approve
                        </button>
                        <button type="button" class="btn payment-approval-btn-reject js-btn-reject"
                                data-id="${escapeHtml(item.paymentId)}"
                                data-booking="${escapeHtml(item.bookingCode)}"
                                data-customer="${escapeHtml(item.contactName)}"
                                data-amount="${escapeHtml(item.amount)}"
                                title="ปฏิเสธสลิป">
                            <i class="fa-solid fa-xmark me-1"></i>Reject
                        </button>`;
                }
                actionButtonsHtml += `</div>`;

                return `
                    <tr>
                        <td class="text-center text-muted fw-semibold">${rowNo}</td>
                        <td><span class="fw-bold text-dark">#${escapeHtml(item.paymentId)}</span></td>
                        <td>
                            <span class="fin-booking-code">${escapeHtml(item.bookingCode)}</span>
                            <span class="d-block text-muted extra-small mt-1">ID: #${escapeHtml(item.bookingId)}</span>
                        </td>
                        <td>
                            <div class="fw-semibold text-dark">${escapeHtml(item.contactName)}</div>
                            <div class="text-muted extra-small"><i class="fa-regular fa-envelope me-1"></i>${escapeHtml(item.contactEmail || "-")}</div>
                        </td>
                        <td>
                            <span class="text-dark fw-medium">${escapeHtml(item.submittedAt)}</span>
                            <span class="d-block extra-small text-muted">${formatMethodLabel(item.method)}</span>
                        </td>
                        <td class="text-end">
                            <span class="fin-amount-cell">${formatTHB(item.amount)}</span>
                            ${amountMismatch ? `<span class="d-block extra-small text-warning-custom"><i class="fa-solid fa-triangle-exclamation me-1"></i>ยอดจอง ${formatTHB(item.bookingTotal)}</span>` : ""}
                        </td>
                        <td class="text-center">${slipCellHtml}</td>
                        <td class="text-center">${renderStatusBadge(item.status)}</td>
                        <td><span class="small text-dark">${escapeHtml(item.reviewedAt || "-")}</span></td>
                        <td>${reviewerHtml}</td>
                        <td class="text-end">${actionButtonsHtml}</td>
                    </tr>`;
            }).join("");
        };

        const renderPagination = (pg) => {
            elPaginationInfo.textContent = `แสดงหน้า ${pg.page} จาก ${pg.totalPages || 1} (ทั้งหมด ${pg.totalCount} รายการ)`;
            elPaginationControls.innerHTML = (!pg.totalPages || pg.totalPages <= 1) ? "" : renderPager(pg);
        };

        // ---- 2. Modal รายละเอียด ----
        const openDetailModal = async (paymentId) => {
            if (!bsDetailModal) return;

            $("paModalHeaderTxnId").textContent = paymentId;
            $("paDetailModalLoading").classList.remove("d-none");
            $("paDetailModalContent").classList.add("d-none");
            $("paModalBtnApprove").classList.add("d-none");
            $("paModalBtnReject").classList.add("d-none");
            $("paModalFullPageLink").setAttribute("href", `/Finance/PaymentDetail/${encodeURIComponent(paymentId)}`);

            bsDetailModal.show();

            try {
                const res = await fetch(`${apiUrls.detail}?id=${encodeURIComponent(paymentId)}`, { headers: { "Accept": "application/json" } });
                const json = await res.json();
                if (!res.ok || !json.success) throw new Error(json.message || "ไม่สามารถโหลดรายละเอียดสลิปได้");

                const d = json.data;
                const statusLower = (d.status || "").toLowerCase();

                $("paDetUserName").textContent = d.contactName || "-";
                $("paDetUserId").textContent = d.userId ? `#${d.userId}` : "Guest / Walk-in";
                $("paDetUserEmail").textContent = d.contactEmail || "-";
                $("paDetUserPhone").textContent = d.contactPhone || "-";

                $("paDetTxnId").textContent = `#${d.paymentId}`;
                $("paDetBookingCode").textContent = `${d.bookingCode} (#${d.bookingId})`;
                $("paDetStatusBadge").innerHTML = renderStatusBadge(d.status);
                $("paDetSubmittedAt").textContent = d.submittedAt || "-";
                $("paDetMethod").innerHTML = formatMethodLabel(d.method);
                $("paDetStayDates").textContent = `${d.checkInDate} - ${d.checkOutDate} (${d.nights} คืน)`;

                $("paDetAmountDue").textContent = formatTHB(d.amountDue);
                $("paDetAmountPaid").textContent = formatTHB(d.amountPaid);

                const matchEl = $("paDetMatchStatus");
                if (d.amountMatches) {
                    matchEl.className = "fw-bold small text-success";
                    matchEl.innerHTML = '<i class="fa-solid fa-circle-check me-1"></i>ยอดชำระตรงตามใบจอง';
                } else {
                    const diffSign = Number(d.difference) > 0 ? "+" : "";
                    matchEl.className = "fw-bold small text-danger";
                    matchEl.innerHTML = `<i class="fa-solid fa-triangle-exclamation me-1"></i>ยอดต่าง ${diffSign}${formatTHB(d.difference)}`;
                }

                const blockAlert = $("paModalBlockAlert");
                const blockText = $("paModalBlockReasonText");
                if (d.blockReason && statusLower === "pending") {
                    blockText.textContent = d.blockReason;
                    blockAlert.classList.remove("d-none");
                } else {
                    blockAlert.classList.add("d-none");
                }

                const auditBox = $("paDetReviewAuditBox");
                const rejectRow = $("paDetRejectReasonRow");
                if (statusLower !== "pending") {
                    auditBox.classList.remove("d-none");
                    $("paDetReviewedBy").textContent = d.reviewedByName || "Admin / Finance";
                    $("paDetReviewedAt").textContent = d.reviewedAt || "-";
                    if (statusLower === "rejected" && d.rejectReason) {
                        rejectRow.classList.remove("d-none");
                        $("paDetRejectReason").textContent = d.rejectReason;
                    } else {
                        rejectRow.classList.add("d-none");
                    }
                } else {
                    auditBox.classList.add("d-none");
                }

                const slipImg = $("paDetSlipImg");
                const slipMissing = $("paDetSlipMissing");
                state.currentDetailZoom = 1;
                slipImg.style.transform = "scale(1)";

                if (d.slipUrl) {
                    slipImg.src = d.slipUrl;
                    slipImg.classList.remove("d-none");
                    slipMissing.classList.add("d-none");
                    slipImg.onerror = () => {
                        slipImg.classList.add("d-none");
                        slipMissing.classList.remove("d-none");
                    };
                } else {
                    slipImg.removeAttribute("src");
                    slipImg.classList.add("d-none");
                    slipMissing.classList.remove("d-none");
                }

                const btnApprove = $("paModalBtnApprove");
                const btnReject = $("paModalBtnReject");

                if (statusLower === "pending") {
                    btnApprove.classList.remove("d-none");
                    btnReject.classList.remove("d-none");
                    btnApprove.disabled = !d.canApprove;
                    btnReject.disabled = !d.canReject;

                    btnApprove.onclick = () => confirmAndApproveSlip(d.paymentId, d.bookingCode, d.amountPaid, true);
                    btnReject.onclick = () => {
                        bsDetailModal.hide();
                        openRejectModal(d.paymentId, d.bookingCode, d.contactName, d.amountPaid);
                    };
                }

                $("paDetailModalLoading").classList.add("d-none");
                $("paDetailModalContent").classList.remove("d-none");
            } catch (err) {
                bsDetailModal.hide();
                Swal.fire({
                    icon: "error",
                    title: "ไม่พบข้อมูล",
                    text: err.message || "เกิดข้อผิดพลาดในการโหลดรายละเอียด",
                    confirmButtonColor: "#2563EB"
                });
            }
        };

        // ---- 3. Approve ----
        const confirmAndApproveSlip = async (paymentId, bookingCode, amount, fromDetailModal = false) => {
            if (state.isSubmitting) return;

            const confirmResult = await Swal.fire({
                icon: "question",
                title: "ยืนยันการอนุมัติสลิปการเงิน?",
                html: `
                    <div class="text-start small p-3 rounded bg-light border mt-2">
                        <div class="d-flex justify-content-between mb-1">
                            <span class="text-muted">Transaction ID:</span>
                            <strong class="text-dark">#${escapeHtml(paymentId)}</strong>
                        </div>
                        <div class="d-flex justify-content-between mb-1">
                            <span class="text-muted">Booking Code:</span>
                            <strong class="text-primary">${escapeHtml(bookingCode)}</strong>
                        </div>
                        <div class="d-flex justify-content-between">
                            <span class="text-muted">จำนวนเงินที่อนุมัติ:</span>
                            <strong class="text-success fs-6">${formatTHB(amount)}</strong>
                        </div>
                    </div>
                    <p class="small text-muted mt-3 mb-0">
                        เมื่ออนุมัติแล้ว ระบบจะเปลี่ยนสถานะการจองเป็น <strong>Confirmed</strong> บันทึกรายรับ และออกใบเสร็จรับเงินทันที
                    </p>`,
                showCancelButton: true,
                confirmButtonColor: "#16A34A",
                cancelButtonColor: "#64748B",
                confirmButtonText: '<i class="fa-solid fa-check me-1"></i> ยืนยันอนุมัติ',
                cancelButtonText: "ยกเลิก"
            });

            if (!confirmResult.isConfirmed) return;

            state.isSubmitting = true;
            Swal.fire({
                title: "กำลังบันทึกการอนุมัติ...",
                text: "กรุณารอสักครู่ ระบบกำลังออกใบเสร็จและอัปเดตสถานะ",
                allowOutsideClick: false,
                didOpen: () => Swal.showLoading()
            });

            try {
                const formData = new URLSearchParams();
                formData.append("id", paymentId);
                formData.append("__RequestVerificationToken", getCsrfToken());

                const res = await fetch(apiUrls.approve, {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json" },
                    body: formData.toString()
                });

                const result = await res.json();
                if (!res.ok || !result.success) throw new Error(result.message || "ไม่สามารถอนุมัติสลิปได้");

                if (fromDetailModal && bsDetailModal) bsDetailModal.hide();

                await Swal.fire({
                    icon: "success",
                    title: "อนุมัติสลิปสำเร็จ!",
                    text: result.message || "บันทึกสถานะ Approved และออกใบเสร็จเรียบร้อยแล้ว",
                    confirmButtonColor: "#16A34A"
                });

                await loadPaymentSlips();
            } catch (err) {
                Swal.fire({
                    icon: "error",
                    title: "ไม่สามารถอนุมัติสลิปได้",
                    text: err.message || "เกิดข้อผิดพลาดในการเชื่อมต่อเซิร์ฟเวอร์",
                    confirmButtonColor: "#DC2626"
                });
            } finally {
                state.isSubmitting = false;
            }
        };

        // ---- 4. Reject ----
        const openRejectModal = (paymentId, bookingCode, customerName, amount) => {
            if (!bsRejectModal) return;

            $("paRejectPaymentId").value = paymentId;
            $("paRejectTxnDisplay").textContent = `#${paymentId}`;
            $("paRejectCustomerDisplay").textContent = `${customerName} (${bookingCode})`;
            $("paRejectAmountDisplay").textContent = formatTHB(amount);

            $("paRejectReasonPreset").value = "";
            const reasonTextarea = $("paRejectReasonText");
            reasonTextarea.value = "";
            reasonTextarea.classList.remove("is-invalid");

            bsRejectModal.show();
        };

        const elReasonPreset = $("paRejectReasonPreset");
        const elReasonText = $("paRejectReasonText");
        if (elReasonPreset && elReasonText) {
            elReasonPreset.addEventListener("change", function () {
                if (this.value && this.value !== "OTHER") {
                    elReasonText.value = this.value;
                    elReasonText.classList.remove("is-invalid");
                } else if (this.value === "OTHER") {
                    elReasonText.value = "";
                    elReasonText.focus();
                }
            });
        }

        const elRejectForm = $("paRejectForm");
        if (elRejectForm) {
            elRejectForm.addEventListener("submit", async function (e) {
                e.preventDefault();
                if (state.isSubmitting) return;

                const paymentId = $("paRejectPaymentId").value;
                const reason = (elReasonText.value || "").trim();

                if (!reason) {
                    elReasonText.classList.add("is-invalid");
                    return;
                }
                elReasonText.classList.remove("is-invalid");

                const btnSubmit = $("paBtnConfirmRejectSubmit");
                state.isSubmitting = true;
                if (btnSubmit) btnSubmit.disabled = true;

                try {
                    const formData = new URLSearchParams();
                    formData.append("PaymentId", paymentId);
                    formData.append("Reason", reason);
                    formData.append("__RequestVerificationToken", getCsrfToken());

                    const res = await fetch(apiUrls.reject, {
                        method: "POST",
                        headers: { "Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json" },
                        body: formData.toString()
                    });

                    const result = await res.json();
                    if (!res.ok || !result.success) throw new Error(result.message || "ไม่สามารถปฏิเสธสลิปได้");

                    if (bsRejectModal) bsRejectModal.hide();

                    await Swal.fire({
                        icon: "success",
                        title: "ปฏิเสธสลิปเรียบร้อยแล้ว",
                        text: result.message || "บันทึกสถานะ Rejected และเหตุผลเรียบร้อยแล้ว",
                        confirmButtonColor: "#2563EB"
                    });

                    await loadPaymentSlips();
                } catch (err) {
                    Swal.fire({
                        icon: "error",
                        title: "ไม่สามารถปฏิเสธรายการได้",
                        text: err.message || "เกิดข้อผิดพลาดในการส่งข้อมูล",
                        confirmButtonColor: "#DC2626"
                    });
                } finally {
                    state.isSubmitting = false;
                    if (btnSubmit) btnSubmit.disabled = false;
                }
            });
        }

        // ---- 5. Lightbox / Zoom ----
        const openSlipLightbox = (slipUrl, txnId) => {
            if (!bsPreviewModal) return;

            const imgEl = $("paLightboxImg");
            const noImgEl = $("paLightboxNoImg");
            const titleEl = $("paPreviewModalTitle");

            state.currentLightboxZoom = 1;
            imgEl.style.transform = "scale(1)";
            if (titleEl) titleEl.innerHTML = `<i class="fa-solid fa-receipt text-cyber me-2"></i>รูปภาพสลิปโอนเงิน #${escapeHtml(txnId || "")}`;

            if (slipUrl) {
                imgEl.src = slipUrl;
                imgEl.classList.remove("d-none");
                noImgEl.classList.add("d-none");
                imgEl.onerror = () => {
                    imgEl.classList.add("d-none");
                    noImgEl.classList.remove("d-none");
                };
            } else {
                imgEl.classList.add("d-none");
                noImgEl.classList.remove("d-none");
            }

            bsPreviewModal.show();
        };

        const applyDetailZoom = (delta, reset = false) => {
            const img = $("paDetSlipImg");
            if (!img) return;
            state.currentDetailZoom = reset ? 1 : Math.min(3, Math.max(0.5, state.currentDetailZoom + delta));
            img.style.transform = `scale(${state.currentDetailZoom})`;
        };

        $("paBtnModalZoomIn")?.addEventListener("click", () => applyDetailZoom(0.25));
        $("paBtnModalZoomOut")?.addEventListener("click", () => applyDetailZoom(-0.25));
        $("paBtnModalZoomReset")?.addEventListener("click", () => applyDetailZoom(0, true));
        $("paBtnModalFullscreen")?.addEventListener("click", () => {
            const stage = $("paModalSlipStage");
            if (stage && stage.requestFullscreen) stage.requestFullscreen();
        });
        $("paDetSlipImg")?.addEventListener("click", function () {
            if (this.getAttribute("src")) openSlipLightbox(this.src, $("paModalHeaderTxnId")?.textContent);
        });

        const applyLightboxZoom = (delta, reset = false) => {
            const img = $("paLightboxImg");
            if (!img) return;
            state.currentLightboxZoom = reset ? 1 : Math.min(3.5, Math.max(0.5, state.currentLightboxZoom + delta));
            img.style.transform = `scale(${state.currentLightboxZoom})`;
        };

        $("paLightboxZoomIn")?.addEventListener("click", () => applyLightboxZoom(0.25));
        $("paLightboxZoomOut")?.addEventListener("click", () => applyLightboxZoom(-0.25));
        $("paLightboxZoomReset")?.addEventListener("click", () => applyLightboxZoom(0, true));
        $("paLightboxFullscreen")?.addEventListener("click", () => {
            const container = $("paLightboxContainer");
            if (container && container.requestFullscreen) container.requestFullscreen();
        });

        // ---- 6. Event listeners ----
        elTableBody?.addEventListener("click", function (e) {
            const thumbBtn = e.target.closest(".js-open-lightbox");
            if (thumbBtn) {
                openSlipLightbox(thumbBtn.getAttribute("data-slip-url"), thumbBtn.getAttribute("data-txn-id"));
                return;
            }

            const viewBtn = e.target.closest(".js-btn-view-detail");
            if (viewBtn) {
                openDetailModal(viewBtn.getAttribute("data-id"));
                return;
            }

            const approveBtn = e.target.closest(".js-btn-approve");
            if (approveBtn) {
                confirmAndApproveSlip(
                    approveBtn.getAttribute("data-id"),
                    approveBtn.getAttribute("data-booking"),
                    approveBtn.getAttribute("data-amount"),
                    false
                );
                return;
            }

            const rejectBtn = e.target.closest(".js-btn-reject");
            if (rejectBtn) {
                openRejectModal(
                    rejectBtn.getAttribute("data-id"),
                    rejectBtn.getAttribute("data-booking"),
                    rejectBtn.getAttribute("data-customer"),
                    rejectBtn.getAttribute("data-amount")
                );
            }
        });

        document.querySelectorAll(".payment-approval-tab").forEach(tab => {
            tab.addEventListener("click", function () {
                state.status = this.getAttribute("data-status") || "pending";
                state.page = 1;
                loadPaymentSlips();
            });
        });

        document.querySelectorAll(".payment-approval-summary-card").forEach(card => {
            card.addEventListener("click", function () {
                state.status = this.getAttribute("data-filter-trigger") || "all";
                state.page = 1;
                loadPaymentSlips();
            });
        });

        $("paBtnSwitchHistory")?.addEventListener("click", function () {
            state.status = "history";
            state.sortOrder = "reviewed_desc";
            if (elSortSelect) elSortSelect.value = "reviewed_desc";
            state.page = 1;
            loadPaymentSlips();
        });

        $("paFilterForm")?.addEventListener("submit", function (e) {
            e.preventDefault();
            state.keyword = (elKeywordInput?.value || "").trim();
            state.fromDate = elFromDateInput?.value || "";
            state.toDate = elToDateInput?.value || "";
            state.sortOrder = elSortSelect?.value || "fifo";
            state.page = 1;
            loadPaymentSlips();
        });

        elSortSelect?.addEventListener("change", function () {
            state.sortOrder = this.value;
            state.page = 1;
            loadPaymentSlips();
        });

        elPageSizeSelect?.addEventListener("change", function () {
            state.pageSize = parseInt(this.value, 10) || 10;
            state.page = 1;
            loadPaymentSlips();
        });

        const resetAllFilters = () => {
            state.status = "all";
            state.keyword = "";
            state.fromDate = "";
            state.toDate = "";
            state.sortOrder = "fifo";
            state.page = 1;
            if (elKeywordInput) elKeywordInput.value = "";
            if (elFromDateInput) elFromDateInput.value = "";
            if (elToDateInput) elToDateInput.value = "";
            if (elSortSelect) elSortSelect.value = "fifo";
            loadPaymentSlips();
        };

        $("paBtnResetFilter")?.addEventListener("click", resetAllFilters);
        $("paEmptyResetBtn")?.addEventListener("click", resetAllFilters);

        $("paBtnReloadTable")?.addEventListener("click", function () {
            const icon = $("paReloadIcon");
            icon?.classList.add("fin-spin-anim");
            loadPaymentSlips().finally(() => setTimeout(() => icon?.classList.remove("fin-spin-anim"), 400));
        });

        elPaginationControls?.addEventListener("click", function (e) {
            e.preventDefault();
            const link = e.target.closest("a.page-link");
            if (!link || link.parentElement.classList.contains("disabled")) return;
            const targetPage = parseInt(link.getAttribute("data-page"), 10);
            if (targetPage && targetPage !== state.page) {
                state.page = targetPage;
                loadPaymentSlips();
            }
        });

        loadPaymentSlips();
    }

    // =====================================================================
    // 3. TRANSACTIONS HISTORY  (window.financeTransactions)
    // =====================================================================
    function initTransactions() {
        const appEl = document.getElementById("ftTransactionsApp");
        if (!appEl) return;

        const $ = (id) => document.getElementById(id);

        const actionUrl = appEl.getAttribute("data-action-url") || "/Finance/Transactions";
        const paymentDetailApi = appEl.getAttribute("data-payment-detail-api") || "/Finance/GetPaymentSlipDetailJson";

        const searchForm = $("ftSearchForm");
        const serverKeywordInput = $("ftServerKeyword");
        const serverTxnTypeInput = $("ftServerTxnType");

        const inputTxnId = $("ftInputTxnId");
        const inputBookingId = $("ftInputBookingId");
        const inputCustomer = $("ftInputCustomer");
        const selectType = $("ftSelectType");
        const selectStatus = $("ftSelectStatus");
        const inputFromDate = $("ftInputFromDate");
        const inputToDate = $("ftInputToDate");
        const selectSort = $("ftSelectSort");

        const tableWrapper = $("ftTableWrapper");
        const tableBody = $("ftTableBody");
        const emptyState = $("ftEmptyState");
        const loadingState = $("ftLoadingState");
        const visibleCountEl = $("ftVisibleCount");

        const detailModalEl = $("ftDetailModal");
        const bsDetailModal = detailModalEl ? new bootstrap.Modal(detailModalEl) : null;

        const renderModalStatusBadge = (statusKey, statusLabel) => {
            const s = (statusKey || "approved").toLowerCase();
            if (s === "pending") return `<span class="ft-badge status-pending"><i class="fa-solid fa-clock me-1"></i>${escapeHtml(statusLabel || "Pending")}</span>`;
            if (s === "rejected") return `<span class="ft-badge status-rejected"><i class="fa-solid fa-circle-xmark me-1"></i>${escapeHtml(statusLabel || "Rejected")}</span>`;
            if (s === "refunded") return `<span class="ft-badge status-refunded"><i class="fa-solid fa-rotate-left me-1"></i>${escapeHtml(statusLabel || "Refunded")}</span>`;
            return `<span class="ft-badge status-approved"><i class="fa-solid fa-circle-check me-1"></i>${escapeHtml(statusLabel || "Approved")}</span>`;
        };

        // ---- 1. กรอง/เรียงในหน้า (Client) ----
        const applyClientFiltersAndSort = () => {
            if (!tableBody) return;

            const rows = Array.from(tableBody.querySelectorAll("tr.ft-txn-row"));
            const txnQuery = (inputTxnId?.value || "").replace(/[^0-9]/g, "").trim();
            const bookingQuery = (inputBookingId?.value || "").toLowerCase().trim();
            const customerQuery = (inputCustomer?.value || "").toLowerCase().trim();
            const typeFilter = (selectType?.value || "").toLowerCase().trim();
            const statusFilter = (selectStatus?.value || "all").toLowerCase().trim();
            const fromDateVal = inputFromDate?.value || "";
            const toDateVal = inputToDate?.value || "";
            const sortMode = selectSort?.value || "date_desc";

            const visibleRows = [];

            rows.forEach(row => {
                const rTxnId = (row.getAttribute("data-txn-id") || "").toLowerCase();
                const rBookingId = (row.getAttribute("data-booking-id") || "").toLowerCase();
                const rBookingCode = (row.getAttribute("data-booking-code") || "").toLowerCase();
                const rCustomer = (row.getAttribute("data-customer") || "").toLowerCase();
                const rType = (row.getAttribute("data-type") || "").toLowerCase();
                const rStatus = (row.getAttribute("data-status") || "").toLowerCase();
                const rDate = row.getAttribute("data-date") || "";

                let match = true;
                if (txnQuery && !rTxnId.includes(txnQuery)) match = false;
                if (bookingQuery && !rBookingCode.includes(bookingQuery) && !rBookingId.includes(bookingQuery)) match = false;
                if (customerQuery && !rCustomer.includes(customerQuery)) match = false;
                if (typeFilter && !rType.includes(typeFilter)) match = false;
                if (statusFilter !== "all" && rStatus !== statusFilter) match = false;
                if (fromDateVal && rDate < fromDateVal) match = false;
                if (toDateVal && rDate > toDateVal) match = false;

                row.classList.toggle("d-none", !match);
                if (match) visibleRows.push(row);
            });

            visibleRows.sort((a, b) => {
                if (sortMode === "amount_desc" || sortMode === "amount_asc") {
                    const amtA = parseFloat(a.getAttribute("data-amount") || "0");
                    const amtB = parseFloat(b.getAttribute("data-amount") || "0");
                    return sortMode === "amount_desc" ? amtB - amtA : amtA - amtB;
                }
                const timeA = a.getAttribute("data-timestamp") || "";
                const timeB = b.getAttribute("data-timestamp") || "";
                return sortMode === "date_asc" ? timeA.localeCompare(timeB) : timeB.localeCompare(timeA);
            });

            visibleRows.forEach(row => tableBody.appendChild(row));

            if (visibleCountEl) visibleCountEl.textContent = visibleRows.length.toLocaleString("th-TH");

            tableWrapper?.classList.toggle("d-none", visibleRows.length === 0);
            emptyState?.classList.toggle("d-none", visibleRows.length !== 0);
        };

        [inputTxnId, inputBookingId, inputCustomer].forEach(el => el?.addEventListener("input", applyClientFiltersAndSort));
        [selectType, selectStatus, inputFromDate, inputToDate, selectSort].forEach(el => el?.addEventListener("change", applyClientFiltersAndSort));

        // ค้นหาจริงจากฐานข้อมูล (ส่งไป FinanceController.Transactions)
        searchForm?.addEventListener("submit", function () {
            if (loadingState && tableWrapper) {
                loadingState.classList.remove("d-none");
                tableWrapper.classList.add("d-none");
                emptyState?.classList.add("d-none");
            }

            const combinedKeyword =
                inputBookingId?.value?.trim() ||
                inputCustomer?.value?.trim() ||
                inputTxnId?.value?.replace(/[^0-9]/g, "")?.trim() ||
                "";
            if (serverKeywordInput) serverKeywordInput.value = combinedKeyword;

            const selectedType = selectType?.value || "";
            if (serverTxnTypeInput) serverTxnTypeInput.value = selectedType === "receipt" ? "income" : selectedType;
        });

        const resetFilters = () => {
            if (inputTxnId) inputTxnId.value = "";
            if (inputBookingId) inputBookingId.value = "";
            if (inputCustomer) inputCustomer.value = "";
            if (selectType) selectType.value = "";
            if (selectStatus) selectStatus.value = "all";
            if (inputFromDate) inputFromDate.value = "";
            if (inputToDate) inputToDate.value = "";
            if (selectSort) selectSort.value = "date_desc";

            if (window.location.search) window.location.href = actionUrl;
            else applyClientFiltersAndSort();
        };

        $("ftBtnReset")?.addEventListener("click", resetFilters);
        $("ftBtnEmptyReset")?.addEventListener("click", resetFilters);

        // ---- 2. Detail Modal ----
        const openTransactionDetail = async (rowEl) => {
            if (!bsDetailModal || !rowEl) return;

            const g = (k, d = "") => rowEl.getAttribute(k) || d;
            const txnId = g("data-txn-id", "-");
            const bookingCode = g("data-booking-code");
            const bookingId = g("data-booking-id");
            const paymentId = g("data-payment-id");
            const typeLabel = g("data-type-label", "-");
            const category = g("data-category", "-");
            const txnCode = `#TXN-${String(txnId).padStart(5, "0")}`;

            $("ftModalHeaderId").textContent = txnCode;
            $("ftModalTxnId").textContent = txnCode;
            $("ftModalAmount").textContent = formatTHB(g("data-amount", "0"));
            $("ftModalCategory").textContent = `หมวดหมู่บัญชี: ${category}`;
            $("ftModalStatusBadge").innerHTML = renderModalStatusBadge(g("data-status", "approved"), g("data-status-label", "Approved"));

            $("ftModalBookingCode").textContent = bookingCode
                ? `${bookingCode}${bookingId ? ` (Booking #${bookingId})` : ""}`
                : (bookingId ? `Booking #${bookingId}` : "ไม่มีรหัสการจองอ้างอิง");

            $("ftModalCustomerName").textContent = bookingCode ? `รายการของการจอง ${bookingCode}` : "รายการบันทึกบัญชีภายใน";
            $("ftModalType").textContent = `${typeLabel} (${category})`;
            $("ftModalMethod").textContent = g("data-method", "-");
            $("ftModalDateTime").textContent = g("data-datetime-display", "-");
            $("ftModalReviewer").textContent = g("data-recorded-by", "-");
            $("ftModalNote").textContent = g("data-description", "-");

            const paymentRefBox = $("ftModalPaymentRefBox");
            const paymentLink = $("ftModalPaymentLink");

            if (paymentId) {
                paymentRefBox?.classList.remove("d-none");
                $("ftModalPaymentId").textContent = `Payment #${paymentId}`;
                if (paymentLink) {
                    paymentLink.href = `/Finance/PaymentDetail/${encodeURIComponent(paymentId)}`;
                    paymentLink.classList.remove("d-none");
                }
            } else {
                paymentRefBox?.classList.add("d-none");
                paymentLink?.classList.add("d-none");
            }

            bsDetailModal.show();

            // เติมชื่อลูกค้า/ช่องทางชำระเงินจริงจาก API
            if (paymentId) {
                try {
                    const res = await fetch(`${paymentDetailApi}?id=${encodeURIComponent(paymentId)}`, { headers: { "Accept": "application/json" } });
                    if (res.ok) {
                        const json = await res.json();
                        if (json.success && json.data) {
                            const d = json.data;
                            if (d.contactName) {
                                $("ftModalCustomerName").textContent = `${d.contactName}${d.contactPhone ? ` (${d.contactPhone})` : ""}`;
                            }
                            if (d.method) $("ftModalMethod").textContent = d.method.toUpperCase();
                            if (d.reviewedByName) $("ftModalReviewer").textContent = d.reviewedByName;
                        }
                    }
                } catch (_) { /* ใช้ข้อมูลจากแถวตามเดิม */ }
            }
        };

        tableBody?.addEventListener("click", function (e) {
            const btn = e.target.closest(".ft-btn-view-detail");
            if (!btn) return;
            openTransactionDetail(btn.closest("tr.ft-txn-row"));
        });

        // ---- 3. Export CSV ----
        $("ftBtnExportCsv")?.addEventListener("click", () => {
            if (!tableBody) return;

            const visibleRows = Array.from(tableBody.querySelectorAll("tr.ft-txn-row:not(.d-none)"));
            if (visibleRows.length === 0) {
                alert("ไม่พบรายการธุรกรรมสำหรับส่งออก CSV");
                return;
            }

            const headers = [
                "Transaction ID", "Booking Code", "Category", "Transaction Type", "Payment Reference",
                "Amount (THB)", "Transaction Date", "Status", "Recorded By", "Description"
            ];

            const lines = [headers.map(csvEsc).join(",")];
            visibleRows.forEach(row => {
                lines.push([
                    `TXN-${String(row.getAttribute("data-txn-id") || "").padStart(5, "0")}`,
                    row.getAttribute("data-booking-code") || "-",
                    row.getAttribute("data-category") || "-",
                    row.getAttribute("data-type-label") || "-",
                    row.getAttribute("data-method") || "-",
                    Number(row.getAttribute("data-amount") || 0).toFixed(2),
                    row.getAttribute("data-datetime-display") || "-",
                    row.getAttribute("data-status-label") || "-",
                    row.getAttribute("data-recorded-by") || "-",
                    row.getAttribute("data-description") || "-"
                ].map(csvEsc).join(","));
            });

            downloadCsv(lines, `staymate-transactions-${todayStamp()}.csv`);
        });
    }

    // =====================================================================
    // 4. RECEIPT MANAGEMENT  (window.financeReceipts)
    // =====================================================================
    function initReceipts() {
        const appEl = document.getElementById("frReceiptsApp");
        if (!appEl) return;

        const $ = (id) => document.getElementById(id);

        const urls = {
            receipts: appEl.getAttribute("data-action-url") || "/Finance/Receipts",
            detailApi: appEl.getAttribute("data-detail-api") || "/Finance/GetReceiptDetailJson",
            createApi: appEl.getAttribute("data-create-api") || "/Finance/CreateReceiptAjax",
            cancelApi: appEl.getAttribute("data-cancel-api") || "/Finance/CancelReceiptAjax",
            pdfBase: appEl.getAttribute("data-pdf-base-url") || "/Finance/ReceiptPdf"
        };

        const unissuedPayments = readJsonScript("frUnissuedPaymentsData", []) || [];

        const filterForm = $("frFilterForm");
        const serverKeywordInput = $("frServerKeyword");
        const inputReceiptNo = $("frInputReceiptNo");
        const inputBookingCode = $("frInputBookingCode");
        const inputCustomerName = $("frInputCustomerName");
        const selectStatus = $("frSelectStatus");
        const inputFromDate = $("frInputFromDate");
        const inputToDate = $("frInputToDate");
        const selectSort = $("frSelectSort");

        const tableWrapper = $("frTableWrapper");
        const tableBody = $("frTableBody");
        const emptyState = $("frEmptyState");
        const visibleCountEl = $("frVisibleRowCount");

        const previewModalEl = $("frPreviewModal");
        const createModalEl = $("frCreateModal");
        const cancelModalEl = $("frCancelModal");

        const bsPreviewModal = previewModalEl ? new bootstrap.Modal(previewModalEl) : null;
        const bsCreateModal = createModalEl ? new bootstrap.Modal(createModalEl) : null;
        const bsCancelModal = cancelModalEl ? new bootstrap.Modal(cancelModalEl) : null;

        let isProcessing = false;

        const getCsrfToken = () => {
            const tokenEl = document.querySelector('#frCsrfForm input[name="__RequestVerificationToken"]');
            return tokenEl ? tokenEl.value : "";
        };

        // ---- 1. ค้นหา/กรอง/เรียง ----
        const applyFiltersAndSort = () => {
            if (!tableBody) return;

            const receiptQuery = (inputReceiptNo?.value || "").toLowerCase().trim();
            const bookingQuery = (inputBookingCode?.value || "").toLowerCase().trim();
            const customerQuery = (inputCustomerName?.value || "").toLowerCase().trim();
            const statusFilter = (selectStatus?.value || "all").toLowerCase().trim();
            const fromDateVal = inputFromDate?.value || "";
            const toDateVal = inputToDate?.value || "";
            const sortVal = selectSort?.value || "date_desc";

            const rows = Array.from(tableBody.querySelectorAll("tr.fr-receipt-row"));
            const visibleRows = [];

            rows.forEach(row => {
                const rNo = (row.getAttribute("data-receipt-no") || "").toLowerCase();
                const rBookingCode = (row.getAttribute("data-booking-code") || "").toLowerCase();
                const rBookingId = (row.getAttribute("data-booking-id") || "").toLowerCase();
                const rCustomer = (row.getAttribute("data-customer") || "").toLowerCase();
                const rStatus = (row.getAttribute("data-status") || "issued").toLowerCase();
                const rDate = row.getAttribute("data-date") || "";

                let match = true;
                if (receiptQuery && !rNo.includes(receiptQuery)) match = false;
                if (bookingQuery && !rBookingCode.includes(bookingQuery) && !rBookingId.includes(bookingQuery)) match = false;
                if (customerQuery && !rCustomer.includes(customerQuery)) match = false;
                if (statusFilter !== "all" && rStatus !== statusFilter) match = false;
                if (fromDateVal && rDate < fromDateVal) match = false;
                if (toDateVal && rDate > toDateVal) match = false;

                row.classList.toggle("d-none", !match);
                if (match) visibleRows.push(row);
            });

            visibleRows.sort((a, b) => {
                if (sortVal === "amount_desc" || sortVal === "amount_asc") {
                    const amtA = parseFloat(a.getAttribute("data-total") || "0");
                    const amtB = parseFloat(b.getAttribute("data-total") || "0");
                    return sortVal === "amount_desc" ? amtB - amtA : amtA - amtB;
                }
                const tA = a.getAttribute("data-timestamp") || "";
                const tB = b.getAttribute("data-timestamp") || "";
                return sortVal === "date_asc" ? tA.localeCompare(tB) : tB.localeCompare(tA);
            });

            visibleRows.forEach(r => tableBody.appendChild(r));

            if (visibleCountEl) visibleCountEl.textContent = visibleRows.length.toLocaleString("th-TH");
            tableWrapper?.classList.toggle("d-none", visibleRows.length === 0);
            emptyState?.classList.toggle("d-none", visibleRows.length !== 0);

            document.querySelectorAll(".fr-pill-btn").forEach(btn => {
                btn.classList.toggle("active", btn.getAttribute("data-status-pill") === statusFilter);
            });
        };

        [inputReceiptNo, inputBookingCode, inputCustomerName].forEach(el => el?.addEventListener("input", applyFiltersAndSort));
        [selectStatus, inputFromDate, inputToDate, selectSort].forEach(el => el?.addEventListener("change", applyFiltersAndSort));

        document.querySelectorAll(".fr-pill-btn").forEach(btn => {
            btn.addEventListener("click", function () {
                const status = this.getAttribute("data-status-pill") || "all";
                if (selectStatus) selectStatus.value = status;
                if (status === "pending" && unissuedPayments.length > 0) openCreateReceiptModal();
                else applyFiltersAndSort();
            });
        });

        document.querySelectorAll(".fr-summary-card[data-filter-status]").forEach(card => {
            card.addEventListener("click", function () {
                const status = this.getAttribute("data-filter-status") || "all";
                if (status === "pending") {
                    openCreateReceiptModal();
                    return;
                }
                if (selectStatus) selectStatus.value = status;
                applyFiltersAndSort();
            });
        });

        filterForm?.addEventListener("submit", function () {
            const combined =
                inputReceiptNo?.value?.trim() ||
                inputBookingCode?.value?.trim() ||
                inputCustomerName?.value?.trim() ||
                "";
            if (serverKeywordInput) serverKeywordInput.value = combined;
        });

        const resetAllFilters = () => {
            if (inputReceiptNo) inputReceiptNo.value = "";
            if (inputBookingCode) inputBookingCode.value = "";
            if (inputCustomerName) inputCustomerName.value = "";
            if (selectStatus) selectStatus.value = "all";
            if (inputFromDate) inputFromDate.value = "";
            if (inputToDate) inputToDate.value = "";
            if (selectSort) selectSort.value = "date_desc";

            if (window.location.search) window.location.href = urls.receipts;
            else applyFiltersAndSort();
        };

        $("frBtnResetFilters")?.addEventListener("click", resetAllFilters);
        $("frBtnEmptyReset")?.addEventListener("click", resetAllFilters);

        // ---- 2. Preview & Print ----
        const openReceiptPreview = async (receiptId, autoPrint = false) => {
            if (!bsPreviewModal) return;

            const loadingEl = $("frPreviewLoading");
            const paperEl = $("frPrintableDocument");

            loadingEl?.classList.remove("d-none");
            paperEl?.classList.add("d-none");
            bsPreviewModal.show();

            try {
                const res = await fetch(`${urls.detailApi}?id=${encodeURIComponent(receiptId)}`, { headers: { "Accept": "application/json" } });
                if (!res.ok) throw new Error("ไม่สามารถดึงข้อมูลใบเสร็จได้");
                const json = await res.json();
                if (!json.success || !json.data) throw new Error(json.message || "ไม่พบข้อมูลใบเสร็จ");

                const d = json.data;
                const isCancelled = (d.status || "").toLowerCase() === "cancelled";

                $("frPrevHeaderNo").textContent = d.receiptNo;
                $("frDocReceiptNo").textContent = d.receiptNo;
                $("frDocIssuedDate").textContent = d.issuedAt;
                $("frDocCustomerName").textContent = d.contactName || "-";
                $("frDocCustomerContact").textContent = `โทร: ${d.contactPhone || "-"} · อีเมล: ${d.contactEmail || "-"}`;
                $("frDocBookingCode").textContent = `${d.bookingCode} (#${d.bookingId})`;
                $("frDocStayPeriod").textContent = `${d.checkInDate} - ${d.checkOutDate} (${d.nights} คืน)`;
                $("frDocPayMethod").textContent = `${(d.method || "N/A").toUpperCase()} (ชำระเมื่อ ${d.paidAt})`;
                $("frDocIssuedBy").textContent = d.issuedByName || "Finance Officer";

                const watermarkEl = $("frCancelledWatermark");
                const statusBadgeEl = $("frDocStatusBadge");
                if (isCancelled) {
                    watermarkEl?.classList.remove("d-none");
                    statusBadgeEl.innerHTML = '<span class="fr-badge status-cancelled">Cancelled (ยกเลิกแล้ว)</span>';
                } else {
                    watermarkEl?.classList.add("d-none");
                    statusBadgeEl.innerHTML = '<span class="fr-badge status-issued">Issued / Paid (ชำระเงินแล้ว)</span>';
                }

                const roomsBody = $("frDocRoomsBody");
                if (d.rooms && d.rooms.length > 0) {
                    roomsBody.innerHTML = d.rooms.map(r => `
                        <tr>
                            <td class="fw-medium">${escapeHtml(r.roomTypeName)}</td>
                            <td class="text-center">${escapeHtml(r.roomNumber || "-")}</td>
                            <td class="text-end">${formatTHB(r.pricePerNight)}</td>
                            <td class="text-center">${escapeHtml(r.nights)}</td>
                            <td class="text-end">${formatTHB(r.extraBedAmount)}</td>
                            <td class="text-end fw-semibold">${formatTHB(r.lineTotal)}</td>
                        </tr>`).join("");
                } else {
                    roomsBody.innerHTML = `
                        <tr>
                            <td colspan="5">ค่าบริการห้องพักตามรหัสการจอง ${escapeHtml(d.bookingCode)}</td>
                            <td class="text-end fw-semibold">${formatTHB(d.amount)}</td>
                        </tr>`;
                }

                const promoBox = $("frDocPromotionsBox");
                const promoList = $("frDocPromotionsList");
                if (d.promotions && d.promotions.length > 0) {
                    promoBox?.classList.remove("d-none");
                    promoList.innerHTML = d.promotions.map(p =>
                        `<div>• [${escapeHtml(p.promoCode)}] ${escapeHtml(p.promoName)} (-${formatTHB(p.appliedAmount)})</div>`
                    ).join("");
                } else {
                    promoBox?.classList.add("d-none");
                }

                $("frDocRoomAmount").textContent = formatTHB(d.roomAmount);
                $("frDocDiscountAmount").textContent = `-${formatTHB(d.discountAmount)}`;
                $("frDocSubtotal").textContent = formatTHB(d.subtotal);
                $("frDocTaxAmount").textContent = formatTHB(d.taxAmount);
                $("frDocTotalPaid").textContent = formatTHB(d.amount);

                $("frModalFullDetailLink").setAttribute("href", `/Finance/ReceiptDetail/${encodeURIComponent(d.receiptId)}`);
                $("frModalBtnDownloadPdf").setAttribute("href", d.pdfUrl || `${urls.pdfBase}/${encodeURIComponent(d.receiptId)}`);

                loadingEl?.classList.add("d-none");
                paperEl?.classList.remove("d-none");

                if (autoPrint) setTimeout(() => printReceiptPaper(), 350);
            } catch (err) {
                bsPreviewModal.hide();
                window.location.href = `/Finance/ReceiptDetail/${encodeURIComponent(receiptId)}`;
            }
        };

        const printReceiptPaper = () => {
            const paperEl = $("frPrintableDocument");
            if (!paperEl) return;

            const printWin = window.open("", "_blank", "width=900,height=700");
            if (!printWin) {
                window.print();
                return;
            }

            printWin.document.write(`
                <!DOCTYPE html>
                <html lang="th">
                <head>
                    <meta charset="utf-8" />
                    <title>พิมพ์ใบเสร็จรับเงิน - StayMate Hotel</title>
                    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css" />
                    <link href="https://fonts.googleapis.com/css2?family=Prompt:wght@300;400;500;600;700&display=swap" rel="stylesheet" />
                    <style>
                        body { font-family: 'Prompt', sans-serif; padding: 24px; color: #0F172A; }
                        .fr-paper-logo { width: 64px; height: 64px; border-radius: 10px; object-fit: cover; }
                        .fr-doc-type-badge { background: #070D1E; color: #00E5FF; padding: 4px 12px; border-radius: 6px; font-weight: 700; font-size: 12px; }
                        .fr-cancelled-stamp { position: absolute; top: 40%; left: 50%; transform: translate(-50%, -50%) rotate(-18deg); border: 4px solid rgba(220,38,38,0.4); color: rgba(220,38,38,0.4); font-size: 32px; font-weight: 800; padding: 8px 20px; }
                    </style>
                </head>
                <body>
                    ${paperEl.innerHTML}
                    <script>
                        window.onload = function() { window.print(); window.onafterprint = function() { window.close(); }; };
                    <\/script>
                </body>
                </html>`);
            printWin.document.close();
        };

        $("frModalBtnPrint")?.addEventListener("click", printReceiptPaper);

        // ---- 3. Create Receipt ----
        const selectApprovedPay = $("frSelectApprovedPayment");
        const createPreviewBox = $("frCreatePreviewBox");
        const btnConfirmCreate = $("frBtnConfirmCreateReceipt");

        function openCreateReceiptModal() {
            if (!bsCreateModal) return;

            const formWrap = $("frCreateFormWrap");
            const emptyNotice = $("frCreateEmptyNotice");

            if (!unissuedPayments || unissuedPayments.length === 0) {
                formWrap?.classList.add("d-none");
                emptyNotice?.classList.remove("d-none");
                if (btnConfirmCreate) btnConfirmCreate.disabled = true;
            } else {
                formWrap?.classList.remove("d-none");
                emptyNotice?.classList.add("d-none");
                if (selectApprovedPay) {
                    selectApprovedPay.innerHTML =
                        `<option value="">-- เลือกรายการชำระเงินที่ได้รับการอนุมัติแล้ว (${unissuedPayments.length} รายการ) --</option>` +
                        unissuedPayments.map(p =>
                            `<option value="${escapeHtml(p.PaymentId)}">Booking ${escapeHtml(p.BookingCode)} - ${escapeHtml(p.ContactName)} (${formatTHB(p.Amount)})</option>`
                        ).join("");
                }
                createPreviewBox?.classList.add("d-none");
                if (btnConfirmCreate) btnConfirmCreate.disabled = true;
            }

            bsCreateModal.show();
        }

        $("frBtnOpenCreateModal")?.addEventListener("click", openCreateReceiptModal);
        $("frBtnEmptyCreate")?.addEventListener("click", openCreateReceiptModal);

        selectApprovedPay?.addEventListener("change", function () {
            const selectedId = Number(this.value);
            const found = unissuedPayments.find(x => Number(x.PaymentId) === selectedId);

            if (!found) {
                createPreviewBox?.classList.add("d-none");
                if (btnConfirmCreate) btnConfirmCreate.disabled = true;
                return;
            }

            $("frCrBookingCode").textContent = `${found.BookingCode} (#${found.BookingId})`;
            $("frCrCustomerName").textContent = found.ContactName || "-";
            $("frCrPaymentId").textContent = `#${found.PaymentId}`;
            $("frCrMethod").textContent = (found.Method || "N/A").toUpperCase();
            $("frCrRoomAmt").textContent = formatTHB(found.RoomAmount || found.Amount);
            $("frCrDiscountAmt").textContent = `-${formatTHB(found.DiscountAmount || 0)}`;
            $("frCrTaxAmt").textContent = formatTHB(found.TaxAmount || (found.Amount * 7 / 107));
            $("frCrNetAmt").textContent = formatTHB(found.Amount);

            createPreviewBox?.classList.remove("d-none");
            if (btnConfirmCreate) btnConfirmCreate.disabled = false;
        });

        btnConfirmCreate?.addEventListener("click", async function () {
            if (isProcessing || !selectApprovedPay?.value) return;

            const paymentId = selectApprovedPay.value;
            const found = unissuedPayments.find(x => String(x.PaymentId) === String(paymentId));

            const confirmRes = await Swal.fire({
                icon: "question",
                title: "ยืนยันการออกใบเสร็จรับเงิน?",
                html: `ต้องการออกใบเสร็จรับเงินอย่างเป็นทางการสำหรับ <strong>${escapeHtml(found?.BookingCode || "")}</strong> ยอดสุทธิ <strong>${formatTHB(found?.Amount || 0)}</strong> ใช่หรือไม่?`,
                showCancelButton: true,
                confirmButtonColor: "#16A34A",
                cancelButtonColor: "#64748B",
                confirmButtonText: "ยืนยันออกใบเสร็จ",
                cancelButtonText: "ยกเลิก"
            });

            if (!confirmRes.isConfirmed) return;

            isProcessing = true;
            btnConfirmCreate.disabled = true;

            try {
                const body = new URLSearchParams();
                body.append("paymentId", paymentId);
                body.append("__RequestVerificationToken", getCsrfToken());

                const res = await fetch(urls.createApi, {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json" },
                    body: body.toString()
                });

                const json = await res.json();
                if (!res.ok || !json.success) throw new Error(json.message || "ไม่สามารถสร้างใบเสร็จรับเงินได้");

                bsCreateModal?.hide();
                await Swal.fire({ icon: "success", title: "ออกใบเสร็จสำเร็จ!", text: json.message, confirmButtonColor: "#16A34A" });

                window.location.reload();
            } catch (err) {
                Swal.fire({ icon: "error", title: "ไม่สามารถออกใบเสร็จได้", text: err.message, confirmButtonColor: "#DC2626" });
            } finally {
                isProcessing = false;
                btnConfirmCreate.disabled = false;
            }
        });

        // ---- 4. Cancel Receipt ----
        const openCancelReceiptModal = (receiptId, receiptNo, bookingCode, customer, amount) => {
            if (!bsCancelModal) return;

            $("frCancelReceiptId").value = receiptId;
            $("frCancelDisplayNo").textContent = receiptNo;
            $("frCancelDisplayCustomer").textContent = `${customer} (${bookingCode})`;
            $("frCancelDisplayAmount").textContent = formatTHB(amount);

            const selectReason = $("frCancelReasonSelect");
            const inputReason = $("frCancelReasonInput");
            if (selectReason) selectReason.value = "";
            if (inputReason) {
                inputReason.value = "";
                inputReason.classList.remove("is-invalid");
            }

            bsCancelModal.show();
        };

        $("frCancelReasonSelect")?.addEventListener("change", function () {
            const inputReason = $("frCancelReasonInput");
            if (!inputReason) return;
            if (this.value && this.value !== "OTHER") {
                inputReason.value = this.value;
                inputReason.classList.remove("is-invalid");
            } else if (this.value === "OTHER") {
                inputReason.value = "";
                inputReason.focus();
            }
        });

        $("frCancelReceiptForm")?.addEventListener("submit", async function (e) {
            e.preventDefault();
            if (isProcessing) return;

            const receiptId = $("frCancelReceiptId").value;
            const inputReason = $("frCancelReasonInput");
            const reason = (inputReason?.value || "").trim();

            if (!reason) {
                inputReason?.classList.add("is-invalid");
                return;
            }
            inputReason?.classList.remove("is-invalid");

            isProcessing = true;
            const submitBtn = $("frBtnConfirmCancelSubmit");
            if (submitBtn) submitBtn.disabled = true;

            try {
                const body = new URLSearchParams();
                body.append("receiptId", receiptId);
                body.append("reason", reason);
                body.append("__RequestVerificationToken", getCsrfToken());

                const res = await fetch(urls.cancelApi, {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json" },
                    body: body.toString()
                });

                const json = await res.json();
                if (!res.ok || !json.success) throw new Error(json.message || "ไม่สามารถยกเลิกใบเสร็จได้");

                bsCancelModal?.hide();
                await Swal.fire({ icon: "success", title: "ยกเลิกใบเสร็จเรียบร้อยแล้ว", text: json.message, confirmButtonColor: "#2563EB" });

                window.location.reload();
            } catch (err) {
                Swal.fire({ icon: "error", title: "เกิดข้อผิดพลาด", text: err.message, confirmButtonColor: "#DC2626" });
            } finally {
                isProcessing = false;
                if (submitBtn) submitBtn.disabled = false;
            }
        });

        // ---- 5. Table actions & Export ----
        tableBody?.addEventListener("click", function (e) {
            const viewBtn = e.target.closest(".js-fr-view");
            if (viewBtn) {
                openReceiptPreview(viewBtn.getAttribute("data-id"), false);
                return;
            }

            const printBtn = e.target.closest(".js-fr-print");
            if (printBtn) {
                openReceiptPreview(printBtn.getAttribute("data-id"), true);
                return;
            }

            const cancelBtn = e.target.closest(".js-fr-cancel");
            if (cancelBtn) {
                openCancelReceiptModal(
                    cancelBtn.getAttribute("data-id"),
                    cancelBtn.getAttribute("data-no"),
                    cancelBtn.getAttribute("data-booking"),
                    cancelBtn.getAttribute("data-customer"),
                    cancelBtn.getAttribute("data-amount")
                );
            }
        });

        $("frBtnExportReport")?.addEventListener("click", function () {
            if (!tableBody) return;
            const visibleRows = Array.from(tableBody.querySelectorAll("tr.fr-receipt-row:not(.d-none)"));
            if (visibleRows.length === 0) {
                Swal.fire({ icon: "info", title: "ไม่มีข้อมูลสำหรับส่งออก", text: "ไม่พบรายการใบเสร็จในตารางปัจจุบัน" });
                return;
            }

            const headers = [
                "Receipt No", "Booking Code", "Customer Name", "Receipt Date", "Subtotal (THB)",
                "VAT 7% (THB)", "Total Amount (THB)", "Status", "Issued By"
            ];

            const lines = [headers.map(csvEsc).join(",")];
            visibleRows.forEach(r => {
                lines.push([
                    r.getAttribute("data-receipt-no"),
                    r.getAttribute("data-booking-code"),
                    r.getAttribute("data-customer"),
                    r.getAttribute("data-datetime-display"),
                    Number(r.getAttribute("data-subtotal") || 0).toFixed(2),
                    Number(r.getAttribute("data-vat") || 0).toFixed(2),
                    Number(r.getAttribute("data-total") || 0).toFixed(2),
                    r.getAttribute("data-status-label"),
                    r.getAttribute("data-issued-by")
                ].map(csvEsc).join(","));
            });

            downloadCsv(lines, `staymate-receipts-${todayStamp()}.csv`);
        });
    }

    // =====================================================================
    // 5. PAYMENT DETAIL & RECEIPT DETAIL PAGES
    // =====================================================================
    function initDetailPages() {
        // ---- PaymentDetail.cshtml ----
        const fpdApp = document.getElementById("fpdPaymentDetailApp");
        if (fpdApp) {
            const slipImg = document.getElementById("fpdSlipImage");
            const slipStage = document.getElementById("fpdSlipStage");
            let zoomLevel = 1;

            const updateZoom = (delta, reset = false) => {
                if (!slipImg) return;
                zoomLevel = reset ? 1 : Math.min(3.5, Math.max(0.5, zoomLevel + delta));
                slipImg.style.transform = `scale(${zoomLevel})`;
            };

            document.getElementById("fpdBtnZoomIn")?.addEventListener("click", () => updateZoom(0.25));
            document.getElementById("fpdBtnZoomOut")?.addEventListener("click", () => updateZoom(-0.25));
            document.getElementById("fpdBtnZoomReset")?.addEventListener("click", () => updateZoom(0, true));
            document.getElementById("fpdBtnFullscreen")?.addEventListener("click", () => {
                if (slipStage && slipStage.requestFullscreen) slipStage.requestFullscreen();
            });

            // ยืนยันการอนุมัติก่อน Submit ฟอร์มไป FinanceController.Approve
            const approveForm = document.getElementById("fpdApproveForm");
            const approveBtn = document.getElementById("fpdBtnApproveSubmit");

            if (approveForm && approveBtn) {
                approveForm.addEventListener("submit", function (e) {
                    if (approveForm.dataset.confirmed === "true") return;
                    e.preventDefault();

                    const payId = escapeHtml(approveBtn.getAttribute("data-payment-id") || "");
                    const bookingCode = escapeHtml(approveBtn.getAttribute("data-booking-code") || "");
                    const amount = escapeHtml(approveBtn.getAttribute("data-amount") || "0.00");

                    if (typeof Swal === "undefined") {
                        if (confirm(`ยืนยันการอนุมัติสลิป #${payId} ยอด ฿${amount} หรือไม่?`)) {
                            approveForm.dataset.confirmed = "true";
                            approveForm.submit();
                        }
                        return;
                    }

                    Swal.fire({
                        icon: "question",
                        title: "ยืนยันการอนุมัติการชำระเงิน?",
                        html: `ต้องการอนุมัติรายการชำระเงิน <strong>#${payId}</strong> ของการจอง <strong>${bookingCode}</strong> จำนวนเงิน <strong class="text-success">฿${amount}</strong> และออกใบเสร็จรับเงินอัตโนมัติใช่หรือไม่?`,
                        showCancelButton: true,
                        confirmButtonColor: "#16A34A",
                        cancelButtonColor: "#64748B",
                        confirmButtonText: '<i class="fa-solid fa-check me-1"></i> ยืนยันอนุมัติ',
                        cancelButtonText: "ยกเลิก"
                    }).then(result => {
                        if (result.isConfirmed) {
                            approveBtn.disabled = true;
                            approveForm.dataset.confirmed = "true";
                            approveForm.submit();
                        }
                    });
                });
            }

            const rejectModalEl = document.getElementById("fpdRejectModal");
            const bsRejectModal = rejectModalEl ? new bootstrap.Modal(rejectModalEl) : null;

            document.getElementById("fpdBtnOpenRejectModal")?.addEventListener("click", () => bsRejectModal?.show());

            const presetSelect = document.getElementById("fpdRejectPreset");
            const reasonInput = document.getElementById("fpdRejectReasonInput");

            presetSelect?.addEventListener("change", function () {
                if (!reasonInput) return;
                if (this.value && this.value !== "OTHER") {
                    reasonInput.value = this.value;
                    reasonInput.classList.remove("is-invalid");
                } else if (this.value === "OTHER") {
                    reasonInput.value = "";
                    reasonInput.focus();
                }
            });

            document.getElementById("fpdRejectForm")?.addEventListener("submit", function (e) {
                const val = (reasonInput?.value || "").trim();
                if (!val) {
                    e.preventDefault();
                    reasonInput?.classList.add("is-invalid");
                }
            });
        }

        // ---- ReceiptDetail.cshtml ----
        if (document.getElementById("frdReceiptDetailApp")) {
            document.getElementById("frdBtnPrintDocument")?.addEventListener("click", () => window.print());
        }
    }

    // =====================================================================
    // 6. FINANCIAL REPORTS  (window.financeReports)
    // =====================================================================
    function initReports() {
        const appEl = document.getElementById("frepReportsApp");
        if (!appEl) return;

        setChartDefaults();

        const $ = (id) => document.getElementById(id);

        const reportsUrl = appEl.getAttribute("data-reports-url") || "/Finance/Reports";
        const startDateInput = $("frepStartDate");
        const endDateInput = $("frepEndDate");
        const periodForm = $("frepPeriodForm");

        const reportData = Object.assign({
            daily: [], weekly: [], monthly: [], revenueBreakdown: [], expenseBreakdown: [],
            totals: { grossRevenue: 0, totalRevenue: 0, totalExpenses: 0, totalRefund: 0, netProfit: 0 }
        }, readJsonScript("frepChartDataJson", {}));

        const toIsoDateString = (dt) => {
            const y = dt.getFullYear();
            const m = String(dt.getMonth() + 1).padStart(2, "0");
            const d = String(dt.getDate()).padStart(2, "0");
            return `${y}-${m}-${d}`;
        };

        const getWeekStart = (now) => {
            const day = now.getDay();
            const diff = day === 0 ? -6 : 1 - day;
            const ws = new Date(now);
            ws.setDate(now.getDate() + diff);
            return ws;
        };

        // ---- 1. Preset ช่วงเวลา ----
        const highlightMatchingPreset = () => {
            if (!startDateInput || !endDateInput) return;
            const now = new Date();
            const todayIso = toIsoDateString(now);
            const weekStartIso = toIsoDateString(getWeekStart(now));
            const monthStartIso = toIsoDateString(new Date(now.getFullYear(), now.getMonth(), 1));
            const yearStartIso = toIsoDateString(new Date(now.getFullYear(), 0, 1));

            const curFrom = startDateInput.value;
            const curTo = endDateInput.value;

            let matchedRange = "custom";
            if (curFrom === todayIso && curTo === todayIso) matchedRange = "today";
            else if (curFrom === weekStartIso && curTo === todayIso) matchedRange = "week";
            else if (curFrom === monthStartIso && curTo === todayIso) matchedRange = "month";
            else if (curFrom === yearStartIso && curTo === todayIso) matchedRange = "year";

            document.querySelectorAll(".frep-preset-btn").forEach(btn => {
                btn.classList.toggle("active", btn.getAttribute("data-range") === matchedRange);
            });
        };

        highlightMatchingPreset();

        document.querySelectorAll(".frep-preset-btn").forEach(btn => {
            btn.addEventListener("click", function () {
                const range = this.getAttribute("data-range");
                document.querySelectorAll(".frep-preset-btn").forEach(b => b.classList.remove("active"));
                this.classList.add("active");

                if (range === "custom") {
                    startDateInput?.focus();
                    return;
                }

                const now = new Date();
                let fromDt = new Date(now);

                if (range === "week") fromDt = getWeekStart(now);
                else if (range === "month") fromDt = new Date(now.getFullYear(), now.getMonth(), 1);
                else if (range === "year") fromDt = new Date(now.getFullYear(), 0, 1);

                if (startDateInput && endDateInput && periodForm) {
                    startDateInput.value = toIsoDateString(fromDt);
                    endDateInput.value = toIsoDateString(now);
                    periodForm.submit();
                }
            });
        });

        $("frepBtnResetPeriod")?.addEventListener("click", () => { window.location.href = reportsUrl; });

        // ---- 2. กราฟรายรับ-รายจ่าย ----
        const revExpCanvas = $("frepRevExpChart");
        let revExpChartInstance = null;

        const getSeriesByMode = (mode) => {
            if (mode === "weekly") return reportData.weekly || [];
            if (mode === "monthly") return reportData.monthly || [];
            return reportData.daily || [];
        };

        if (revExpCanvas && typeof Chart !== "undefined") {
            const series = getSeriesByMode("daily");

            revExpChartInstance = new Chart(revExpCanvas.getContext("2d"), {
                type: "bar",
                data: {
                    labels: series.map(x => x.label),
                    datasets: [
                        {
                            label: "รายรับสุทธิ (Net Revenue)",
                            data: series.map(x => x.income),
                            backgroundColor: "rgba(22, 163, 74, 0.85)",
                            borderColor: "#16A34A",
                            borderWidth: 1,
                            borderRadius: 6,
                            maxBarThickness: 34
                        },
                        {
                            label: "รายจ่าย (Expenses)",
                            data: series.map(x => x.expense),
                            backgroundColor: "rgba(220, 38, 38, 0.82)",
                            borderColor: "#DC2626",
                            borderWidth: 1,
                            borderRadius: 6,
                            maxBarThickness: 34
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: { position: "top", align: "end", labels: { usePointStyle: true, boxWidth: 8 } },
                        tooltip: {
                            backgroundColor: "#070D1E",
                            titleColor: "#00E5FF",
                            bodyColor: "#FFFFFF",
                            padding: 12,
                            callbacks: { label: (c) => ` ${c.dataset.label}: ${formatTHB(c.parsed.y)}` }
                        }
                    },
                    scales: {
                        x: { grid: { display: false } },
                        y: {
                            beginAtZero: true,
                            grid: { color: "#F1F5F9" },
                            ticks: { callback: (v) => "฿" + Number(v).toLocaleString("th-TH") }
                        }
                    }
                }
            });
        }

        document.querySelectorAll("[data-chart-mode]").forEach(btn => {
            btn.addEventListener("click", function () {
                document.querySelectorAll("[data-chart-mode]").forEach(b => b.classList.remove("active"));
                this.classList.add("active");

                const series = getSeriesByMode(this.getAttribute("data-chart-mode") || "daily");

                if (revExpChartInstance) {
                    revExpChartInstance.data.labels = series.map(x => x.label);
                    revExpChartInstance.data.datasets[0].data = series.map(x => x.income);
                    revExpChartInstance.data.datasets[1].data = series.map(x => x.expense); // [แก้] เดิมเขียน datasets.data ทำให้รายจ่ายไม่อัปเดต
                    revExpChartInstance.update();
                }
            });
        });

        // ---- 3. Donut ----
        const initDonutChart = (canvasId, items) => {
            const canvas = $(canvasId);
            if (!canvas || typeof Chart === "undefined") return;

            const activeItems = (items || []).filter(x => Number(x.amount) > 0);
            if (activeItems.length === 0) return;

            const total = activeItems.reduce((sum, cur) => sum + Number(cur.amount), 0);

            new Chart(canvas.getContext("2d"), {
                type: "doughnut",
                data: {
                    labels: activeItems.map(x => x.label),
                    datasets: [{
                        data: activeItems.map(x => x.amount),
                        backgroundColor: activeItems.map(x => x.color),
                        borderColor: "#FFFFFF",
                        borderWidth: 3,
                        hoverOffset: 5
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: "70%",
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: "#070D1E",
                            titleColor: "#00E5FF",
                            bodyColor: "#FFFFFF",
                            padding: 10,
                            callbacks: {
                                label: (ctx) => {
                                    const val = Number(ctx.raw) || 0;
                                    const pct = total > 0 ? ((val / total) * 100).toFixed(1) : "0.0";
                                    return ` ${formatTHB(val)} (${pct}%)`;
                                }
                            }
                        }
                    }
                }
            });
        };

        initDonutChart("frepRevenueDonutChart", reportData.revenueBreakdown);
        initDonutChart("frepExpenseDonutChart", reportData.expenseBreakdown);

        // ---- 4. ตารางธุรกรรม ----
        const tableBody = $("frepTableBody");
        const tableContainer = $("frepTableContainer");
        const tableEmpty = $("frepTableEmpty");
        const searchInput = $("frepTableSearch");
        const typeSelect = $("frepTableTypeFilter");
        const sortSelect = $("frepTableSort");
        const pageSizeSelect = $("frepTablePageSize");
        const pgInfo = $("frepPaginationInfo");
        const pgControls = $("frepPaginationControls");

        const allTableRows = tableBody ? Array.from(tableBody.querySelectorAll("tr.frep-txn-row")) : [];
        let currentPage = 1;

        const renderTablePage = () => {
            if (!tableBody) return;

            const keyword = (searchInput?.value || "").toLowerCase().trim();
            const typeFilter = (typeSelect?.value || "all").toLowerCase();
            const sortBy = sortSelect?.value || "date_desc";
            const pageSize = parseInt(pageSizeSelect?.value || "10", 10);

            const filtered = allTableRows.filter(row => {
                const rType = (row.getAttribute("data-type") || "").toLowerCase();
                const rSearch = row.getAttribute("data-search") || "";
                if (typeFilter !== "all" && rType !== typeFilter) return false;
                if (keyword && !rSearch.includes(keyword)) return false;
                return true;
            });

            filtered.sort((a, b) => {
                if (sortBy === "amount_desc" || sortBy === "amount_asc") {
                    const amtA = parseFloat(a.getAttribute("data-amount") || "0");
                    const amtB = parseFloat(b.getAttribute("data-amount") || "0");
                    return sortBy === "amount_desc" ? amtB - amtA : amtA - amtB;
                }
                const tA = a.getAttribute("data-timestamp") || "";
                const tB = b.getAttribute("data-timestamp") || "";
                return sortBy === "date_asc" ? tA.localeCompare(tB) : tB.localeCompare(tA);
            });

            const totalFiltered = filtered.length;
            const totalPages = Math.max(1, Math.ceil(totalFiltered / pageSize));
            if (currentPage > totalPages) currentPage = 1;

            const startIdx = (currentPage - 1) * pageSize;
            const pageItems = filtered.slice(startIdx, startIdx + pageSize);

            allTableRows.forEach(r => r.classList.add("d-none"));
            pageItems.forEach(r => {
                r.classList.remove("d-none");
                tableBody.appendChild(r);
            });

            if (totalFiltered === 0) {
                tableContainer?.classList.add("d-none");
                tableEmpty?.classList.remove("d-none");
                if (pgInfo) pgInfo.textContent = "ไม่พบรายการธุรกรรม";
                if (pgControls) pgControls.innerHTML = "";
                return;
            }

            tableContainer?.classList.remove("d-none");
            tableEmpty?.classList.add("d-none");

            if (pgInfo) {
                pgInfo.textContent = `แสดง ${startIdx + 1} – ${Math.min(startIdx + pageSize, totalFiltered)} จากทั้งหมด ${totalFiltered} รายการ`;
            }

            if (pgControls) {
                pgControls.innerHTML = totalPages <= 1 ? "" : renderPager({
                    page: currentPage,
                    totalPages,
                    hasPrevious: currentPage > 1,
                    hasNext: currentPage < totalPages
                });
            }
        };

        searchInput?.addEventListener("input", () => { currentPage = 1; renderTablePage(); });
        typeSelect?.addEventListener("change", () => { currentPage = 1; renderTablePage(); });
        sortSelect?.addEventListener("change", () => { currentPage = 1; renderTablePage(); });
        pageSizeSelect?.addEventListener("change", () => { currentPage = 1; renderTablePage(); });

        pgControls?.addEventListener("click", function (e) {
            e.preventDefault();
            const link = e.target.closest("a.page-link");
            if (!link || link.parentElement.classList.contains("disabled")) return;
            const target = parseInt(link.getAttribute("data-page") || "1", 10);
            if (target !== currentPage) {
                currentPage = target;
                renderTablePage();
            }
        });

        renderTablePage();

        // ---- 5. Export CSV & Print ----
        $("frepBtnExportCsv")?.addEventListener("click", function () {
            if (allTableRows.length === 0) {
                alert("ไม่มีรายการธุรกรรมในช่วงเวลานี้สำหรับส่งออก CSV");
                return;
            }

            const headers = [
                "Date", "Transaction ID", "Description", "Booking Code", "Category",
                "Type", "Payment Method", "Amount (THB)", "Status"
            ];

            const lines = [headers.map(csvEsc).join(",")];
            allTableRows.forEach(r => {
                lines.push([
                    r.getAttribute("data-date"),
                    `TXN-${String(r.getAttribute("data-id") || "").padStart(5, "0")}`,
                    r.getAttribute("data-description"),
                    r.getAttribute("data-booking"),
                    r.getAttribute("data-category"),
                    r.getAttribute("data-type-label"),
                    r.getAttribute("data-method"),
                    Number(r.getAttribute("data-signed-amount") || 0).toFixed(2),
                    r.getAttribute("data-status")
                ].map(csvEsc).join(","));
            });

            const fromStr = appEl.getAttribute("data-from") || "";
            const toStr = appEl.getAttribute("data-to") || "";
            downloadCsv(lines, `staymate-financial-report_${fromStr}_${toStr}.csv`);
        });

        $("frepBtnPrintReport")?.addEventListener("click", function () {
            const prevPageSize = pageSizeSelect ? pageSizeSelect.value : "10";
            const prevPage = currentPage;
            if (pageSizeSelect) {
                // ใส่ option 9999 ชั่วคราวถ้ายังไม่มี เพื่อให้ select รับค่าได้
                if (![...pageSizeSelect.options].some(o => o.value === "9999")) {
                    pageSizeSelect.appendChild(new Option("All", "9999"));
                }
                pageSizeSelect.value = "9999";
                currentPage = 1;
                renderTablePage();
            }
            window.print();
            if (pageSizeSelect) {
                pageSizeSelect.value = prevPageSize;
                currentPage = prevPage;
                renderTablePage();
            }
        });
    }

    // ---------------------------------------------------------------------
    // Bootstrap: namespaces (เผื่อ view เรียก init เอง) + เริ่มทำงานทุกโมดูล
    // ---------------------------------------------------------------------
    window.financeTransactions = { init: initTransactions };
    window.financeReceipts = { init: initReceipts };
    window.financeReports = { init: initReports };

    onReady(initDashboard);
    onReady(initPaymentApproval);
    onReady(initTransactions);
    onReady(initReceipts);
    onReady(initDetailPages);
    onReady(initReports);
})();