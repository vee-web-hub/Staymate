/* wwwroot/js/Owner/Owner.js
 * Owner Portal (Read-only): ดึง JSON จาก Controller (GET เท่านั้น) แล้วเติมลง DOM, ทำ KPI Counter และวาดกราฟ Chart.js
 * รองรับ Executive Dashboard, Financial Overview, Performance Reports และ Staff Overview
 *
 * สัญญากับ View (data-attribute):
 *   <div data-owner-page="dashboard|financial|performance|staff"
 *        data-api-url="@Url.Action("DashboardData", "Owner")"></div>
 *
 *   ค่าตัวเลข/ข้อความ : <span data-bind="totalRevenue" data-format="currency|percent|number|text"
 *                             data-null-text="ไม่รองรับ" data-counter-value></span>
 *   รายการข้อความ     : <ul data-list="dataNotes"></ul>
 *   ตาราง            : <table data-table="monthlyReports"> (thead มี th[data-col][data-format])
 *                      - ตารางที่มี tbody[data-fin-tx-tbody] (Razor render เอง) JS จะไม่เขียนทับ
 *                      - แถวที่มี field "month" จะได้ attribute data-perf-month-row อัตโนมัติ
 *   กราฟทั่วไป        : <canvas data-chart="monthlyRevenue,monthlyExpense,monthlyProfit"
 *                               data-chart-type="line|bar|doughnut"
 *                               data-chart-titles="รายได้|รายจ่าย|กำไร"
 *                               data-chart-format="currency|percent|number"></canvas>
 *   กราฟ Performance  : canvas id = ownerPerfRevenueChart, ownerPerfProfitExpenseBarChart,
 *                       ownerPerfOccupancyDonut, ownerPerfYearlyComparisonChart
 *                       (ห้ามใส่ data-chart ที่ canvas เหล่านี้ เพราะจะวาดซ้ำ)
 *   กราฟ Staff        : canvas id = ownerStaffStatusDonutChart, ownerStaffPositionBarChart
 *                       (ห้ามใส่ data-chart เช่นกัน)
 *   ตัวกรองปี/เดือน   : <select data-owner-year> , <select data-owner-month>
 */
(function () {
    "use strict";

    var charts = [];
    var lastLoadedData = null;
    var chartJsLoadingPromise = null;
    var CHART_JS_CDN = "https://cdn.jsdelivr.net/npm/chart.js@4.4.3/dist/chart.umd.min.js";

    // ───────────── Formatters ─────────────
    var currencyFmt = new Intl.NumberFormat("th-TH", {
        style: "currency",
        currency: "THB",
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
    var numberFmt = new Intl.NumberFormat("th-TH");
    var percentFmt = new Intl.NumberFormat("th-TH", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    function format(value, kind, nullText) {
        if (value === null || value === undefined || value === "") {
            return nullText !== undefined && nullText !== null ? nullText : "-";
        }
        var num = Number(value);
        switch (kind) {
            case "currency":
                return isNaN(num) ? String(value) : currencyFmt.format(num);
            case "percent":
                return isNaN(num) ? String(value) : percentFmt.format(num) + "%";
            case "number":
                return isNaN(num) ? String(value) : numberFmt.format(num);
            default:
                return String(value);
        }
    }

    function getPath(obj, path) {
        if (!obj || !path) return undefined;
        return path.split(".").reduce(function (acc, key) {
            if (acc === null || acc === undefined) return undefined;
            if (acc[key] !== undefined) return acc[key];
            // รองรับทั้ง camelCase และ PascalCase
            var altKey = key.charAt(0).toUpperCase() + key.slice(1);
            return acc[altKey];
        }, obj);
    }

    // ───────────── โหลด Chart.js อัตโนมัติหากยังไม่มีในหน้า ─────────────
    function ensureChartJsLoaded() {
        if (typeof window.Chart !== "undefined") {
            return Promise.resolve(window.Chart);
        }
        if (chartJsLoadingPromise) {
            return chartJsLoadingPromise;
        }
        chartJsLoadingPromise = new Promise(function (resolve) {
            var existingScript = document.querySelector("script[data-owner-chartjs]");
            if (existingScript) {
                existingScript.addEventListener("load", function () { resolve(window.Chart); });
                existingScript.addEventListener("error", function () { resolve(undefined); });
                return;
            }
            var script = document.createElement("script");
            script.src = CHART_JS_CDN;
            script.async = true;
            script.setAttribute("data-owner-chartjs", "true");
            script.onload = function () { resolve(window.Chart); };
            script.onerror = function () { resolve(undefined); };
            document.head.appendChild(script);
        });
        return chartJsLoadingPromise;
    }

    // ───────────── Fetch (GET เท่านั้น - Read-Only) ─────────────
    async function fetchJson(url) {
        var response = await fetch(url, {
            method: "GET",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "X-Requested-With": "XMLHttpRequest"
            }
        });
        if (response.status === 401) {
            window.location.reload();
            throw new Error("Unauthorized");
        }
        if (response.status === 403) {
            throw new Error("ไม่มีสิทธิ์เข้าถึงข้อมูลนี้ (สำหรับ OWNER เท่านั้น)");
        }
        if (!response.ok) {
            throw new Error("โหลดข้อมูลไม่สำเร็จ (HTTP " + response.status + ")");
        }
        return response.json();
    }

    // ───────────── KPI Number Counter Animation ─────────────
    function animateValue(el, endValue, kind, nullText) {
        var target = Number(endValue);
        if (endValue === null || endValue === undefined || isNaN(target)) {
            el.textContent = format(endValue, kind, nullText);
            return;
        }
        var duration = 650;
        var startTime = null;

        function step(timestamp) {
            if (!startTime) startTime = timestamp;
            var progress = Math.min((timestamp - startTime) / duration, 1);
            var eased = 1 - Math.pow(1 - progress, 3);
            var current = target * eased;

            el.textContent = kind === "number"
                ? format(Math.round(current), kind, nullText)
                : format(current, kind, nullText);

            if (progress < 1) {
                window.requestAnimationFrame(step);
            } else {
                el.textContent = format(target, kind, nullText);
            }
        }
        window.requestAnimationFrame(step);
    }

    // ───────────── Renderers ─────────────
    function renderBindings(root, data) {
        root.querySelectorAll("[data-bind]").forEach(function (el) {
            var value = getPath(data, el.getAttribute("data-bind"));
            var kind = el.getAttribute("data-format");
            var nullText = el.getAttribute("data-null-text");

            if (el.hasAttribute("data-counter-value") && (kind === "currency" || kind === "percent" || kind === "number")) {
                animateValue(el, value, kind, nullText);
            } else {
                el.textContent = format(value, kind, nullText);
            }
        });
    }

    function renderLists(root, data) {
        root.querySelectorAll("[data-list]").forEach(function (el) {
            var items = getPath(data, el.getAttribute("data-list"));
            el.replaceChildren();
            var list = Array.isArray(items) ? items : [];
            if (list.length === 0) {
                var emptyLi = document.createElement("li");
                emptyLi.textContent = "ไม่มีหมายเหตุเพิ่มเติม";
                el.appendChild(emptyLi);
                return;
            }
            list.forEach(function (text) {
                var li = document.createElement("li");
                li.textContent = String(text);
                el.appendChild(li);
            });
        });
    }

    function renderTables(root, data) {
        root.querySelectorAll("table[data-table]").forEach(function (table) {
            // ตารางที่ Razor render เอง (มี client-side filter/pagination) ห้ามเขียนทับ
            if (table.querySelector("[data-fin-tx-tbody]")) return;

            var rows = getPath(data, table.getAttribute("data-table"));
            var cols = Array.prototype.slice.call(table.querySelectorAll("thead [data-col]"));
            var tbody = table.tBodies[0] || table.createTBody();
            tbody.replaceChildren();

            if (!Array.isArray(rows) || rows.length === 0) {
                var emptyRow = tbody.insertRow();
                var emptyCell = emptyRow.insertCell();
                emptyCell.colSpan = Math.max(cols.length, 1);
                emptyCell.className = "owner-empty-cell";
                emptyCell.textContent = "ไม่มีข้อมูลในช่วงเวลาที่เลือก";
                return;
            }

            rows.forEach(function (row) {
                var tr = tbody.insertRow();

                // ใช้กับตัวกรองช่วงเดือนของหน้า Performance Reports
                var monthVal = getPath(row, "month");
                if (monthVal !== null && monthVal !== undefined) {
                    tr.setAttribute("data-perf-month-row", String(monthVal));
                }

                cols.forEach(function (col) {
                    var td = tr.insertCell();
                    var colKey = col.getAttribute("data-col");
                    var kind = col.getAttribute("data-format");
                    var cellVal = getPath(row, colKey);
                    td.textContent = format(cellVal, kind, col.getAttribute("data-null-text"));
                    if (kind === "currency" || kind === "percent" || kind === "number") {
                        td.style.textAlign = "right";
                    }
                    if (colKey === "profit" || colKey === "netProfit") {
                        var numVal = Number(cellVal);
                        if (!isNaN(numVal)) {
                            td.className = numVal >= 0 ? "owner-text-success" : "owner-text-danger";
                        }
                    }
                });
            });
        });
    }

    // ───────────── Palette & Chart.js Config ─────────────
    var SERIES_PALETTE = [
        { border: "#00E5FF", bg: "rgba(0, 229, 255, 0.55)", lineBg: "rgba(0, 229, 255, 0.16)" },
        { border: "#F59E0B", bg: "rgba(245, 158, 11, 0.55)", lineBg: "rgba(245, 158, 11, 0.16)" },
        { border: "#10B981", bg: "rgba(16, 185, 129, 0.55)", lineBg: "rgba(16, 185, 129, 0.16)" },
        { border: "#8B5CF6", bg: "rgba(139, 92, 246, 0.55)", lineBg: "rgba(139, 92, 246, 0.16)" }
    ];

    var DOUGHNUT_COLORS = ["#00E5FF", "#8B5CF6", "#F59E0B", "#10B981", "#2563EB", "#EC4899", "#334155"];

    function setCanvasEmptyState(canvas, isEmpty, message) {
        var parent = canvas.parentElement;
        if (!parent) return;
        var emptyEl = parent.querySelector(".owner-chart-empty-state");
        if (isEmpty) {
            if (!emptyEl) {
                emptyEl = document.createElement("div");
                emptyEl.className = "owner-chart-empty-state";
                parent.appendChild(emptyEl);
            }
            emptyEl.textContent = message || "ไม่มีข้อมูลสำหรับแสดงกราฟในช่วงเวลานี้";
            emptyEl.style.display = "flex";
        } else if (emptyEl) {
            emptyEl.style.display = "none";
        }
    }

    function renderCharts(root, data) {
        if (typeof window.Chart === "undefined") return;

        charts.forEach(function (c) {
            try { c.destroy(); } catch (_) { /* ignore */ }
        });
        charts = [];

        root.querySelectorAll("canvas[data-chart]").forEach(function (canvas) {
            var existingInstance = window.Chart.getChart ? window.Chart.getChart(canvas) : null;
            if (existingInstance) existingInstance.destroy();

            var sources = canvas.getAttribute("data-chart").split(",").map(function (s) { return s.trim(); });
            var titles = (canvas.getAttribute("data-chart-titles") || "").split("|");
            var type = canvas.getAttribute("data-chart-type") || "bar";
            var valueFormat = canvas.getAttribute("data-chart-format") || "currency";

            function fmtValue(v) {
                if (valueFormat === "percent") return percentFmt.format(v) + "%";
                if (valueFormat === "number") return numberFmt.format(v);
                return currencyFmt.format(v);
            }

            var labels = [];
            var hasAnyPoint = false;

            var datasets = sources.map(function (source, index) {
                var series = getPath(data, source);
                series = Array.isArray(series) ? series : [];

                if (labels.length === 0 && series.length > 0) {
                    labels = series.map(function (p) {
                        var lbl = p.label !== undefined ? p.label
                            : (p.Label !== undefined ? p.Label
                            : (p.category !== undefined ? p.category : p.Category));
                        return lbl || "-";
                    });
                }

                var values = series.map(function (p) {
                    var raw = p.amount !== undefined ? p.amount
                        : (p.Amount !== undefined ? p.Amount
                        : (p.count !== undefined ? p.count : p.Count));
                    var num = Number(raw);
                    if (!isNaN(num) && num !== 0) hasAnyPoint = true;
                    return isNaN(num) ? 0 : num;
                });

                var colorSet = SERIES_PALETTE[index % SERIES_PALETTE.length];

                if (type === "doughnut" || type === "pie") {
                    return {
                        label: titles[index] || source,
                        data: values,
                        backgroundColor: DOUGHNUT_COLORS.slice(0, Math.max(values.length, 2)),
                        borderColor: "#0E172E",
                        borderWidth: 3,
                        hoverOffset: 6
                    };
                }

                return {
                    label: titles[index] || source,
                    data: values,
                    borderColor: colorSet.border,
                    backgroundColor: type === "line" ? colorSet.lineBg : colorSet.bg,
                    borderWidth: 2,
                    borderRadius: type === "bar" ? 6 : 0,
                    pointBackgroundColor: colorSet.border,
                    pointRadius: type === "line" ? 4 : 0,
                    pointHoverRadius: type === "line" ? 6 : 0,
                    tension: 0.35,
                    fill: type === "line" && index === 0
                };
            });

            if (!hasAnyPoint || labels.length === 0) {
                setCanvasEmptyState(canvas, true, "ยังไม่มีข้อมูลธุรกรรมสำหรับวาดกราฟในช่วงเวลานี้");
                return;
            }
            setCanvasEmptyState(canvas, false);

            var isRound = type === "doughnut" || type === "pie";

            var options = {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: {
                        display: !isRound,
                        labels: { color: "#CBD5E1", usePointStyle: true, padding: 14 }
                    },
                    tooltip: {
                        backgroundColor: "rgba(5, 11, 24, 0.95)",
                        borderColor: "rgba(0, 229, 255, 0.35)",
                        borderWidth: 1,
                        padding: 10,
                        callbacks: {
                            label: function (ctx) {
                                var label = ctx.dataset.label || "";
                                var val = ctx.parsed.y !== undefined ? ctx.parsed.y : ctx.parsed;
                                return " " + (isRound ? (ctx.label || label) : label) + ": " + fmtValue(Number(val));
                            }
                        }
                    }
                }
            };

            if (isRound) {
                options.cutout = "68%";
            } else {
                options.scales = {
                    x: { grid: { color: "rgba(148, 163, 184, 0.08)" }, ticks: { color: "#94A3B8" } },
                    y: {
                        grid: { color: "rgba(148, 163, 184, 0.08)" },
                        ticks: {
                            color: "#94A3B8",
                            callback: function (v) { return numberFmt.format(Number(v)); }
                        }
                    }
                };
            }

            charts.push(new window.Chart(canvas, {
                type: type,
                data: { labels: labels, datasets: datasets },
                options: options
            }));
        });
    }

    // ───────────── Financial Overview: Preset Filter, Chart Switcher, Table Search & Pagination ─────────────
    function initFinancialOverviewFeatures(root) {
        // 1) Date Preset Filter Buttons
        var filterForm = root.querySelector("[data-fin-filter-form]");
        var presetInput = document.getElementById("finPresetInput");
        var customRangeBox = root.querySelector("[data-fin-custom-range]");

        if (filterForm && presetInput) {
            root.querySelectorAll("[data-fin-preset]").forEach(function (btn) {
                btn.addEventListener("click", function () {
                    var chosen = btn.getAttribute("data-fin-preset");
                    presetInput.value = chosen;
                    root.querySelectorAll("[data-fin-preset]").forEach(function (b) {
                        b.classList.toggle("active", b === btn);
                    });

                    if (chosen === "custom") {
                        if (customRangeBox) customRangeBox.classList.add("is-visible");
                    } else {
                        if (customRangeBox) customRangeBox.classList.remove("is-visible");
                        filterForm.submit();
                    }
                });
            });
        }

        // 2) Bar / Line Chart Mode Switcher
        var mainChartCanvas = document.getElementById("ownerFinIncomeExpenseChart");
        root.querySelectorAll("[data-fin-chart-mode]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                if (!mainChartCanvas || !lastLoadedData) return;
                var mode = btn.getAttribute("data-fin-chart-mode");
                mainChartCanvas.setAttribute("data-chart-type", mode);
                root.querySelectorAll("[data-fin-chart-mode]").forEach(function (b) {
                    b.classList.toggle("active", b === btn);
                });
                renderCharts(root, lastLoadedData);
            });
        });

        // 3) Financial Transactions Table: Client-side Search, Filter & Pagination
        var tbody = root.querySelector("[data-fin-tx-tbody]");
        if (!tbody) return;

        var allRows = Array.prototype.slice.call(tbody.querySelectorAll("tr[data-tx-row]"));
        var searchInput = root.querySelector("[data-fin-tx-search]");
        var typeSelect = root.querySelector("[data-fin-tx-type]");
        var statusSelect = root.querySelector("[data-fin-tx-status]");
        var fromInput = root.querySelector("[data-fin-tx-from]");
        var toInput = root.querySelector("[data-fin-tx-to]");
        var resetBtn = root.querySelector("[data-fin-tx-reset]");
        var countBadge = root.querySelector("[data-fin-tx-count]");
        var pageInfo = root.querySelector("[data-fin-page-info]");
        var pageBtns = root.querySelector("[data-fin-page-buttons]");

        var pageSize = 10;
        var currentPage = 1;

        function applyTableFilter() {
            var q = (searchInput && searchInput.value ? searchInput.value : "").trim().toLowerCase();
            var tVal = typeSelect && typeSelect.value ? typeSelect.value : "";
            var sVal = statusSelect && statusSelect.value ? statusSelect.value : "";
            var dFrom = fromInput && fromInput.value ? fromInput.value : "";
            var dTo = toInput && toInput.value ? toInput.value : "";

            var matched = allRows.filter(function (row) {
                var rId = (row.getAttribute("data-tx-id") || "").toLowerCase();
                var rDesc = (row.getAttribute("data-tx-desc") || "").toLowerCase();
                var rType = row.getAttribute("data-tx-type") || "";
                var rStatus = row.getAttribute("data-tx-status") || "";
                var rDate = row.getAttribute("data-tx-date") || "";

                if (q && rId.indexOf(q) === -1 && rDesc.indexOf(q) === -1) return false;
                if (tVal && rType !== tVal) return false;
                if (sVal && rStatus !== sVal) return false;
                if (dFrom && rDate < dFrom) return false;
                if (dTo && rDate > dTo) return false;
                return true;
            });

            var totalPages = Math.max(1, Math.ceil(matched.length / pageSize));
            if (currentPage > totalPages) currentPage = totalPages;

            var startIdx = (currentPage - 1) * pageSize;
            var endIdx = startIdx + pageSize;

            allRows.forEach(function (r) { r.style.display = "none"; });
            matched.slice(startIdx, endIdx).forEach(function (r) { r.style.display = ""; });

            var existingEmpty = tbody.querySelector("[data-fin-empty-dynamic]");
            if (matched.length === 0 && allRows.length > 0) {
                if (!existingEmpty) {
                    existingEmpty = tbody.insertRow();
                    existingEmpty.setAttribute("data-fin-empty-dynamic", "true");
                    var cell = existingEmpty.insertCell();
                    cell.colSpan = 6;
                    cell.className = "owner-empty-cell";
                    cell.textContent = "ไม่พบรายการธุรกรรมที่ตรงกับเงื่อนไขการค้นหา";
                }
                existingEmpty.style.display = "";
            } else if (existingEmpty) {
                existingEmpty.style.display = "none";
            }

            if (countBadge) {
                countBadge.textContent = "แสดง " + matched.length + " จาก " + allRows.length + " รายการ";
            }
            if (pageInfo) {
                pageInfo.textContent = matched.length === 0
                    ? "ไม่พบรายการ"
                    : "แสดงลำดับที่ " + (startIdx + 1) + " – " + Math.min(endIdx, matched.length) + " (หน้า " + currentPage + "/" + totalPages + ")";
            }

            if (pageBtns) {
                pageBtns.replaceChildren();
                for (var p = 1; p <= totalPages; p++) {
                    (function (pageNumber) {
                        var b = document.createElement("button");
                        b.type = "button";
                        b.className = "owner-fin-page-btn" + (pageNumber === currentPage ? " active" : "");
                        b.textContent = String(pageNumber);
                        b.addEventListener("click", function () {
                            currentPage = pageNumber;
                            applyTableFilter();
                        });
                        pageBtns.appendChild(b);
                    })(p);
                }
            }
        }

        [searchInput, typeSelect, statusSelect, fromInput, toInput].forEach(function (ctrl) {
            if (!ctrl) return;
            ctrl.addEventListener("input", function () { currentPage = 1; applyTableFilter(); });
            ctrl.addEventListener("change", function () { currentPage = 1; applyTableFilter(); });
        });

        if (resetBtn) {
            resetBtn.addEventListener("click", function () {
                if (searchInput) searchInput.value = "";
                if (typeSelect) typeSelect.value = "";
                if (statusSelect) statusSelect.value = "";
                if (fromInput) fromInput.value = "";
                if (toInput) toInput.value = "";
                currentPage = 1;
                applyTableFilter();
            });
        }

        applyTableFilter();
    }

    // ═══════════════════ PERFORMANCE REPORTS MODULE (Prefix: perf*) ═══════════════════
    var perfCharts = [];
    var perfSectionInitialized = false;

    function destroyPerfCharts() {
        perfCharts.forEach(function (c) {
            try { c.destroy(); } catch (_) { /* ignore */ }
        });
        perfCharts = [];
    }

    function perfNum(row, camel, pascal) {
        var raw = row[camel] !== undefined ? row[camel] : row[pascal];
        return Number(raw) || 0;
    }

    function perfLabel(row) {
        return row.label || row.Label || "-";
    }

    function perfAxisScales() {
        return {
            x: { grid: { color: "rgba(148, 163, 184, 0.08)" }, ticks: { color: "#94A3B8" } },
            y: {
                grid: { color: "rgba(148, 163, 184, 0.08)" },
                ticks: { color: "#94A3B8", callback: function (v) { return numberFmt.format(Number(v)); } }
            }
        };
    }

    function renderPerformanceCharts(root, data, startMonth, endMonth, revSeriesMode) {
        if (typeof window.Chart === "undefined" || !data) return;
        destroyPerfCharts();

        var sMonth = startMonth || 1;
        var eMonth = endMonth || 12;
        var mode = revSeriesMode || "monthly";

        var monthlyRaw = getPath(data, "monthlyReports");
        monthlyRaw = Array.isArray(monthlyRaw) ? monthlyRaw : [];

        var yearlyRaw = getPath(data, "yearlyReports");
        yearlyRaw = Array.isArray(yearlyRaw) ? yearlyRaw : [];

        var filteredMonthly = monthlyRaw.filter(function (r) {
            var m = Number(r.month !== undefined ? r.month : r.Month);
            return isNaN(m) || (m >= sMonth && m <= eMonth);
        });

        var commonTooltip = {
            backgroundColor: "rgba(5, 11, 24, 0.95)",
            titleColor: "#F8FAFC",
            bodyColor: "#E2E8F0",
            borderColor: "rgba(0, 229, 255, 0.35)",
            borderWidth: 1,
            padding: 11
        };

        function currencyTooltip() {
            return Object.assign({}, commonTooltip, {
                callbacks: {
                    label: function (ctx) {
                        return " " + ctx.dataset.label + ": " + currencyFmt.format(Number(ctx.parsed.y));
                    }
                }
            });
        }

        function prepare(id) {
            var canvas = document.getElementById(id);
            if (!canvas) return null;
            var existing = window.Chart.getChart ? window.Chart.getChart(canvas) : null;
            if (existing) existing.destroy();
            return canvas;
        }

        // 1) Revenue Performance Area/Line Chart
        var revCanvas = prepare("ownerPerfRevenueChart");
        if (revCanvas) {
            var sourceList = mode === "yearly" ? yearlyRaw : filteredMonthly;
            var revLabels = sourceList.map(perfLabel);
            var revValues = sourceList.map(function (r) { return perfNum(r, "revenue", "Revenue"); });

            if (revLabels.length === 0) {
                setCanvasEmptyState(revCanvas, true, "ยังไม่มีข้อมูลรายได้สำหรับช่วงเวลานี้");
            } else {
                setCanvasEmptyState(revCanvas, false);
                perfCharts.push(new window.Chart(revCanvas, {
                    type: "line",
                    data: {
                        labels: revLabels,
                        datasets: [{
                            label: mode === "yearly" ? "รายได้รวมรายปี (THB)" : "รายได้สุทธิรายเดือน (THB)",
                            data: revValues,
                            borderColor: "#00E5FF",
                            backgroundColor: "rgba(0, 229, 255, 0.18)",
                            borderWidth: 2.5,
                            pointBackgroundColor: "#00E5FF",
                            pointBorderColor: "#050B18",
                            pointRadius: 4,
                            pointHoverRadius: 6,
                            tension: 0.36,
                            fill: true
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { labels: { color: "#CBD5E1", usePointStyle: true } },
                            tooltip: currencyTooltip()
                        },
                        scales: perfAxisScales()
                    }
                }));
            }
        }

        // 2) Profit & Expense Analysis Bar Chart
        var barCanvas = prepare("ownerPerfProfitExpenseBarChart");
        if (barCanvas) {
            var barLabels = filteredMonthly.map(perfLabel);

            if (barLabels.length === 0) {
                setCanvasEmptyState(barCanvas, true, "ยังไม่มีข้อมูลเปรียบเทียบรายเดือน");
            } else {
                setCanvasEmptyState(barCanvas, false);
                perfCharts.push(new window.Chart(barCanvas, {
                    type: "bar",
                    data: {
                        labels: barLabels,
                        datasets: [
                            {
                                label: "Revenue (THB)",
                                data: filteredMonthly.map(function (r) { return perfNum(r, "revenue", "Revenue"); }),
                                backgroundColor: "rgba(0, 229, 255, 0.75)",
                                borderColor: "#00E5FF",
                                borderWidth: 1,
                                borderRadius: 5
                            },
                            {
                                label: "Expenses (THB)",
                                data: filteredMonthly.map(function (r) { return perfNum(r, "expense", "Expense"); }),
                                backgroundColor: "rgba(245, 158, 11, 0.75)",
                                borderColor: "#F59E0B",
                                borderWidth: 1,
                                borderRadius: 5
                            },
                            {
                                label: "Net Profit (THB)",
                                data: filteredMonthly.map(function (r) { return perfNum(r, "profit", "Profit"); }),
                                backgroundColor: "rgba(16, 185, 129, 0.75)",
                                borderColor: "#10B981",
                                borderWidth: 1,
                                borderRadius: 5
                            }
                        ]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        interaction: { mode: "index", intersect: false },
                        plugins: {
                            legend: { display: false },
                            tooltip: currencyTooltip()
                        },
                        scales: perfAxisScales()
                    }
                }));
            }
        }

        // 3) Occupancy Donut Chart
        var occCanvas = prepare("ownerPerfOccupancyDonut");
        if (occCanvas) {
            var totalOccNights = filteredMonthly.reduce(function (sum, r) {
                return sum + perfNum(r, "occupiedRoomNights", "OccupiedRoomNights");
            }, 0);
            var totalAvailNights = filteredMonthly.reduce(function (sum, r) {
                return sum + perfNum(r, "availableRoomNights", "AvailableRoomNights");
            }, 0);
            var vacantNights = Math.max(0, totalAvailNights - totalOccNights);

            if (totalAvailNights <= 0) {
                setCanvasEmptyState(occCanvas, true, "ไม่มีข้อมูลห้องพัก");
            } else {
                setCanvasEmptyState(occCanvas, false);
                perfCharts.push(new window.Chart(occCanvas, {
                    type: "doughnut",
                    data: {
                        labels: ["ห้องที่มีผู้เข้าพักจริง (Occupied)", "ห้องว่าง (Available)"],
                        datasets: [{
                            data: [totalOccNights, vacantNights],
                            backgroundColor: ["#00E5FF", "#1E293B"],
                            borderColor: "#0E172E",
                            borderWidth: 3,
                            hoverOffset: 5
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        cutout: "72%",
                        plugins: {
                            legend: { display: false },
                            tooltip: Object.assign({}, commonTooltip, {
                                callbacks: {
                                    label: function (ctx) {
                                        var nights = Number(ctx.parsed) || 0;
                                        var pct = totalAvailNights > 0 ? (nights * 100 / totalAvailNights) : 0;
                                        return " " + ctx.label + ": " + numberFmt.format(nights) + " คืน (" + percentFmt.format(pct) + "%)";
                                    }
                                }
                            })
                        }
                    }
                }));
            }
        }

        // 4) Yearly Performance Comparison Chart
        var yrCanvas = prepare("ownerPerfYearlyComparisonChart");
        if (yrCanvas) {
            var yrLabels = yearlyRaw.map(function (r) { return "ปี " + (r.label || r.Label || r.year || r.Year); });

            if (yrLabels.length === 0) {
                setCanvasEmptyState(yrCanvas, true, "ยังไม่มีข้อมูลเปรียบเทียบรายปี");
            } else {
                setCanvasEmptyState(yrCanvas, false);
                perfCharts.push(new window.Chart(yrCanvas, {
                    type: "bar",
                    data: {
                        labels: yrLabels,
                        datasets: [
                            {
                                label: "Annual Revenue",
                                data: yearlyRaw.map(function (r) { return perfNum(r, "revenue", "Revenue"); }),
                                backgroundColor: "rgba(37, 99, 235, 0.8)",
                                borderColor: "#2563EB",
                                borderWidth: 1,
                                borderRadius: 6
                            },
                            {
                                label: "Annual Expenses",
                                data: yearlyRaw.map(function (r) { return perfNum(r, "expense", "Expense"); }),
                                backgroundColor: "rgba(245, 158, 11, 0.8)",
                                borderColor: "#F59E0B",
                                borderWidth: 1,
                                borderRadius: 6
                            },
                            {
                                label: "Annual Profit",
                                data: yearlyRaw.map(function (r) { return perfNum(r, "profit", "Profit"); }),
                                backgroundColor: "rgba(139, 92, 246, 0.8)",
                                borderColor: "#8B5CF6",
                                borderWidth: 1,
                                borderRadius: 6
                            }
                        ]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        interaction: { mode: "index", intersect: false },
                        plugins: {
                            legend: { labels: { color: "#CBD5E1", usePointStyle: true } },
                            tooltip: currencyTooltip()
                        },
                        scales: perfAxisScales()
                    }
                }));
            }
        }
    }

    function initPerformanceReportsFeatures(root, data) {
        if (root.getAttribute("data-owner-page") !== "performance") return;

        var currentRevSeries = "monthly";
        var startMonthSel = root.querySelector("[data-perf-start-month]");
        var endMonthSel = root.querySelector("[data-perf-end-month]");
        var customBar = root.querySelector("[data-perf-custom-bar]");
        var yearlySec = root.querySelector('[data-perf-section="yearly"]');

        function readRange() {
            return {
                from: startMonthSel ? Number(startMonthSel.value) || 1 : 1,
                to: endMonthSel ? Number(endMonthSel.value) || 12 : 12
            };
        }

        var initial = readRange();
        renderPerformanceCharts(root, data, initial.from, initial.to, currentRevSeries);

        if (perfSectionInitialized) return;
        perfSectionInitialized = true;

        function showAllMonthRows() {
            root.querySelectorAll("tr[data-perf-month-row]").forEach(function (tr) {
                tr.style.display = "";
            });
        }

        function syncRevSeriesButtons() {
            root.querySelectorAll("[data-perf-rev-series]").forEach(function (b) {
                b.classList.toggle("active", b.getAttribute("data-perf-rev-series") === currentRevSeries);
            });
        }

        function applyCustomMonthFilter(fromM, toM) {
            var minM = Math.min(fromM, toM);
            var maxM = Math.max(fromM, toM);

            root.querySelectorAll("tr[data-perf-month-row]").forEach(function (tr) {
                var m = Number(tr.getAttribute("data-perf-month-row"));
                tr.style.display = (m >= minM && m <= maxM) ? "" : "none";
            });

            renderPerformanceCharts(root, lastLoadedData, minM, maxM, currentRevSeries);
        }

        // สลับโหมด Monthly / Yearly / Custom Range (reset สถานะที่ค้างจาก Custom Range ทุกครั้ง)
        root.querySelectorAll("[data-perf-view-mode]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                var mode = btn.getAttribute("data-perf-view-mode");
                root.querySelectorAll("[data-perf-view-mode]").forEach(function (b) {
                    b.classList.toggle("active", b === btn);
                });
                if (customBar) customBar.classList.toggle("is-open", mode === "custom");

                if (mode === "custom") return; // รอให้กดปุ่ม Apply

                if (startMonthSel) startMonthSel.value = "1";
                if (endMonthSel) endMonthSel.value = "12";
                showAllMonthRows();
                currentRevSeries = mode === "yearly" ? "yearly" : "monthly";
                syncRevSeriesButtons();
                renderPerformanceCharts(root, lastLoadedData, 1, 12, currentRevSeries);

                if (mode === "yearly" && yearlySec) {
                    yearlySec.scrollIntoView({ behavior: "smooth", block: "start" });
                }
            });
        });

        // ปุ่มสลับกราฟรายได้ (Monthly vs Yearly)
        root.querySelectorAll("[data-perf-rev-series]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                currentRevSeries = btn.getAttribute("data-perf-rev-series") || "monthly";
                syncRevSeriesButtons();
                var r = readRange();
                renderPerformanceCharts(root, lastLoadedData, r.from, r.to, currentRevSeries);
            });
        });

        var applyBtn = root.querySelector("[data-perf-apply-range]");
        if (applyBtn) {
            applyBtn.addEventListener("click", function () {
                var r = readRange();
                applyCustomMonthFilter(r.from, r.to);
            });
        }

        var resetBtn = root.querySelector("[data-perf-reset-range]");
        if (resetBtn) {
            resetBtn.addEventListener("click", function () {
                if (startMonthSel) startMonthSel.value = "1";
                if (endMonthSel) endMonthSel.value = "12";
                applyCustomMonthFilter(1, 12);
            });
        }
    }

    // ═══════════════════ STAFF OVERVIEW MODULE (Prefix: staff*) ═══════════════════
    var staffCharts = [];
    var staffDirectoryInitialized = false;

    function destroyStaffCharts() {
        staffCharts.forEach(function (c) {
            try { c.destroy(); } catch (_) { /* ignore */ }
        });
        staffCharts = [];
    }

    function renderStaffCharts(root, data) {
        if (typeof window.Chart === "undefined" || !data) return;
        destroyStaffCharts();

        var totalStaff = Number(getPath(data, "totalStaff")) || 0;
        var activeStaff = Number(getPath(data, "activeStaff")) || 0;
        var inactiveStaff = Number(getPath(data, "inactiveStaff")) || 0;
        var suspendedRaw = getPath(data, "suspendedStaff");
        var resignedRaw = getPath(data, "resignedStaff");

        var suspendedCount = suspendedRaw !== null && suspendedRaw !== undefined ? Number(suspendedRaw) : inactiveStaff;
        var resignedCount = resignedRaw !== null && resignedRaw !== undefined ? Number(resignedRaw) : 0;

        // 1) Section 5: Staff Status Distribution Donut Chart (Green / Amber / Red)
        var statusCanvas = document.getElementById("ownerStaffStatusDonutChart");
        if (statusCanvas) {
            var existingDonut = window.Chart.getChart ? window.Chart.getChart(statusCanvas) : null;
            if (existingDonut) existingDonut.destroy();

            if (totalStaff <= 0) {
                setCanvasEmptyState(statusCanvas, true, "ยังไม่มีข้อมูลบัญชีพนักงาน");
            } else {
                setCanvasEmptyState(statusCanvas, false);
                staffCharts.push(new window.Chart(statusCanvas, {
                    type: "doughnut",
                    data: {
                        labels: [
                            "Active (ปฏิบัติงาน)",
                            suspendedRaw !== null && suspendedRaw !== undefined ? "Suspended (ระงับสิทธิ์)" : "Inactive / Suspended (ปิดสิทธิ์)",
                            "Resigned (ลาออก)"
                        ],
                        datasets: [{
                            data: [activeStaff, suspendedCount, resignedCount],
                            backgroundColor: ["#10B981", "#F59E0B", "#EF4444"],
                            borderColor: "#0E172E",
                            borderWidth: 3,
                            hoverOffset: 6
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        cutout: "72%",
                        plugins: {
                            legend: { display: false },
                            tooltip: {
                                backgroundColor: "rgba(5, 11, 24, 0.95)",
                                borderColor: "rgba(0, 229, 255, 0.35)",
                                borderWidth: 1,
                                padding: 10,
                                callbacks: {
                                    label: function (ctx) {
                                        var count = Number(ctx.parsed) || 0;
                                        var pct = totalStaff > 0 ? (count * 100 / totalStaff) : 0;
                                        return " " + ctx.label + ": " + numberFmt.format(count) + " คน (" + percentFmt.format(pct) + "%)";
                                    }
                                }
                            }
                        }
                    }
                }));
            }
        }

        // 2) Section 6: Employee by Position (Horizontal Bar Chart)
        var posCanvas = document.getElementById("ownerStaffPositionBarChart");
        if (posCanvas) {
            var existingBar = window.Chart.getChart ? window.Chart.getChart(posCanvas) : null;
            if (existingBar) existingBar.destroy();

            var roles = getPath(data, "staffByRole");
            roles = Array.isArray(roles) ? roles : [];

            if (roles.length === 0) {
                setCanvasEmptyState(posCanvas, true, "ยังไม่มีข้อมูลตำแหน่งพนักงาน");
            } else {
                setCanvasEmptyState(posCanvas, false);
                var roleLabels = roles.map(function (r) {
                    var code = r.roleCode || r.RoleCode || "";
                    var name = r.roleName || r.RoleName || "";
                    return name && name !== code ? code + " (" + name + ")" : code;
                });
                var roleCounts = roles.map(function (r) {
                    return Number(r.total !== undefined ? r.total : r.Total) || 0;
                });

                staffCharts.push(new window.Chart(posCanvas, {
                    type: "bar",
                    data: {
                        labels: roleLabels,
                        datasets: [{
                            label: "จำนวนพนักงาน (คน)",
                            data: roleCounts,
                            backgroundColor: [
                                "rgba(0, 229, 255, 0.78)",
                                "rgba(37, 99, 235, 0.78)",
                                "rgba(139, 92, 246, 0.78)",
                                "rgba(16, 185, 129, 0.78)",
                                "rgba(245, 158, 11, 0.78)"
                            ],
                            borderColor: "#00E5FF",
                            borderWidth: 1,
                            borderRadius: 6
                        }]
                    },
                    options: {
                        indexAxis: "y",
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { display: false },
                            tooltip: {
                                backgroundColor: "rgba(5, 11, 24, 0.95)",
                                borderColor: "rgba(0, 229, 255, 0.35)",
                                borderWidth: 1,
                                padding: 10,
                                callbacks: {
                                    label: function (ctx) {
                                        var c = Number(ctx.parsed.x) || 0;
                                        var pct = totalStaff > 0 ? (c * 100 / totalStaff) : 0;
                                        return " จำนวน: " + numberFmt.format(c) + " คน (" + percentFmt.format(pct) + "% ของพนักงานทั้งหมด)";
                                    }
                                }
                            }
                        },
                        scales: {
                            x: {
                                beginAtZero: true,
                                grid: { color: "rgba(148, 163, 184, 0.08)" },
                                ticks: { color: "#94A3B8", precision: 0 }
                            },
                            y: {
                                grid: { display: false },
                                ticks: { color: "#E2E8F0", font: { weight: "600" } }
                            }
                        }
                    }
                }));
            }
        }
    }

    function initStaffOverviewFeatures(root, data) {
        if (root.getAttribute("data-owner-page") !== "staff") return;

        renderStaffCharts(root, data);

        if (staffDirectoryInitialized) return;
        staffDirectoryInitialized = true;

        var tbody = root.querySelector("[data-staff-dir-tbody]");
        if (!tbody) return;

        var allRows = Array.prototype.slice.call(tbody.querySelectorAll("tr[data-staff-row]"));
        var searchInput = root.querySelector("[data-staff-search]");
        var roleFilter = root.querySelector("[data-staff-role-filter]");
        var statusFilter = root.querySelector("[data-staff-status-filter]");
        var sortSelect = root.querySelector("[data-staff-sort]");
        var resetBtn = root.querySelector("[data-staff-reset]");
        var countBadge = root.querySelector("[data-staff-dir-count]");
        var pageInfo = root.querySelector("[data-staff-page-info]");
        var pageBtns = root.querySelector("[data-staff-page-btns]");

        var pageSize = 8;
        var currentPage = 1;

        function applyDirectoryFilterAndSort() {
            var q = (searchInput && searchInput.value ? searchInput.value : "").trim().toLowerCase();
            var rVal = (roleFilter && roleFilter.value ? roleFilter.value : "").trim();
            var sVal = (statusFilter && statusFilter.value ? statusFilter.value : "").trim();
            var sortMode = sortSelect && sortSelect.value ? sortSelect.value : "id-asc";

            var matched = allRows.filter(function (row) {
                var idStr = "emp-" + (row.getAttribute("data-emp-id") || "").toLowerCase();
                var nameStr = (row.getAttribute("data-emp-name") || "").toLowerCase();
                var userStr = (row.getAttribute("data-emp-username") || "").toLowerCase();
                var rolesStr = row.getAttribute("data-emp-roles") || "";
                var statusStr = row.getAttribute("data-emp-status") || "";

                if (q && idStr.indexOf(q) === -1 && nameStr.indexOf(q) === -1 && userStr.indexOf(q) === -1 && rolesStr.toLowerCase().indexOf(q) === -1) {
                    return false;
                }
                if (rVal && rolesStr.split(",").map(function (s) { return s.trim(); }).indexOf(rVal) === -1) {
                    return false;
                }
                if (sVal && statusStr !== sVal) {
                    return false;
                }
                return true;
            });

            // Sort rows
            matched.sort(function (a, b) {
                if (sortMode === "name-asc" || sortMode === "name-desc") {
                    var na = (a.getAttribute("data-emp-name") || "").toLowerCase();
                    var nb = (b.getAttribute("data-emp-name") || "").toLowerCase();
                    return sortMode === "name-asc" ? na.localeCompare(nb, "th") : nb.localeCompare(na, "th");
                }
                if (sortMode === "role-asc") {
                    var ra = (a.getAttribute("data-emp-roles") || "").toLowerCase();
                    var rb = (b.getAttribute("data-emp-roles") || "").toLowerCase();
                    return ra.localeCompare(rb, "th");
                }
                var ida = Number(a.getAttribute("data-emp-id")) || 0;
                var idb = Number(b.getAttribute("data-emp-id")) || 0;
                return ida - idb;
            });

            matched.forEach(function (row) { tbody.appendChild(row); });

            var totalPages = Math.max(1, Math.ceil(matched.length / pageSize));
            if (currentPage > totalPages) currentPage = totalPages;

            var startIdx = (currentPage - 1) * pageSize;
            var endIdx = startIdx + pageSize;

            allRows.forEach(function (r) { r.style.display = "none"; });
            matched.slice(startIdx, endIdx).forEach(function (r) { r.style.display = ""; });

            var dynamicEmpty = tbody.querySelector("[data-staff-empty-dynamic]");
            if (matched.length === 0 && allRows.length > 0) {
                if (!dynamicEmpty) {
                    dynamicEmpty = tbody.insertRow();
                    dynamicEmpty.setAttribute("data-staff-empty-dynamic", "true");
                    var td = dynamicEmpty.insertCell();
                    td.colSpan = 5;
                    td.className = "owner-empty-cell";
                    td.textContent = "ไม่พบบัญชีพนักงานที่ตรงกับเงื่อนไขการค้นหา";
                }
                dynamicEmpty.style.display = "";
            } else if (dynamicEmpty) {
                dynamicEmpty.style.display = "none";
            }

            if (countBadge) {
                countBadge.textContent = "แสดง " + matched.length + " จาก " + allRows.length + " บัญชี";
            }
            if (pageInfo) {
                pageInfo.textContent = matched.length === 0
                    ? "ไม่พบรายการ"
                    : "แสดงลำดับที่ " + (startIdx + 1) + " – " + Math.min(endIdx, matched.length) + " (หน้า " + currentPage + "/" + totalPages + ")";
            }

            if (pageBtns) {
                pageBtns.replaceChildren();
                for (var p = 1; p <= totalPages; p++) {
                    (function (pageNum) {
                        var btn = document.createElement("button");
                        btn.type = "button";
                        btn.className = "owner-fin-page-btn" + (pageNum === currentPage ? " active" : "");
                        btn.textContent = String(pageNum);
                        btn.addEventListener("click", function () {
                            currentPage = pageNum;
                            applyDirectoryFilterAndSort();
                        });
                        pageBtns.appendChild(btn);
                    })(p);
                }
            }
        }

        [searchInput, roleFilter, statusFilter, sortSelect].forEach(function (el) {
            if (!el) return;
            el.addEventListener("input", function () { currentPage = 1; applyDirectoryFilterAndSort(); });
            el.addEventListener("change", function () { currentPage = 1; applyDirectoryFilterAndSort(); });
        });

        if (resetBtn) {
            resetBtn.addEventListener("click", function () {
                if (searchInput) searchInput.value = "";
                if (roleFilter) roleFilter.value = "";
                if (statusFilter) statusFilter.value = "";
                if (sortSelect) sortSelect.value = "id-asc";
                currentPage = 1;
                applyDirectoryFilterAndSort();
            });
        }

        applyDirectoryFilterAndSort();
    }

    // ───────────── Render All ─────────────
    function renderAll(root, data) {
        lastLoadedData = data;
        renderBindings(root, data);
        renderLists(root, data);
        renderTables(root, data);
        renderCharts(root, data);
        initPerformanceReportsFeatures(root, data);
        initStaffOverviewFeatures(root, data);
    }

    // ───────────── Status / Error ─────────────
    function showError(root, message) {
        var box = root.querySelector("[data-owner-error]");
        if (!box) {
            box = document.createElement("div");
            box.setAttribute("data-owner-error", "");
            box.setAttribute("role", "alert");
            box.className = "owner-error-banner";
            root.prepend(box);
        }
        box.textContent = message;
    }

    function setLoading(root, isLoading) {
        root.setAttribute("aria-busy", isLoading ? "true" : "false");
        root.classList.toggle("is-loading", isLoading);
    }

    // ───────────── Filters (GET ผ่าน query string) ─────────────
    function initFilters() {
        var yearSel = document.querySelector("[data-owner-year]");
        var monthSel = document.querySelector("[data-owner-month]");
        var params = new URLSearchParams(window.location.search);

        if (yearSel && params.get("year")) yearSel.value = params.get("year");
        if (monthSel && params.get("month")) monthSel.value = params.get("month");

        function apply() {
            var next = new URLSearchParams(window.location.search);
            if (yearSel && yearSel.value) next.set("year", yearSel.value); else next.delete("year");
            if (monthSel && monthSel.value) next.set("month", monthSel.value); else next.delete("month");
            // เปลี่ยนปีในหน้า Financial Overview ให้ใช้ preset = year
            if (document.querySelector('[data-owner-page="financial"]')) {
                next.set("preset", "year");
                next.delete("startDate");
                next.delete("endDate");
            }
            window.location.assign(window.location.pathname + (next.toString() ? "?" + next.toString() : ""));
        }

        if (yearSel) yearSel.addEventListener("change", apply);
        if (monthSel) monthSel.addEventListener("change", apply);
    }

    // ───────────── Bootstrap ─────────────
    async function init() {
        var root = document.querySelector("[data-owner-page]");
        if (!root) return;

        var apiUrl = root.getAttribute("data-api-url");
        if (!apiUrl) {
            showError(root, "ไม่พบ data-api-url ของหน้านี้");
            return;
        }

        initFilters();
        initFinancialOverviewFeatures(root);

        var url = apiUrl + window.location.search;

        setLoading(root, true);
        try {
            await ensureChartJsLoaded();
            var data = await fetchJson(url);
            renderAll(root, data);
        } catch (err) {
            showError(root, err && err.message ? err.message : "เกิดข้อผิดพลาดในการโหลดข้อมูล");
        } finally {
            setLoading(root, false);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();