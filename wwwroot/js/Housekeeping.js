/* =====================================================================
 - StayMate - Housekeeping Portal Script (wwwroot/js/Housekeeping.js)
 - รองรับครบทุกหน้า: Dashboard, CleaningTasks, RoomStatus, History, TaskDetails
 - ===================================================================== */
(function () {
    'use strict';

    // ---------- Shared Helpers ----------
    function $(sel, ctx) { return (ctx || document).querySelector(sel); }
    function $all(sel, ctx) { return Array.prototype.slice.call((ctx || document).querySelectorAll(sel)); }

    function getToken() {
        var input = $('input[name="__RequestVerificationToken"]');
        if (input && input.value) return input.value;
        var meta = $('meta[name="csrf-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    function fmtDate(val, appendUnit) {
        if (!val) return '-';
        var d = new Date(val);
        if (isNaN(d.getTime())) return '-';
        var str = d.toLocaleString('th-TH', { dateStyle: 'medium', timeStyle: 'short' });
        return appendUnit ? (str + ' น.') : str;
    }

    async function readJson(res) {
        var ct = res.headers.get('content-type') || '';
        if (ct.indexOf('application/json') !== -1) return res.json();
        return null;
    }

    async function postForm(url, paramsObj) {
        var token = getToken();
        var body = new URLSearchParams();
        Object.keys(paramsObj || {}).forEach(function (k) {
            if (paramsObj[k] !== undefined && paramsObj[k] !== null) {
                body.append(k, String(paramsObj[k]));
            }
        });
        if (token) body.append('__RequestVerificationToken', token);

        var headers = {
            'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
            'X-Requested-With': 'XMLHttpRequest',
            'Accept': 'application/json'
        };
        if (token) headers['RequestVerificationToken'] = token;

        var res = await fetch(url, {
            method: 'POST',
            headers: headers,
            body: body.toString(),
            credentials: 'same-origin'
        });

        var json = await readJson(res);
        if (!json) {
            if (res.redirected || res.status === 401 || res.status === 403) {
                throw new Error('เซสชันหมดอายุหรือไม่มีสิทธิ์ กรุณาเข้าสู่ระบบใหม่');
            }
            throw new Error('เซิร์ฟเวอร์ตอบกลับไม่ถูกต้อง (' + res.status + ')');
        }
        return json;
    }

    var FLASH_KEY = 'hk_flash';
    function setFlash(type, message) {
        try { sessionStorage.setItem(FLASH_KEY, JSON.stringify({ type: type, message: message })); } catch (e) { /* ignore */ }
    }

    function consumeFlash(notifyFn) {
        try {
            var raw = sessionStorage.getItem(FLASH_KEY);
            if (!raw) return;
            sessionStorage.removeItem(FLASH_KEY);
            var f = JSON.parse(raw);
            if (f && f.message) notifyFn(f.type || 'success', f.message);
        } catch (e) { /* ignore */ }
    }

    // =====================================================================
    // MODULE 1: DASHBOARD & CLEANING TASKS (Index.cshtml / CleaningTasks.cshtml)
    // =====================================================================
    (function () {
        var SELECTORS = {
            root: '#hkApp',
            searchRoom: '#searchRoom',
            searchTask: '#searchTaskId',
            floorFilter: '#floorFilter',
            statusFilter: '#statusFilter',
            priorityFilter: '#priorityFilter',
            assignedDateFilter: '#assignedDateFilter',
            sortOrder: '#sortDateOrder',
            dateFrom: '#dateFrom',
            dateTo: '#dateTo',
            clearFilters: '#btnClearFilters',
            refreshDashboardBtn: '#btnRefreshDashboard',
            refreshTasksPageBtn: '#btnRefreshTasksPage',
            mobileFilterToggle: '#btnToggleMobileFilter',
            filterCollapsible: '#hkFilterCollapsible',
            viewSwitchBtn: '[data-hk-view-mode]',
            cardContainer: '#hkCardViewContainer',
            tableContainer: '#hkTableViewContainer',
            cardItem: '.hk-task-item',
            tableItem: '.hk-task-row',
            pageSizeSelect: '#hkPageSizeSelect',
            paginationWrap: '#hkPaginationWrapper',
            prevPageBtn: '#hkPrevPageBtn',
            nextPageBtn: '#hkNextPageBtn',
            pageNumbers: '#hkPageNumbers',
            modal: '#taskModal',
            modalBody: '#taskModalBody',
            modalClose: '.hk-modal-close',
            loading: '#hkLoading',
            toast: '#hkToast'
        };

        var ACTIONS = {
            accept: {
                url: '/AcceptTask',
                title: 'ยืนยันการรับงาน',
                confirm: 'ยืนยันการรับงานทำความสะอาดห้องนี้ใช่หรือไม่?',
                loading: 'กำลังรับงาน...'
            },
            start: {
                url: '/StartTask',
                title: 'เริ่มทำความสะอาด',
                confirm: 'ยืนยันการเริ่มทำความสะอาดห้องนี้ใช่หรือไม่?',
                loading: 'กำลังเปลี่ยนสถานะเป็นกำลังทำความสะอาด...'
            },
            complete: {
                url: '/CompleteTask',
                title: 'ทำความสะอาดเสร็จแล้ว',
                confirm: 'ยืนยันว่าการทำความสะอาดห้องนี้เสร็จสมบูรณ์แล้วใช่หรือไม่?',
                loading: 'กำลังบันทึกและตรวจสอบความพร้อมของห้อง...'
            }
        };

        var root = $(SELECTORS.root) || document.body;
        var BASE = ((root.dataset && root.dataset.baseUrl) || '/Housekeeping').replace(/\/+$/, '');
        var isCleaningTasksPage = root.dataset && root.dataset.page === 'cleaning-tasks';
        var busy = Object.create(null);
        var loadingCount = 0;

        var state = {
            viewMode: 'card',
            currentPage: 1,
            pageSize: 12,
            filteredIds: []
        };

        function ensureToastHost() {
            var host = $(SELECTORS.toast);
            if (host) return host;
            host = document.createElement('div');
            host.id = 'hkToast';
            host.className = 'hk-toast-host';
            document.body.appendChild(host);
            return host;
        }

        function displayNotification(type, message) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                window.Swal.fire({
                    toast: true,
                    position: 'top-end',
                    icon: type === 'error' ? 'error' : type === 'warning' ? 'warning' : 'success',
                    title: message,
                    showConfirmButton: false,
                    timer: 3200,
                    timerProgressBar: true
                });
                return;
            }
            var host = ensureToastHost();
            var el = document.createElement('div');
            el.className = 'hk-toast hk-toast-' + type;
            el.setAttribute('role', type === 'error' ? 'alert' : 'status');
            el.textContent = message;
            host.appendChild(el);
            setTimeout(function () {
                if (el.parentNode) el.parentNode.removeChild(el);
            }, 4000);
        }

        async function confirmDialog(title, text) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                var res = await window.Swal.fire({
                    title: title || 'ยืนยันรายการ',
                    text: text,
                    icon: 'question',
                    showCancelButton: true,
                    confirmButtonColor: '#2563EB',
                    cancelButtonColor: '#64748B',
                    confirmButtonText: 'ยืนยัน',
                    cancelButtonText: 'ยกเลิก'
                });
                return !!res.isConfirmed;
            }
            return window.confirm(text);
        }

        function setLoading(on, text) {
            var el = $(SELECTORS.loading);
            if (!el) {
                el = document.createElement('div');
                el.id = 'hkLoading';
                el.className = 'hk-loading-overlay';
                var box = document.createElement('div');
                box.className = 'hk-loading-box';
                var spinner = document.createElement('div');
                spinner.className = 'hk-spinner';
                var span = document.createElement('span');
                span.className = 'hk-loading-text';
                box.appendChild(spinner);
                box.appendChild(span);
                el.appendChild(box);
                document.body.appendChild(el);
            }
            loadingCount = Math.max(0, loadingCount + (on ? 1 : -1));
            el.classList.toggle('is-open', loadingCount > 0);
            var t = $('.hk-loading-text', el);
            if (t && on && text) t.textContent = text;
        }

        function animateCounters() {
            $all('[data-hk-counter]').forEach(function (el) {
                var target = parseInt(el.getAttribute('data-hk-counter'), 10);
                if (isNaN(target)) return;
                var startTime = null;
                var duration = 600;
                function step(ts) {
                    if (!startTime) startTime = ts;
                    var p = Math.min((ts - startTime) / duration, 1);
                    var eased = 1 - Math.pow(1 - p, 3);
                    el.textContent = Math.round(target * eased).toLocaleString('th-TH');
                    if (p < 1) window.requestAnimationFrame(step);
                }
                window.requestAnimationFrame(step);
            });

            var progressBar = $('#hkProgressBar');
            if (progressBar) {
                var pct = parseInt(progressBar.getAttribute('data-progress') || '0', 10);
                setTimeout(function () { progressBar.style.width = Math.max(0, Math.min(100, pct)) + '%'; }, 100);
            }
        }

        function createActionButtonDom(action, label, taskId, extraClass) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'hk-btn ' + extraClass;
            btn.setAttribute('data-hk-action', action);
            btn.setAttribute('data-task-id', taskId);
            btn.textContent = label;
            return btn;
        }

        function adjustSummaryCounter(statusKey, delta) {
            var map = {
                pending: '#summaryPendingTasks',
                in_progress: '#summaryInProgressTasks',
                done: '#summaryCompletedTasks'
            };
            var sel = map[statusKey];
            if (!sel) return;
            var el = $(sel);
            if (!el) return;
            var current = parseInt((el.textContent || '0').replace(/,/g, ''), 10);
            if (isNaN(current)) current = 0;
            el.textContent = Math.max(0, current + delta).toLocaleString('th-TH');
        }

        function updateTaskDomInPlace(taskId, newStatus, extraData) {
            var targets = $all('[data-task-id="' + taskId + '"].hk-task-item, [data-task-id="' + taskId + '"].hk-task-row');
            if (!targets.length) return false;

            var oldStatus = targets[0].getAttribute('data-raw-status') || targets[0].getAttribute('data-status');

            targets.forEach(function (el) {
                el.setAttribute('data-status', newStatus);
                el.setAttribute('data-raw-status', newStatus);
                el.setAttribute('data-sort-status', newStatus === 'in_progress' ? '1' : newStatus === 'pending' ? '2' : '3');

                if (el.classList.contains('hk-modern-task-card')) {
                    el.classList.remove('hk-task-card-pending', 'hk-task-card-inprogress', 'hk-task-card-completed', 'hk-task-card-maintenance');
                    el.classList.add(newStatus === 'in_progress' ? 'hk-task-card-inprogress' : 'hk-task-card-completed');
                }

                var badge = $('[data-role="status-badge"]', el);
                if (badge) {
                    badge.className = 'hk-status-badge ' + (newStatus === 'in_progress' ? 'hk-badge-in_progress' : 'hk-badge-completed');
                    badge.textContent = newStatus === 'in_progress'
                        ? 'In Progress (กำลังทำความสะอาด)'
                        : 'Completed (ทำความสะอาดเสร็จแล้ว)';
                }

                if (extraData && extraData.assignedToName) {
                    var assigneeEl = $('[data-role="assignee-name"]', el);
                    if (assigneeEl) assigneeEl.textContent = extraData.assignedToName;
                }

                var actionWrap = $('[data-role="action-container"]', el);
                if (actionWrap) {
                    actionWrap.textContent = '';
                    var isTable = el.tagName === 'TR';
                    var sizeCls = isTable ? 'hk-btn-sm' : '';

                    if (newStatus === 'in_progress') {
                        actionWrap.appendChild(createActionButtonDom('details', 'ดูรายละเอียด', taskId, 'hk-btn-outline ' + sizeCls));
                        actionWrap.appendChild(createActionButtonDom('complete', 'ทำความสะอาดเสร็จแล้ว', taskId, 'hk-btn-success ' + (isTable ? sizeCls : 'hk-btn-flex')));
                    } else if (newStatus === 'done') {
                        actionWrap.appendChild(createActionButtonDom('details', 'ดูรายละเอียด', taskId, 'hk-btn-outline ' + (isTable ? sizeCls : 'hk-btn-block')));
                    }
                }
            });

            adjustSummaryCounter(oldStatus, -1);
            adjustSummaryCounter(newStatus, 1);
            applyFilters();
            return true;
        }

        async function updateCleaningStatus(actionKey, taskId) {
            var cfg = ACTIONS[actionKey];
            if (!cfg) return;

            var key = actionKey + ':' + taskId;
            if (busy[key]) return;

            var confirmed = await confirmDialog(cfg.title, cfg.confirm);
            if (!confirmed) return;

            busy[key] = true;
            var buttons = $all('[data-hk-action][data-task-id="' + taskId + '"]');
            buttons.forEach(function (b) { b.disabled = true; });
            setLoading(true, cfg.loading);

            try {
                var json = await postForm(BASE + cfg.url, { taskId: taskId });

                if (json.success) {
                    closeModal();
                    var targetStatus = actionKey === 'start' ? 'in_progress' : actionKey === 'complete' ? 'done' : 'pending';
                    if (isCleaningTasksPage && (actionKey === 'start' || actionKey === 'complete')) {
                        if (updateTaskDomInPlace(taskId, targetStatus, json.data || {})) {
                            displayNotification('success', json.message || 'อัปเดตสถานะงานสำเร็จ');
                            return;
                        }
                    }
                    setFlash('success', json.message || 'ดำเนินการสำเร็จ');
                    window.location.reload();
                    return;
                }

                displayNotification('error', json.message || 'ดำเนินการไม่สำเร็จ');
                buttons.forEach(function (b) { b.disabled = false; });
            } catch (err) {
                displayNotification('error', (err && err.message) || 'เกิดข้อผิดพลาดในการเชื่อมต่อ');
                buttons.forEach(function (b) { b.disabled = false; });
            } finally {
                setLoading(false);
                delete busy[key];
            }
        }

        function switchViewMode(mode) {
            state.viewMode = mode === 'table' ? 'table' : 'card';
            $all(SELECTORS.viewSwitchBtn).forEach(function (btn) {
                btn.classList.toggle('is-active', btn.getAttribute('data-hk-view-mode') === state.viewMode);
            });

            var cardWrap = $(SELECTORS.cardContainer);
            var tableWrap = $(SELECTORS.tableContainer);
            if (cardWrap && tableWrap) {
                cardWrap.hidden = state.viewMode !== 'card';
                tableWrap.hidden = state.viewMode !== 'table';
            }
            renderPagination();
        }

        function matchesItem(el, roomVal, taskVal, floorVal, statusVal, prioVal, assignedDateVal, fromVal, toVal) {
            var rRoom = (el.getAttribute('data-room-number') || '').toLowerCase();
            var rId = el.getAttribute('data-task-id') || '';
            var rFloor = el.getAttribute('data-floor') || '';
            var rStatus = el.getAttribute('data-status') || '';
            var rRawStatus = el.getAttribute('data-raw-status') || '';
            var rPriority = el.getAttribute('data-priority') || '';
            var rDate = el.getAttribute('data-created') || '';

            if (roomVal && rRoom.indexOf(roomVal) === -1 && rId.indexOf(roomVal) === -1) return false;
            if (taskVal && rId.indexOf(taskVal) === -1) return false;
            if (floorVal && rFloor !== floorVal) return false;
            if (statusVal && rStatus !== statusVal && rRawStatus !== statusVal) return false;
            if (prioVal && rPriority.toLowerCase() !== prioVal.toLowerCase()) return false;
            if (assignedDateVal && rDate !== assignedDateVal) return false;
            if (fromVal && rDate && rDate < fromVal) return false;
            if (toVal && rDate && rDate > toVal) return false;
            return true;
        }

        function applyFilters() {
            var roomVal = ($(SELECTORS.searchRoom) ? $(SELECTORS.searchRoom).value : '').trim().toLowerCase();
            var taskVal = ($(SELECTORS.searchTask) ? $(SELECTORS.searchTask).value : '').trim().replace(/^#/, '');
            var floorVal = $(SELECTORS.floorFilter) ? $(SELECTORS.floorFilter).value : '';
            var statusVal = $(SELECTORS.statusFilter) ? $(SELECTORS.statusFilter).value : '';
            var prioVal = $(SELECTORS.priorityFilter) ? $(SELECTORS.priorityFilter).value : '';
            var assignedDateVal = $(SELECTORS.assignedDateFilter) ? $(SELECTORS.assignedDateFilter).value : '';
            var sortOrderVal = $(SELECTORS.sortOrder) ? $(SELECTORS.sortOrder).value : 'asc';
            var fromVal = $(SELECTORS.dateFrom) ? $(SELECTORS.dateFrom).value : '';
            var toVal = $(SELECTORS.dateTo) ? $(SELECTORS.dateTo).value : '';

            if (!isCleaningTasksPage) {
                var rows = $all(SELECTORS.tableItem);
                rows.forEach(function (row) {
                    row.hidden = !matchesItem(row, roomVal, taskVal, floorVal, statusVal, prioVal, assignedDateVal, fromVal, toVal);
                });
                var anyRowVisible = rows.some(function (r) { return !r.hidden; });
                var dashEmpty = $('.hk-empty');
                if (dashEmpty) dashEmpty.hidden = anyRowVisible;
                return;
            }

            var cardItems = $all(SELECTORS.cardItem);
            var tableItems = $all(SELECTORS.tableItem);

            // เรียงตามสถานะที่ต้องทำก่อน -> ความเร่งด่วน -> เวลาที่ได้รับมอบหมาย (รองรับ asc / desc)
            function compareUrgency(a, b) {
                var sA = parseInt(a.getAttribute('data-sort-status') || '9', 10);
                var sB = parseInt(b.getAttribute('data-sort-status') || '9', 10);
                if (sA !== sB) return sA - sB;

                var pA = parseInt(a.getAttribute('data-sort-priority') || '9', 10);
                var pB = parseInt(b.getAttribute('data-sort-priority') || '9', 10);
                if (pA !== pB) return pA - pB;

                var tA = parseInt(a.getAttribute('data-timestamp') || '0', 10);
                var tB = parseInt(b.getAttribute('data-timestamp') || '0', 10);
                return sortOrderVal === 'desc' ? (tB - tA) : (tA - tB);
            }

            var cardParent = $(SELECTORS.cardContainer);
            if (cardParent && cardItems.length > 1) {
                cardItems.sort(compareUrgency).forEach(function (el) { cardParent.appendChild(el); });
            }
            var tableParent = $('#hkTasksTableBody');
            if (tableParent && tableItems.length > 1) {
                tableItems.sort(compareUrgency).forEach(function (el) { tableParent.appendChild(el); });
            }

            var matchedIds = [];
            cardItems.forEach(function (card) {
                if (matchesItem(card, roomVal, taskVal, floorVal, statusVal, prioVal, assignedDateVal, fromVal, toVal)) {
                    matchedIds.push(card.getAttribute('data-task-id'));
                }
            });

            state.filteredIds = matchedIds;
            state.currentPage = 1;
            renderPagination();
        }

        function renderPagination() {
            if (!isCleaningTasksPage) return;

            var total = state.filteredIds.length;
            var pageSize = state.pageSize;
            var totalPages = Math.max(1, Math.ceil(total / pageSize));
            if (state.currentPage > totalPages) state.currentPage = totalPages;

            var startIdx = (state.currentPage - 1) * pageSize;
            var endIdx = Math.min(startIdx + pageSize, total);
            var pageIdsSet = Object.create(null);

            state.filteredIds.slice(startIdx, endIdx).forEach(function (id) {
                pageIdsSet[id] = true;
            });

            $all(SELECTORS.cardItem).forEach(function (card) {
                card.hidden = !pageIdsSet[card.getAttribute('data-task-id')];
            });

            $all(SELECTORS.tableItem).forEach(function (row) {
                row.hidden = !pageIdsSet[row.getAttribute('data-task-id')];
            });

            var startEl = $('#hkShowingStart');
            var endEl = $('#hkShowingEnd');
            var totalEl = $('#hkTotalFilteredCount');
            if (startEl) startEl.textContent = total === 0 ? '0' : String(startIdx + 1);
            if (endEl) endEl.textContent = String(endIdx);
            if (totalEl) totalEl.textContent = String(total);

            var emptyEl = $('#hkTasksEmptyState');
            if (emptyEl) emptyEl.hidden = total > 0;

            var cardWrap = $(SELECTORS.cardContainer);
            var tableWrap = $(SELECTORS.tableContainer);
            if (cardWrap) cardWrap.hidden = total === 0 || state.viewMode !== 'card';
            if (tableWrap) tableWrap.hidden = total === 0 || state.viewMode !== 'table';

            var pagWrap = $(SELECTORS.paginationWrap);
            if (pagWrap) pagWrap.hidden = total === 0;

            var prevBtn = $(SELECTORS.prevPageBtn);
            var nextBtn = $(SELECTORS.nextPageBtn);
            if (prevBtn) prevBtn.disabled = state.currentPage <= 1;
            if (nextBtn) nextBtn.disabled = state.currentPage >= totalPages;

            var numbersHost = $(SELECTORS.pageNumbers);
            if (numbersHost) {
                numbersHost.textContent = '';
                for (var p = 1; p <= totalPages; p++) {
                    var btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'hk-page-num-btn' + (p === state.currentPage ? ' is-active' : '');
                    btn.setAttribute('data-hk-page', String(p));
                    btn.textContent = String(p);
                    numbersHost.appendChild(btn);
                }
            }
        }

        function clearFilters() {
            [
                SELECTORS.searchRoom,
                SELECTORS.searchTask,
                SELECTORS.floorFilter,
                SELECTORS.statusFilter,
                SELECTORS.priorityFilter,
                SELECTORS.assignedDateFilter,
                SELECTORS.dateFrom,
                SELECTORS.dateTo
            ].forEach(function (s) {
                var el = $(s);
                if (el) el.value = '';
            });
            var sortEl = $(SELECTORS.sortOrder);
            if (sortEl) sortEl.value = 'asc';
            applyFilters();
        }

        // ---------- Task Details Modal ----------
        function ensureModal() {
            var modal = $(SELECTORS.modal);
            if (modal) return modal;
            modal = document.createElement('div');
            modal.id = 'taskModal';
            modal.className = 'hk-modal';
            modal.innerHTML =
                '<div class="hk-modal-dialog" role="dialog" aria-modal="true">' +
                '<button type="button" class="hk-modal-close" aria-label="ปิด">&times;</button>' +
                '<div id="taskModalBody"></div></div>';
            document.body.appendChild(modal);
            return modal;
        }

        function openModal() {
            var modal = ensureModal();
            modal.classList.add('is-open');
            modal.removeAttribute('hidden');
            modal.style.display = 'flex';
            document.body.style.overflow = 'hidden';
        }

        function closeModal() {
            var modal = $(SELECTORS.modal);
            if (!modal) return;
            modal.classList.remove('is-open');
            modal.style.display = 'none';
            document.body.style.overflow = '';
        }

        function makeRow(label, value) {
            var row = document.createElement('div');
            row.className = 'hk-detail-row';
            var l = document.createElement('span');
            l.className = 'hk-detail-label';
            l.textContent = label;
            var v = document.createElement('span');
            v.className = 'hk-detail-value';
            v.textContent = (value === null || value === undefined || value === '') ? '-' : String(value);
            row.appendChild(l);
            row.appendChild(v);
            return row;
        }

        function renderDetails(d) {
            var body = $(SELECTORS.modalBody);
            if (!body) return;
            body.textContent = '';

            var title = document.createElement('h3');
            title.className = 'hk-modal-title';
            title.textContent = 'รายละเอียดงาน #' + d.taskId + ' — ห้อง ' + d.roomNumber;
            body.appendChild(title);

            var sub = document.createElement('p');
            sub.className = 'hk-modal-subtitle';
            sub.textContent = 'ชั้น ' + d.floorNo + ' • ' + (d.roomTypeName || 'Standard Room');
            body.appendChild(sub);

            var listWrap = document.createElement('div');
            listWrap.className = 'hk-detail-list';
            listWrap.appendChild(makeRow('หมายเลขห้อง', d.roomNumber));
            listWrap.appendChild(makeRow('ชั้น', 'ชั้น ' + d.floorNo));
            listWrap.appendChild(makeRow('ประเภทห้อง', d.roomTypeName));
            listWrap.appendChild(makeRow('สถานะห้องพัก', d.roomStatusLabel));
            listWrap.appendChild(makeRow('สถานะงานทำความสะอาด', d.statusThaiLabel || d.statusLabel));
            listWrap.appendChild(makeRow('ระดับความเร่งด่วน', d.priorityLabel || d.priority || 'ปกติ'));
            listWrap.appendChild(makeRow('ผู้รับผิดชอบ', d.assignedToName || 'ยังไม่มีผู้รับงาน'));
            listWrap.appendChild(makeRow('หมายเหตุ / ปัญหาห้องพัก', d.note));
            listWrap.appendChild(makeRow('วันที่ได้รับมอบหมาย', fmtDate(d.createdAt, false)));
            listWrap.appendChild(makeRow('เวลาเริ่มทำความสะอาด', fmtDate(d.startedAt, false)));
            listWrap.appendChild(makeRow('เวลาที่เสร็จงาน', fmtDate(d.completedAt, false)));
            if (d.durationMinutes !== null && d.durationMinutes !== undefined) {
                listWrap.appendChild(makeRow('เวลาที่ใช้ทั้งหมด', d.durationMinutes + ' นาที'));
            }
            body.appendChild(listWrap);

            var actions = document.createElement('div');
            actions.className = 'hk-modal-actions';
            if (d.status === 'pending') {
                actions.appendChild(createActionButtonDom('start', 'เริ่มทำความสะอาด', d.taskId, 'hk-btn-warning-solid'));
            }
            if (d.canComplete) {
                actions.appendChild(createActionButtonDom('complete', 'ทำความสะอาดเสร็จแล้ว', d.taskId, 'hk-btn-success'));
            }
            if (actions.childNodes.length) body.appendChild(actions);
        }

        async function showDetails(taskId) {
            ensureModal();
            var body = $(SELECTORS.modalBody);
            if (body) body.textContent = 'กำลังโหลดข้อมูล...';
            openModal();
            setLoading(true, 'กำลังโหลดรายละเอียด...');
            try {
                var res = await fetch(BASE + '/TaskDetails/' + encodeURIComponent(taskId), {
                    headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' },
                    credentials: 'same-origin'
                });
                var json = await readJson(res);
                if (!json || !json.success) {
                    throw new Error((json && json.message) || 'ไม่สามารถโหลดรายละเอียดงานได้');
                }
                renderDetails(json.data);
            } catch (err) {
                closeModal();
                displayNotification('error', (err && err.message) || 'ไม่สามารถโหลดรายละเอียดงานได้');
            } finally {
                setLoading(false);
            }
        }

        function debounce(fn, ms) {
            var t;
            return function () {
                var args = arguments, ctx = this;
                clearTimeout(t);
                t = setTimeout(function () { fn.apply(ctx, args); }, ms);
            };
        }

        function init() {
            consumeFlash(displayNotification);
            animateCounters();

            var pageSizeEl = $(SELECTORS.pageSizeSelect);
            if (pageSizeEl) {
                state.pageSize = parseInt(pageSizeEl.value || '12', 10) || 12;
                pageSizeEl.addEventListener('change', function () {
                    state.pageSize = parseInt(pageSizeEl.value || '12', 10) || 12;
                    state.currentPage = 1;
                    renderPagination();
                });
            }

            document.addEventListener('click', function (e) {
                var actionBtn = e.target.closest('[data-hk-action]');
                if (actionBtn) {
                    var action = actionBtn.getAttribute('data-hk-action');
                    var id = actionBtn.getAttribute('data-task-id');
                    if (!id) return;
                    e.preventDefault();
                    if (action === 'details') showDetails(id);
                    else updateCleaningStatus(action, id);
                    return;
                }

                var viewBtn = e.target.closest(SELECTORS.viewSwitchBtn);
                if (viewBtn) {
                    e.preventDefault();
                    switchViewMode(viewBtn.getAttribute('data-hk-view-mode'));
                    return;
                }

                var pageBtn = e.target.closest('[data-hk-page]');
                if (pageBtn) {
                    e.preventDefault();
                    state.currentPage = parseInt(pageBtn.getAttribute('data-hk-page'), 10) || 1;
                    renderPagination();
                    return;
                }

                if (e.target.closest(SELECTORS.modalClose)) {
                    e.preventDefault();
                    closeModal();
                    return;
                }

                var modal = $(SELECTORS.modal);
                if (modal && e.target === modal) closeModal();
            });

            var prevBtn = $(SELECTORS.prevPageBtn);
            if (prevBtn) {
                prevBtn.addEventListener('click', function () {
                    if (state.currentPage > 1) {
                        state.currentPage--;
                        renderPagination();
                    }
                });
            }

            var nextBtn = $(SELECTORS.nextPageBtn);
            if (nextBtn) {
                nextBtn.addEventListener('click', function () {
                    state.currentPage++;
                    renderPagination();
                });
            }

            var mobileToggle = $(SELECTORS.mobileFilterToggle);
            var filterBox = $(SELECTORS.filterCollapsible);
            if (mobileToggle && filterBox) {
                mobileToggle.addEventListener('click', function () {
                    filterBox.classList.toggle('is-collapsed-mobile');
                });
            }

            // ปุ่ม Refresh หน้า Dashboard
            var refreshDashBtn = $(SELECTORS.refreshDashboardBtn);
            if (refreshDashBtn) {
                refreshDashBtn.addEventListener('click', function () {
                    setLoading(true, 'กำลังอัปเดตข้อมูล Dashboard...');
                    window.location.reload();
                });
            }

            // ปุ่ม Refresh หน้า Cleaning Tasks
            var refreshTasksBtn = $(SELECTORS.refreshTasksPageBtn);
            if (refreshTasksBtn) {
                refreshTasksBtn.addEventListener('click', function () {
                    setLoading(true, 'กำลังรีเฟรชรายการงาน...');
                    window.location.reload();
                });
            }

            var debouncedFilter = debounce(applyFilters, 150);
            [SELECTORS.searchRoom, SELECTORS.searchTask].forEach(function (s) {
                var el = $(s);
                if (el) el.addEventListener('input', debouncedFilter);
            });

            [
                SELECTORS.floorFilter,
                SELECTORS.statusFilter,
                SELECTORS.priorityFilter,
                SELECTORS.assignedDateFilter,
                SELECTORS.sortOrder,
                SELECTORS.dateFrom,
                SELECTORS.dateTo
            ].forEach(function (s) {
                var el = $(s);
                if (el) el.addEventListener('change', applyFilters);
            });

            var clearBtn = $(SELECTORS.clearFilters);
            if (clearBtn) clearBtn.addEventListener('click', function (e) { e.preventDefault(); clearFilters(); });

            applyFilters();
        }

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', init);
        } else {
            init();
        }
    })();

    // =====================================================================
    // MODULE 2: ROOM STATUS MANAGEMENT (Views/Housekeeping/RoomStatus.cshtml)
    // =====================================================================
    (function () {
        var root = $('#hkApp');
        if (!root || root.getAttribute('data-page') !== 'room-status') return;

        var BASE = (root.getAttribute('data-base-url') || '/Housekeeping').replace(/\/+$/, '');
        var UPDATE_URL = root.getAttribute('data-update-room-url') || (BASE + '/UpdateRoomStatusWorkflow');
        var isSubmitting = false;

        function notify(type, message) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                window.Swal.fire({
                    icon: type === 'error' ? 'error' : 'success',
                    title: type === 'error' ? 'ไม่สามารถดำเนินการได้' : 'ดำเนินการสำเร็จ',
                    text: message,
                    confirmButtonColor: '#2563EB'
                });
                return;
            }
            window.alert(message);
        }

        async function confirmAction(title, text) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                var res = await window.Swal.fire({
                    title: title,
                    text: text,
                    icon: 'question',
                    showCancelButton: true,
                    confirmButtonColor: '#2563EB',
                    cancelButtonColor: '#64748B',
                    confirmButtonText: 'ยืนยัน',
                    cancelButtonText: 'ยกเลิก'
                });
                return !!res.isConfirmed;
            }
            return window.confirm(text);
        }

        function applyRoomFilters() {
            var searchVal = ($('#rsSearchRoom') ? $('#rsSearchRoom').value : '').trim().toLowerCase();
            var floorVal = $('#rsFloorFilter') ? $('#rsFloorFilter').value : '';
            var typeVal = $('#rsTypeFilter') ? $('#rsTypeFilter').value : '';
            var statusVal = $('#rsStatusFilter') ? $('#rsStatusFilter').value : '';
            var sortBy = $('#rsSortBy') ? $('#rsSortBy').value : 'room_asc';

            var totalVisibleRooms = 0;

            $all('[data-floor-section]').forEach(function (floorSec) {
                var floorNo = floorSec.getAttribute('data-floor-section');
                var grid = $('[data-floor-grid="' + floorNo + '"]', floorSec);
                var cards = $all('.hk-rs-room-card', grid);

                cards.sort(function (a, b) {
                    var numA = a.getAttribute('data-room-number') || '';
                    var numB = b.getAttribute('data-room-number') || '';
                    if (sortBy === 'room_desc') {
                        return numB.localeCompare(numA, undefined, { numeric: true });
                    }
                    if (sortBy === 'status_priority') {
                        var ordA = parseInt(a.getAttribute('data-sort-order') || '9', 10);
                        var ordB = parseInt(b.getAttribute('data-sort-order') || '9', 10);
                        if (ordA !== ordB) return ordA - ordB;
                    }
                    return numA.localeCompare(numB, undefined, { numeric: true });
                });

                var visibleInFloor = 0;
                cards.forEach(function (card) {
                    grid.appendChild(card);

                    var cNum = (card.getAttribute('data-room-number') || '').toLowerCase();
                    var cFloor = card.getAttribute('data-floor') || '';
                    var cType = card.getAttribute('data-room-type') || '';
                    var cState = card.getAttribute('data-management-state') || '';

                    var match = true;
                    if (searchVal && cNum.indexOf(searchVal) === -1) match = false;
                    if (floorVal && cFloor !== floorVal) match = false;
                    if (typeVal && cType !== typeVal) match = false;
                    if (statusVal && cState !== statusVal) match = false;

                    card.hidden = !match;
                    if (match) {
                        visibleInFloor++;
                        totalVisibleRooms++;
                    }
                });

                floorSec.hidden = visibleInFloor === 0;
            });

            var emptyEl = $('#hkRoomStatusEmpty');
            if (emptyEl) emptyEl.hidden = totalVisibleRooms > 0;
        }

        function resetRoomFilters() {
            ['#rsSearchRoom', '#rsFloorFilter', '#rsTypeFilter', '#rsStatusFilter'].forEach(function (s) {
                var el = $(s);
                if (el) el.value = '';
            });
            var sortEl = $('#rsSortBy');
            if (sortEl) sortEl.value = 'room_asc';
            applyRoomFilters();
        }

        function buildAllowedTransitions(managementState) {
            if (managementState === 'pending_cleaning') {
                return [
                    {
                        value: 'start_cleaning',
                        title: 'เริ่มทำความสะอาด (In Progress)',
                        desc: 'รับงานและเปลี่ยนสถานะเป็นกำลังทำความสะอาด บันทึกเวลาเริ่มงานทันที'
                    },
                    {
                        value: 'report_maintenance',
                        title: 'แจ้งปัญหาห้องพัก / อุปกรณ์ชำรุด (Maintenance)',
                        desc: 'พบความเสียหายในห้องพัก ระงับการขายห้องชั่วคราวเพื่อรอซ่อมบำรุง'
                    }
                ];
            }

            if (managementState === 'in_progress') {
                return [
                    {
                        value: 'complete_cleaning',
                        title: 'ทำความสะอาดเสร็จสิ้น — พร้อมเปิดขาย (Available)',
                        desc: 'ผ่านการตรวจสอบ Checklist ความพร้อมของห้อง และเปลี่ยนสถานะห้องเป็น Available'
                    },
                    {
                        value: 'report_maintenance',
                        title: 'พบอุปกรณ์ชำรุด — แจ้งซ่อมบำรุง (Maintenance)',
                        desc: 'ระงับการขายห้องและบันทึกปัญหาที่พบระหว่างทำความสะอาด'
                    }
                ];
            }

            if (managementState === 'maintenance') {
                return [
                    {
                        value: 'resolve_maintenance',
                        title: 'ซ่อมบำรุงเสร็จแล้ว — ส่งเข้าทำความสะอาด (Pending Cleaning)',
                        desc: 'ตามกฎความปลอดภัย ห้องซ่อมเสร็จต้องผ่านการทำความสะอาดและตรวจความพร้อมก่อนเปิดเป็น Available'
                    }
                ];
            }

            if (managementState === 'available') {
                return [
                    {
                        value: 'start_cleaning',
                        title: 'เปิดงานทำความสะอาดซ้ำ / ดูแลความสะอาด (In Progress)',
                        desc: 'เปลี่ยนสถานะห้องเพื่อเข้าทำความสะอาดเพิ่มเติม'
                    },
                    {
                        value: 'report_maintenance',
                        title: 'แจ้งปัญหาอุปกรณ์ชำรุด (Maintenance)',
                        desc: 'พบอุปกรณ์ชำรุดในห้องว่าง เปลี่ยนสถานะเป็นซ่อมบำรุง'
                    }
                ];
            }

            return [];
        }

        function syncModalRequirementState(selectedAction) {
            var checklistBox = $('#rsChecklistBox');
            var noteLabel = $('#rsFormNoteLabel');
            if (selectedAction === 'complete_cleaning') {
                checklistBox.hidden = false;
                noteLabel.innerHTML = 'หมายเหตุเพิ่มเติมหลังตรวจสอบห้อง (ถ้ามี)';
            } else if (selectedAction === 'report_maintenance' || selectedAction === 'resolve_maintenance') {
                checklistBox.hidden = true;
                noteLabel.innerHTML = 'รายละเอียดปัญหา / การซ่อมบำรุง <span class="text-danger">* (จำเป็นต้องระบุ)</span>';
            } else {
                checklistBox.hidden = true;
                noteLabel.innerHTML = 'หมายเหตุ / รายละเอียดเพิ่มเติม';
            }
        }

        function openUpdateRoomModal(card) {
            var modal = $('#rsUpdateModal');
            if (!modal) return;

            var roomId = card.getAttribute('data-room-id');
            var roomNumber = card.getAttribute('data-room-number');
            var floor = card.getAttribute('data-floor');
            var roomType = card.getAttribute('data-room-type');
            var mState = card.getAttribute('data-management-state');
            var stateLabel = card.getAttribute('data-state-label');
            var cleaningLabel = card.getAttribute('data-cleaning-label');

            $('#rsFormRoomId').value = roomId;
            $('#rsModalRoomNumber').textContent = roomNumber;
            $('#rsModalFloorType').textContent = 'ชั้น ' + floor + ' • ' + roomType;
            $('#rsModalCurrentStateLabel').textContent = stateLabel;
            $('#rsModalCleaningLabel').textContent = cleaningLabel;
            $('#rsFormNote').value = '';

            ['#chkBedLinens', '#chkBathroom', '#chkAmenities', '#chkAppliances'].forEach(function (sel) {
                var chk = $(sel);
                if (chk) chk.checked = false;
            });

            var occupiedAlert = $('#rsOccupiedAlert');
            var actionOptionsWrap = $('#rsActionOptionsWrap');
            var noteGroup = $('#rsNoteGroup');
            var submitBtn = $('#rsSubmitBtn');
            var radioHost = $('#rsRadioOptionsList');
            var checklistBox = $('#rsChecklistBox');

            radioHost.textContent = '';
            checklistBox.hidden = true;

            if (mState === 'occupied') {
                occupiedAlert.hidden = false;
                actionOptionsWrap.hidden = true;
                noteGroup.hidden = true;
                submitBtn.hidden = true;
            } else {
                occupiedAlert.hidden = true;
                actionOptionsWrap.hidden = false;
                noteGroup.hidden = false;
                submitBtn.hidden = false;

                var options = buildAllowedTransitions(mState);
                options.forEach(function (opt, idx) {
                    var label = document.createElement('label');
                    label.className = 'hk-rs-radio-card' + (idx === 0 ? ' is-selected' : '');

                    var radio = document.createElement('input');
                    radio.type = 'radio';
                    radio.name = 'targetAction';
                    radio.value = opt.value;
                    if (idx === 0) radio.checked = true;

                    radio.addEventListener('change', function () {
                        $all('.hk-rs-radio-card', radioHost).forEach(function (c) { c.classList.remove('is-selected'); });
                        label.classList.add('is-selected');
                        syncModalRequirementState(opt.value);
                    });

                    var content = document.createElement('div');
                    content.className = 'hk-rs-radio-content';

                    var titleSpan = document.createElement('strong');
                    titleSpan.className = 'hk-rs-radio-title';
                    titleSpan.textContent = opt.title;

                    var descSpan = document.createElement('span');
                    descSpan.className = 'hk-rs-radio-desc';
                    descSpan.textContent = opt.desc;

                    content.appendChild(titleSpan);
                    content.appendChild(descSpan);
                    label.appendChild(radio);
                    label.appendChild(content);
                    radioHost.appendChild(label);
                });

                if (options.length > 0) {
                    syncModalRequirementState(options[0].value);
                }
            }

            modal.classList.add('is-open');
            modal.removeAttribute('hidden');
            modal.style.display = 'flex';
            document.body.style.overflow = 'hidden';
        }

        function closeUpdateRoomModal() {
            var modal = $('#rsUpdateModal');
            if (!modal) return;
            modal.classList.remove('is-open');
            modal.style.display = 'none';
            document.body.style.overflow = '';
        }

        async function handleStatusFormSubmit(e) {
            e.preventDefault();
            if (isSubmitting) return;

            var form = e.target;
            var roomId = $('#rsFormRoomId').value;
            var roomNumber = $('#rsModalRoomNumber').textContent;
            var selectedRadio = $('input[name="targetAction"]:checked', form);
            if (!selectedRadio) return;

            var targetAction = selectedRadio.value;
            var note = ($('#rsFormNote').value || '').trim();

            var checkBedLinens = $('#chkBedLinens') && $('#chkBedLinens').checked;
            var checkBathroom = $('#chkBathroom') && $('#chkBathroom').checked;
            var checkAmenities = $('#chkAmenities') && $('#chkAmenities').checked;
            var checkAppliances = $('#chkAppliances') && $('#chkAppliances').checked;

            if (targetAction === 'complete_cleaning') {
                if (!checkBedLinens || !checkBathroom || !checkAmenities || !checkAppliances) {
                    notify('error', 'กรุณาติ๊กยืนยัน Checklist ตรวจสอบความพร้อมของห้องพักให้ครบทั้ง 4 ข้อก่อนเปลี่ยนสถานะเป็น Available');
                    return;
                }
            }

            if ((targetAction === 'report_maintenance' || targetAction === 'resolve_maintenance') && !note) {
                notify('error', 'กรุณากรอกรายละเอียดปัญหาหรือการซ่อมบำรุงในช่องหมายเหตุ');
                $('#rsFormNote').focus();
                return;
            }

            var confirmed = await confirmAction(
                'ยืนยันการอัปเดตสถานะห้อง ' + roomNumber,
                'คุณต้องการบันทึกการเปลี่ยนแปลงสถานะของห้อง ' + roomNumber + ' ใช่หรือไม่?'
            );
            if (!confirmed) return;

            isSubmitting = true;
            var submitBtn = $('#rsSubmitBtn');
            if (submitBtn) submitBtn.disabled = true;

            try {
                var json = await postForm(UPDATE_URL, {
                    roomId: roomId,
                    targetAction: targetAction,
                    note: note,
                    checkBedLinens: checkBedLinens,
                    checkBathroom: checkBathroom,
                    checkAmenities: checkAmenities,
                    checkAppliances: checkAppliances
                });

                if (!json || !json.success) {
                    throw new Error((json && json.message) || 'ไม่สามารถอัปเดตสถานะห้องพักได้');
                }

                setFlash('success', json.message);
                closeUpdateRoomModal();
                window.location.reload();
            } catch (err) {
                notify('error', (err && err.message) || 'เกิดข้อผิดพลาดในการเชื่อมต่อเซิร์ฟเวอร์');
                if (submitBtn) submitBtn.disabled = false;
            } finally {
                isSubmitting = false;
            }
        }

        document.addEventListener('click', function (e) {
            var openBtn = e.target.closest('[data-rs-open-modal]');
            if (openBtn) {
                e.preventDefault();
                var card = openBtn.closest('.hk-rs-room-card');
                if (card) openUpdateRoomModal(card);
                return;
            }

            if (e.target.closest('.hk-rs-modal-close')) {
                e.preventDefault();
                closeUpdateRoomModal();
                return;
            }

            var modal = $('#rsUpdateModal');
            if (modal && e.target === modal) closeUpdateRoomModal();
        });

        var rsSearch = $('#rsSearchRoom');
        if (rsSearch) rsSearch.addEventListener('input', applyRoomFilters);

        ['#rsFloorFilter', '#rsTypeFilter', '#rsStatusFilter', '#rsSortBy'].forEach(function (sel) {
            var el = $(sel);
            if (el) el.addEventListener('change', applyRoomFilters);
        });

        var resetBtn = $('#btnResetRoomFilters');
        if (resetBtn) resetBtn.addEventListener('click', resetRoomFilters);

        var refreshBtn = $('#btnRefreshRoomStatus');
        if (refreshBtn) refreshBtn.addEventListener('click', function () { window.location.reload(); });

        var mobileToggle = $('#btnToggleRoomFilterMobile');
        var filterPanel = $('#hkRoomFilterPanel');
        if (mobileToggle && filterPanel) {
            mobileToggle.addEventListener('click', function () {
                filterPanel.classList.toggle('is-collapsed-mobile');
            });
        }

        var form = $('#rsUpdateForm');
        if (form) form.addEventListener('submit', handleStatusFormSubmit);
    })();

    // =====================================================================
    // MODULE 3: CLEANING HISTORY MANAGEMENT (Views/Housekeeping/History.cshtml)
    // =====================================================================
    (function () {
        var root = $('#hkApp');
        if (!root || root.getAttribute('data-page') !== 'cleaning-history') return;

        var BASE = (root.getAttribute('data-base-url') || '/Housekeeping').replace(/\/+$/, '');
        var EXPORT_URL = root.getAttribute('data-export-url') || (BASE + '/ExportCleaningHistory');

        var state = {
            currentPage: 1,
            pageSize: 15,
            matchedRows: []
        };

        function notifyError(msg) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                window.Swal.fire({ icon: 'error', title: 'เกิดข้อผิดพลาด', text: msg, confirmButtonColor: '#2563EB' });
            } else {
                window.alert(msg);
            }
        }

        function applyClientHistoryFilter() {
            var searchVal = ($('#histSearchRoom') ? $('#histSearchRoom').value : '').trim().toLowerCase().replace(/^#/, '');
            var floorVal = $('#histFloorFilter') ? $('#histFloorFilter').value : '';
            var empVal = $('#histEmployeeFilter') ? $('#histEmployeeFilter').value : '';
            var statusVal = $('#histStatusFilter') ? $('#histStatusFilter').value : '';
            var singleDateVal = $('#histSingleDate') ? $('#histSingleDate').value : '';
            var fromVal = $('#histDateFrom') ? $('#histDateFrom').value : '';
            var toVal = $('#histDateTo') ? $('#histDateTo').value : '';
            var issueVal = $('#histIssueFilter') ? $('#histIssueFilter').value : '';

            var allRows = $all('.hk-history-row');
            var matched = [];

            allRows.forEach(function (row) {
                var rRoom = (row.getAttribute('data-room-number') || '').toLowerCase();
                var rTask = (row.getAttribute('data-task-id') || '').toLowerCase();
                var rFloor = row.getAttribute('data-floor') || '';
                var rEmp = row.getAttribute('data-employee-id') || '';
                var rStatus = row.getAttribute('data-status') || '';
                var rRawStatus = row.getAttribute('data-raw-status') || '';
                var rDate = row.getAttribute('data-date') || '';
                var rIssue = row.getAttribute('data-has-issue') === 'true';

                var ok = true;
                if (searchVal && rRoom.indexOf(searchVal) === -1 && rTask.indexOf(searchVal) === -1) ok = false;
                if (floorVal && rFloor !== floorVal) ok = false;
                if (empVal && rEmp !== empVal) ok = false;
                if (statusVal && rStatus !== statusVal && rRawStatus !== statusVal) ok = false;
                if (singleDateVal && rDate !== singleDateVal) ok = false;
                if (!singleDateVal) {
                    if (fromVal && rDate < fromVal) ok = false;
                    if (toVal && rDate > toVal) ok = false;
                }
                if (issueVal === 'issue_only' && !rIssue) ok = false;
                if (issueVal === 'normal_only' && rIssue) ok = false;

                if (ok) matched.push(row);
                else row.hidden = true;
            });

            state.matchedRows = matched;
            state.currentPage = 1;
            renderHistoryPagination();
        }

        function renderHistoryPagination() {
            var total = state.matchedRows.length;
            var pageSize = state.pageSize;
            var totalPages = Math.max(1, Math.ceil(total / pageSize));
            if (state.currentPage > totalPages) state.currentPage = totalPages;

            var startIdx = (state.currentPage - 1) * pageSize;
            var endIdx = Math.min(startIdx + pageSize, total);

            state.matchedRows.forEach(function (row, idx) {
                var visible = idx >= startIdx && idx < endIdx;
                row.hidden = !visible;
                if (visible) {
                    var idxCell = $('.hk-col-index', row);
                    if (idxCell) idxCell.textContent = String(idx + 1);
                }
            });

            var startEl = $('#histShowingStart');
            var endEl = $('#histShowingEnd');
            var totalEl = $('#histTotalCount');
            if (startEl) startEl.textContent = total === 0 ? '0' : String(startIdx + 1);
            if (endEl) endEl.textContent = String(endIdx);
            if (totalEl) totalEl.textContent = String(total);

            var emptyEl = $('#hkHistoryEmptyState');
            if (emptyEl) emptyEl.hidden = total > 0;

            var pagBar = $('#histPaginationBar');
            if (pagBar) pagBar.hidden = total === 0;

            var prevBtn = $('#histPrevBtn');
            var nextBtn = $('#histNextBtn');
            if (prevBtn) prevBtn.disabled = state.currentPage <= 1;
            if (nextBtn) nextBtn.disabled = state.currentPage >= totalPages;

            var numHost = $('#histPageNumbers');
            if (numHost) {
                numHost.textContent = '';
                for (var p = 1; p <= totalPages; p++) {
                    var b = document.createElement('button');
                    b.type = 'button';
                    b.className = 'hk-page-num-btn' + (p === state.currentPage ? ' is-active' : '');
                    b.setAttribute('data-hist-page', String(p));
                    b.textContent = String(p);
                    numHost.appendChild(b);
                }
            }
        }

        function makeDetailField(label, val) {
            var row = document.createElement('div');
            row.className = 'hk-detail-row';
            var l = document.createElement('span');
            l.className = 'hk-detail-label';
            l.textContent = label;
            var v = document.createElement('span');
            v.className = 'hk-detail-value';
            v.textContent = (val === null || val === undefined || val === '') ? '-' : String(val);
            row.appendChild(l);
            row.appendChild(v);
            return row;
        }

        function renderHistoryDetailModal(d) {
            var titleEl = $('#histModalTitle');
            var subEl = $('#histModalSub');
            var host = $('#histModalBodyContent');
            if (!host) return;

            if (titleEl) titleEl.textContent = 'ประวัติงาน #' + d.taskId + ' — ห้อง ' + d.roomNumber;
            if (subEl) subEl.textContent = 'ชั้น ' + d.floorNo + ' • ' + (d.roomTypeName || 'Standard Room');
            host.textContent = '';

            // ส่วนที่ 1: ข้อมูลหลัก
            var infoSection = document.createElement('div');
            infoSection.className = 'hk-detail-list mb-3';
            infoSection.appendChild(makeDetailField('หมายเลขห้อง', 'ห้อง ' + d.roomNumber));
            infoSection.appendChild(makeDetailField('ชั้น', 'ชั้น ' + d.floorNo));
            infoSection.appendChild(makeDetailField('ประเภทห้อง', d.roomTypeName || 'Standard'));
            infoSection.appendChild(makeDetailField('ผู้รับผิดชอบ', d.assignedToName || 'ยังไม่ระบุ'));
            infoSection.appendChild(makeDetailField('วันที่ได้รับมอบหมาย', fmtDate(d.createdAt, true)));
            infoSection.appendChild(makeDetailField('เวลาเริ่มทำความสะอาด', fmtDate(d.startedAt, true)));
            infoSection.appendChild(makeDetailField('เวลาทำความสะอาดเสร็จ', fmtDate(d.completedAt, true)));
            infoSection.appendChild(makeDetailField('ระยะเวลาที่ใช้', d.durationMinutes !== null && d.durationMinutes !== undefined ? d.durationMinutes + ' นาที' : '-'));
            infoSection.appendChild(makeDetailField('สถานะงาน', d.statusThaiLabel || d.statusLabel));
            infoSection.appendChild(makeDetailField('หมายเหตุ / ปัญหาที่พบ', d.note || 'ไม่มีหมายเหตุเพิ่มเติม'));
            host.appendChild(infoSection);

            // ส่วนที่ 2: Checklist การตรวจสอบห้อง (เช็กจาก passedChecklist จริงใน ViewModel)
            var chkBox = document.createElement('div');
            chkBox.className = 'hk-hist-checklist-summary mb-3';
            var chkTitle = document.createElement('h4');
            chkTitle.className = 'hk-rs-checklist-title';
            chkTitle.textContent = 'ผลการตรวจสอบมาตรฐานความสะอาด (Room Readiness Checklist)';
            chkBox.appendChild(chkTitle);

            var items = [
                'การจัดเตียง ผ้าปูที่นอน และผ้าเช็ดตัวสะอาดครบถ้วน',
                'ความสะอาดห้องน้ำ สุขภัณฑ์ และของใช้ในห้องน้ำ',
                'การเติมน้ำดื่ม ของใช้ภายในห้อง และความสะอาดพื้น/กระจก',
                'ความพร้อมของเครื่องปรับอากาศ หลอดไฟ และอุปกรณ์ไฟฟ้า'
            ];

            var isPassed = !!d.passedChecklist;
            items.forEach(function (txt) {
                var line = document.createElement('div');
                line.className = 'hk-hist-check-line';
                if (isPassed) {
                    line.textContent = '✓ ผ่านการตรวจสอบ: ' + txt;
                } else if (d.status === 'done') {
                    line.textContent = '– ปิดงานแบบด่วน (ไม่ได้ทำ Checklist): ' + txt;
                } else {
                    line.textContent = '• รอการตรวจสอบ: ' + txt;
                }
                chkBox.appendChild(line);
            });
            host.appendChild(chkBox);

            // ส่วนที่ 3: Activity Timeline ของงานนี้
            if (Array.isArray(d.timelineEvents) && d.timelineEvents.length > 0) {
                var tlWrap = document.createElement('div');
                tlWrap.className = 'hk-hist-modal-timeline';
                var tlHeading = document.createElement('h4');
                tlHeading.className = 'hk-section-title fs-6 mb-2';
                tlHeading.textContent = 'ลำดับเหตุการณ์ของงานนี้ (Activity Timeline)';
                tlWrap.appendChild(tlHeading);

                var ul = document.createElement('ul');
                ul.className = 'hk-activity-timeline';
                d.timelineEvents.forEach(function (ev) {
                    var li = document.createElement('li');
                    li.className = 'hk-activity-item';

                    var marker = document.createElement('div');
                    var mCls = ev.eventType === 'completed' ? 'hk-act-success'
                        : ev.eventType === 'maintenance' ? 'hk-act-danger'
                        : ev.eventType === 'started' ? 'hk-act-info' : 'hk-act-warning';
                    marker.className = 'hk-activity-marker ' + mCls;

                    var body = document.createElement('div');
                    body.className = 'hk-activity-body';

                    var top = document.createElement('div');
                    top.className = 'hk-activity-top';
                    var st = document.createElement('strong');
                    st.className = 'hk-activity-title';
                    st.textContent = ev.title;
                    var tm = document.createElement('span');
                    tm.className = 'hk-activity-time';
                    tm.textContent = fmtDate(ev.timestamp, true);
                    top.appendChild(st);
                    top.appendChild(tm);

                    body.appendChild(top);
                    if (ev.description) {
                        var p = document.createElement('p');
                        p.className = 'hk-activity-note';
                        p.textContent = ev.description;
                        body.appendChild(p);
                    }
                    if (ev.actorName) {
                        var act = document.createElement('span');
                        act.className = 'hk-activity-actor';
                        act.textContent = 'ผู้ดำเนินการ: ' + ev.actorName;
                        body.appendChild(act);
                    }

                    li.appendChild(marker);
                    li.appendChild(body);
                    ul.appendChild(li);
                });
                tlWrap.appendChild(ul);
                host.appendChild(tlWrap);
            }
        }

        async function openHistoryDetails(taskId) {
            var modal = $('#histDetailModal');
            var host = $('#histModalBodyContent');
            if (!modal || !host) return;

            host.textContent = 'กำลังโหลดข้อมูลประวัติ...';
            modal.classList.add('is-open');
            modal.removeAttribute('hidden');
            modal.style.display = 'flex';
            document.body.style.overflow = 'hidden';

            try {
                var res = await fetch(BASE + '/TaskDetails/' + encodeURIComponent(taskId), {
                    headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' },
                    credentials: 'same-origin'
                });
                var json = await readJson(res);
                if (!json || !json.success || !json.data) {
                    throw new Error((json && json.message) || 'ไม่สามารถดึงรายละเอียดประวัติได้');
                }
                renderHistoryDetailModal(json.data);
            } catch (err) {
                closeHistoryModal();
                notifyError((err && err.message) || 'เกิดข้อผิดพลาดในการโหลดรายละเอียด');
            }
        }

        function closeHistoryModal() {
            var modal = $('#histDetailModal');
            if (!modal) return;
            modal.classList.remove('is-open');
            modal.style.display = 'none';
            document.body.style.overflow = '';
        }

        function initPerformanceChart() {
            var canvas = $('#hkPerformanceChart');
            if (!canvas || !window.Chart) return;

            try {
                var labels = JSON.parse(canvas.getAttribute('data-labels') || '[]');
                var completed = JSON.parse(canvas.getAttribute('data-completed') || '[]');
                var issues = JSON.parse(canvas.getAttribute('data-issues') || '[]');

                new window.Chart(canvas, {
                    type: 'bar',
                    data: {
                        labels: labels,
                        datasets: [
                            {
                                label: 'ทำความสะอาดเสร็จ (ห้อง)',
                                data: completed,
                                backgroundColor: '#2563EB',
                                borderRadius: 6
                            },
                            {
                                label: 'พบปัญหา/แจ้งซ่อม (รายการ)',
                                data: issues,
                                backgroundColor: '#DC2626',
                                borderRadius: 6
                            }
                        ]
                    },
                    options: {
                        responsive: true,
                        plugins: {
                            legend: { position: 'top' }
                        },
                        scales: {
                            y: { beginAtZero: true, ticks: { precision: 0 } }
                        }
                    }
                });
            } catch (e) { /* ignore chart parse error */ }
        }

        function exportFilteredHistory() {
            var form = $('#hkHistoryFilterForm');
            var params = form ? new URLSearchParams(new FormData(form)).toString() : '';
            window.location.href = EXPORT_URL + (params ? ('?' + params) : '');
        }

        var pageSizeSelect = $('#histPageSize');
        if (pageSizeSelect) {
            state.pageSize = parseInt(pageSizeSelect.value || '15', 10) || 15;
            pageSizeSelect.addEventListener('change', function () {
                state.pageSize = parseInt(pageSizeSelect.value || '15', 10) || 15;
                state.currentPage = 1;
                renderHistoryPagination();
            });
        }

        document.addEventListener('click', function (e) {
            var detailBtn = e.target.closest('[data-hist-details]');
            if (detailBtn) {
                e.preventDefault();
                openHistoryDetails(detailBtn.getAttribute('data-hist-details'));
                return;
            }

            var pageBtn = e.target.closest('[data-hist-page]');
            if (pageBtn) {
                e.preventDefault();
                state.currentPage = parseInt(pageBtn.getAttribute('data-hist-page'), 10) || 1;
                renderHistoryPagination();
                return;
            }

            if (e.target.closest('.hk-hist-modal-close')) {
                e.preventDefault();
                closeHistoryModal();
                return;
            }

            var modal = $('#histDetailModal');
            if (modal && e.target === modal) closeHistoryModal();
        });

        var prevBtn = $('#histPrevBtn');
        if (prevBtn) {
            prevBtn.addEventListener('click', function () {
                if (state.currentPage > 1) {
                    state.currentPage--;
                    renderHistoryPagination();
                }
            });
        }

        var nextBtn = $('#histNextBtn');
        if (nextBtn) {
            nextBtn.addEventListener('click', function () {
                state.currentPage++;
                renderHistoryPagination();
            });
        }

        ['#histSearchRoom', '#histFloorFilter', '#histEmployeeFilter', '#histStatusFilter', '#histSingleDate', '#histDateFrom', '#histDateTo', '#histIssueFilter']
            .forEach(function (sel) {
                var el = $(sel);
                if (!el) return;
                el.addEventListener(sel === '#histSearchRoom' ? 'input' : 'change', applyClientHistoryFilter);
            });

        var exportBtn = $('#btnExportHistoryExcel');
        if (exportBtn) exportBtn.addEventListener('click', exportFilteredHistory);

        var printBtn = $('#btnPrintHistoryReport');
        if (printBtn) printBtn.addEventListener('click', function () { window.print(); });

        var refreshBtn = $('#btnRefreshHistory');
        if (refreshBtn) refreshBtn.addEventListener('click', function () { window.location.reload(); });

        var mobileToggle = $('#btnToggleHistoryFilterMobile');
        var filterForm = $('#hkHistoryFilterForm');
        if (mobileToggle && filterForm) {
            mobileToggle.addEventListener('click', function () {
                filterForm.classList.toggle('is-collapsed-mobile');
            });
        }

        applyClientHistoryFilter();
        initPerformanceChart();
    })();

    // =====================================================================
    // MODULE 4: FULL-PAGE TASK DETAILS (Views/Housekeeping/TaskDetails.cshtml)
    // =====================================================================
    (function () {
        var root = $('#hkApp');
        if (!root || root.getAttribute('data-page') !== 'task-details') return;

        var BASE = (root.getAttribute('data-base-url') || '/Housekeeping').replace(/\/+$/, '');
        var UPDATE_ROOM_URL = root.getAttribute('data-update-room-url') || (BASE + '/UpdateRoomStatusWorkflow');
        var MAINT_URL = root.getAttribute('data-maintenance-url') || (BASE + '/ReportMaintenance');

        function showAlert(type, title, text) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                return window.Swal.fire({
                    icon: type,
                    title: title,
                    text: text,
                    confirmButtonColor: '#2563EB'
                });
            }
            window.alert(title + ': ' + text);
        }

        async function askConfirm(title, text) {
            if (window.Swal && typeof window.Swal.fire === 'function') {
                var res = await window.Swal.fire({
                    title: title,
                    text: text,
                    icon: 'question',
                    showCancelButton: true,
                    confirmButtonColor: '#16A34A',
                    cancelButtonColor: '#64748B',
                    confirmButtonText: 'ยืนยัน',
                    cancelButtonText: 'ยกเลิก'
                });
                return !!res.isConfirmed;
            }
            return window.confirm(text);
        }

        var toggleMaintBtn = $('#btnToggleTdMaintForm');
        var maintForm = $('#tdInlineMaintenanceForm');
        var cancelMaintBtn = $('#btnCancelTdMaint');

        if (toggleMaintBtn && maintForm) {
            toggleMaintBtn.addEventListener('click', function () {
                maintForm.hidden = !maintForm.hidden;
                if (!maintForm.hidden) {
                    var txt = $('#tdMaintIssueNote');
                    if (txt) txt.focus();
                }
            });
        }

        if (cancelMaintBtn && maintForm) {
            cancelMaintBtn.addEventListener('click', function () {
                maintForm.hidden = true;
            });
        }

        if (maintForm) {
            maintForm.addEventListener('submit', async function (e) {
                e.preventDefault();
                var roomId = maintForm.getAttribute('data-room-id');
                var taskId = maintForm.getAttribute('data-task-id');
                var issueNote = ($('#tdMaintIssueNote') ? $('#tdMaintIssueNote').value : '').trim();

                if (!issueNote) {
                    showAlert('warning', 'ข้อมูลไม่ครบถ้วน', 'กรุณาระบุอาการชำรุดหรือปัญหาที่พบในห้องพัก');
                    return;
                }

                var ok = await askConfirm('ยืนยันการแจ้งซ่อมบำรุง', 'ห้องพักจะถูกเปลี่ยนสถานะเป็น Maintenance (ระงับการขายชั่วคราว) ทันที');
                if (!ok) return;

                var submitBtn = $('#btnSubmitTdMaint');
                if (submitBtn) submitBtn.disabled = true;

                try {
                    var json = await postForm(MAINT_URL, {
                        roomId: roomId,
                        taskId: taskId || undefined,
                        issueNote: issueNote
                    });

                    if (!json || !json.success) {
                        throw new Error((json && json.message) || 'ไม่สามารถบันทึกแจ้งซ่อมบำรุงได้');
                    }

                    setFlash('success', json.message);
                    window.location.reload();
                } catch (err) {
                    showAlert('error', 'เกิดข้อผิดพลาด', (err && err.message) || 'ไม่สามารถเชื่อมต่อกับเซิร์ฟเวอร์ได้');
                    if (submitBtn) submitBtn.disabled = false;
                }
            });
        }

        var checklistForm = $('#tdCompleteChecklistForm');
        if (checklistForm) {
            checklistForm.addEventListener('submit', async function (e) {
                e.preventDefault();

                var chkBed = $('#tdChkBed') && $('#tdChkBed').checked;
                var chkBath = $('#tdChkBath') && $('#tdChkBath').checked;
                var chkAmenity = $('#tdChkAmenity') && $('#tdChkAmenity').checked;
                var chkAppliance = $('#tdChkAppliance') && $('#tdChkAppliance').checked;

                if (!chkBed || !chkBath || !chkAmenity || !chkAppliance) {
                    showAlert('warning', 'ตรวจเช็คไม่ครบถ้วน', 'กรุณาติ๊กยืนยันรายการตรวจสอบความพร้อมของห้องพักให้ครบทั้ง 4 ข้อก่อนปิดงาน');
                    return;
                }

                var roomId = checklistForm.getAttribute('data-room-id');
                var roomNumber = checklistForm.getAttribute('data-room-number');
                var note = ($('#tdCompleteNote') ? $('#tdCompleteNote').value : '').trim();

                var ok = await askConfirm(
                    'ยืนยันปิดงานห้อง ' + roomNumber,
                    'ตรวจสอบความพร้อมครบถ้วนแล้ว และต้องการเปลี่ยนสถานะห้องเป็น Available (พร้อมขาย) ใช่หรือไม่?'
                );
                if (!ok) return;

                var btn = $('#btnTdCompleteSubmit');
                if (btn) btn.disabled = true;

                try {
                    var json = await postForm(UPDATE_ROOM_URL, {
                        roomId: roomId,
                        targetAction: 'complete_cleaning',
                        note: note,
                        checkBedLinens: true,
                        checkBathroom: true,
                        checkAmenities: true,
                        checkAppliances: true
                    });

                    if (!json || !json.success) {
                        throw new Error((json && json.message) || 'ไม่สามารถบันทึกปิดงานได้');
                    }

                    setFlash('success', json.message);
                    window.location.reload();
                } catch (err) {
                    showAlert('error', 'เกิดข้อผิดพลาด', (err && err.message) || 'ไม่สามารถบันทึกข้อมูลได้');
                    if (btn) btn.disabled = false;
                }
            });
        }
    })();
})();