/*!
 * StayMate Manager Portal - manager.js
 * ถูกโหลดจาก _ManagerLayout ทุกหน้า ทุกฟังก์ชันจึงตรวจ Element ก่อนทำงาน
 * ไม่มีข้อมูล Mock: ข้อมูลกราฟมาจาก /Manager/DashboardData เท่านั้น
 */
(function () {
  'use strict';

  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  // ───────────────────────── Formatters ─────────────────────────
  var fmt = {
    currency: function (v) {
      return new Intl.NumberFormat('th-TH', { style: 'currency', currency: 'THB' }).format(Number(v) || 0);
    },
    number: function (v) { return new Intl.NumberFormat('th-TH').format(Number(v) || 0); },
    percent: function (v) { return (Number(v) || 0).toFixed(2) + '%'; },
    rating: function (v) { return v == null ? '-' : Number(v).toFixed(2); },
    text: function (v) { return v == null ? '' : String(v); }
  };

  function pad(n) { return n < 10 ? '0' + n : '' + n; }
  function toYmd(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }

  // ───────────────────────── Alerts (Bootstrap alert สร้างด้วย JS) ─────────────────────────
  function showAlert(type, message) {
    var host = $('#managerAlertHost');
    if (!host) {
      host = document.createElement('div');
      host.id = 'managerAlertHost';
      host.style.cssText = 'position:fixed;top:80px;right:16px;z-index:2000;max-width:380px;';
      document.body.appendChild(host);
    }
    var el = document.createElement('div');
    el.className = 'alert alert-' + type + ' shadow-sm mb-2';
    el.setAttribute('role', 'alert');
    el.textContent = message;
    host.appendChild(el);
    setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 4500);
  }

  // ───────────────────────── Anti-forgery & HTTP ─────────────────────────
  function getToken(scope) {
    var input = (scope && $('input[name="__RequestVerificationToken"]', scope)) ||
                $('input[name="__RequestVerificationToken"]');
    return input ? input.value : null;
  }

  async function postForm(url, body, scope) {
    var headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    var token = getToken(scope);
    if (token) headers['RequestVerificationToken'] = token;

    var res = await fetch(url, { method: 'POST', body: body, headers: headers, credentials: 'same-origin' });
    var type = res.headers.get('content-type') || '';
    if (res.status === 401 || res.status === 403 || res.redirected || type.indexOf('application/json') === -1) {
      throw new Error('สิทธิ์การใช้งานหมดอายุหรือไม่ได้รับอนุญาต กรุณาเข้าสู่ระบบใหม่');
    }
    return res.json();
  }

  async function getJson(url) {
    var res = await fetch(url, {
      headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
      credentials: 'same-origin'
    });
    var type = res.headers.get('content-type') || '';
    if (!res.ok || type.indexOf('application/json') === -1) {
      throw new Error('ไม่สามารถโหลดข้อมูลได้ (' + res.status + ')');
    }
    return res.json();
  }

  // ───────────────────────── Charts ─────────────────────────
  var PALETTE = ['#0d6efd', '#198754', '#ffc107', '#dc3545', '#6f42c1', '#0dcaf0', '#fd7e14', '#6c757d'];
  var nativeState = {}; // canvas.id -> { type, labels, values, colors, label, valueFmt }

  function destroyChartJs(canvas) {
    if (window.Chart && typeof window.Chart.getChart === 'function') {
      var existing = window.Chart.getChart(canvas);
      if (existing) existing.destroy();
    }
  }

  function renderChart(canvasId, type, labels, values, opts) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return;
    opts = opts || {};
    var colors = type === 'doughnut'
      ? labels.map(function (_, i) { return PALETTE[i % PALETTE.length]; })
      : [opts.color || PALETTE[0]];

    if (window.Chart) {
      destroyChartJs(canvas);
      var isDoughnut = type === 'doughnut';
      new window.Chart(canvas.getContext('2d'), {
        type: type,
        data: {
          labels: labels,
          datasets: [{
            label: opts.label || '',
            data: values,
            backgroundColor: isDoughnut ? colors : (type === 'line' ? colors[0] + '33' : colors[0]),
            borderColor: isDoughnut ? '#ffffff' : colors[0],
            borderWidth: isDoughnut ? 2 : 2,
            fill: type === 'line',
            tension: 0.3,
            pointRadius: type === 'line' && labels.length > 40 ? 0 : 3
          }]
        },
        options: {
          responsive: true,
          aspectRatio: isDoughnut ? 1.6 : 2,
          plugins: { legend: { display: isDoughnut, position: 'right' } },
          scales: isDoughnut ? {} : { y: { beginAtZero: true, ticks: { precision: 0 } } }
        }
      });
      delete nativeState[canvasId];
      return;
    }

    // ไม่มี Chart.js: วาดด้วย Canvas 2D (ไม่เพิ่ม Library)
    nativeState[canvasId] = { type: type, labels: labels, values: values, colors: colors, valueFmt: opts.valueFmt };
    drawNative(canvas, nativeState[canvasId]);
  }

  function drawNative(canvas, s) {
    var dpr = window.devicePixelRatio || 1;
    var parent = canvas.parentElement;
    var w = Math.max(280, (parent && parent.clientWidth) || 600);
    var h = Number(canvas.getAttribute('data-height')) || 260;
    canvas.style.width = w + 'px';
    canvas.style.height = h + 'px';
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);

    var ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.font = '12px system-ui, sans-serif';
    var text = getComputedStyle(canvas).color || '#555';
    var vf = s.valueFmt || fmt.number;

    var total = s.values.reduce(function (a, b) { return a + (Number(b) || 0); }, 0);
    if (!s.values.length || (s.type === 'doughnut' && total === 0)) {
      ctx.fillStyle = text; ctx.textAlign = 'center';
      ctx.fillText('ไม่มีข้อมูลในช่วงที่เลือก', w / 2, h / 2);
      return;
    }

    if (s.type === 'doughnut') {
      var cx = Math.min(h / 2, w / 3), cy = h / 2, r = Math.min(cx, cy) - 8, start = -Math.PI / 2;
      s.values.forEach(function (v, i) {
        var ang = (Number(v) || 0) / total * Math.PI * 2;
        ctx.beginPath(); ctx.moveTo(cx, cy); ctx.arc(cx, cy, r, start, start + ang); ctx.closePath();
        ctx.fillStyle = s.colors[i]; ctx.fill();
        start += ang;
      });
      ctx.globalCompositeOperation = 'destination-out';
      ctx.beginPath(); ctx.arc(cx, cy, r * 0.58, 0, Math.PI * 2); ctx.fill();
      ctx.globalCompositeOperation = 'source-over';
      ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
      s.labels.forEach(function (label, i) {
        var y = 20 + i * 20, x = cx * 2 + 24;
        ctx.fillStyle = s.colors[i]; ctx.fillRect(x, y - 6, 12, 12);
        ctx.fillStyle = text; ctx.fillText(label + ' (' + vf(s.values[i]) + ')', x + 18, y);
      });
      return;
    }

    // bar / line
    var padL = 56, padR = 12, padT = 12, padB = 28;
    var pw = w - padL - padR, ph = h - padT - padB;
    var max = Math.max.apply(null, s.values.map(Number));
    if (!(max > 0)) max = 1;
    ctx.strokeStyle = 'rgba(128,128,128,.25)'; ctx.fillStyle = text;
    ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
    for (var g = 0; g <= 4; g++) {
      var gy = padT + ph - (ph * g / 4);
      ctx.beginPath(); ctx.moveTo(padL, gy); ctx.lineTo(w - padR, gy); ctx.stroke();
      ctx.fillText(vf(max * g / 4), padL - 6, gy);
    }
    var n = s.values.length, step = pw / n, color = s.colors[0];
    ctx.textAlign = 'center'; ctx.textBaseline = 'top';
    var every = Math.ceil(n / 8);
    s.labels.forEach(function (label, i) {
      if (i % every === 0) { ctx.fillStyle = text; ctx.fillText(label, padL + step * i + step / 2, h - padB + 8); }
    });
    if (s.type === 'bar') {
      ctx.fillStyle = color;
      s.values.forEach(function (v, i) {
        var bh = (Number(v) || 0) / max * ph, bw = Math.max(2, step * 0.7);
        ctx.fillRect(padL + step * i + (step - bw) / 2, padT + ph - bh, bw, bh);
      });
    } else {
      ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.beginPath();
      s.values.forEach(function (v, i) {
        var x = padL + step * i + step / 2, y = padT + ph - (Number(v) || 0) / max * ph;
        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      });
      ctx.stroke();
    }
  }

  var resizeTimer = null;
  window.addEventListener('resize', function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      Object.keys(nativeState).forEach(function (id) {
        var c = document.getElementById(id);
        if (c) drawNative(c, nativeState[id]);
      });
    }, 150);
  });

  // ───────────────────────── Dashboard ─────────────────────────
  // Contract: [data-manager-dashboard][data-endpoint]
  function initDashboard() {
    var root = $('[data-manager-dashboard]');
    if (!root) return;
    var endpoint = root.getAttribute('data-endpoint');
    if (!endpoint) return;

    var form = $('[data-manager-range-form]');
    var startInput = form ? $('[name="startDate"]', form) : null;
    var endInput = form ? $('[name="endDate"]', form) : null;

    function applyData(d) {
      // KPI: <span data-kpi="periodRevenue" data-format="currency">
      $$('[data-kpi]').forEach(function (el) {
        var key = el.getAttribute('data-kpi');
        if (!(key in d)) return;
        var f = fmt[el.getAttribute('data-format') || 'text'] || fmt.text;
        el.textContent = f(d[key]);
      });

      if (startInput && d.startDate) startInput.value = d.startDate;
      if (endInput && d.endDate) endInput.value = d.endDate;

      var rev = d.dailyRevenue || [], bk = d.dailyBookings || [];
      renderChart('managerRevenueChart', 'line',
        rev.map(function (p) { return p.label; }), rev.map(function (p) { return p.value; }),
        { label: 'รายรับ (บาท)', color: '#198754', valueFmt: fmt.number });
      renderChart('managerBookingChart', 'bar',
        bk.map(function (p) { return p.label; }), bk.map(function (p) { return p.count; }),
        { label: 'จำนวนการจอง', color: '#0d6efd', valueFmt: fmt.number });

      var rooms = d.roomsByStatus || [], bs = d.bookingsByStatus || [];
      renderChart('managerRoomChart', 'doughnut',
        rooms.map(function (p) { return p.label; }), rooms.map(function (p) { return p.count; }));
      renderChart('managerBookingStatusChart', 'doughnut',
        bs.map(function (p) { return p.label; }), bs.map(function (p) { return p.count; }));
    }

    async function load() {
      var params = new URLSearchParams();
      if (startInput && startInput.value) params.set('startDate', startInput.value);
      if (endInput && endInput.value) params.set('endDate', endInput.value);
      var url = endpoint + (params.toString() ? (endpoint.indexOf('?') >= 0 ? '&' : '?') + params.toString() : '');

      root.setAttribute('aria-busy', 'true');
      try {
        var data = await getJson(url);
        applyData(data);
        if (window.history && window.history.replaceState && params.toString()) {
          window.history.replaceState(null, '', '?' + params.toString());
        }
      } catch (err) {
        showAlert('danger', err.message || 'โหลดข้อมูลแดชบอร์ดไม่สำเร็จ');
      } finally {
        root.removeAttribute('aria-busy');
      }
    }

    if (form) {
      form.addEventListener('submit', function (e) { e.preventDefault(); load(); });
      $$('[name="startDate"],[name="endDate"]', form).forEach(function (i) { i.addEventListener('change', load); });

      // ปุ่มช่วงเวลาด่วน: <button type="button" data-range-preset="today|7|30|month">
      $$('[data-range-preset]', form).forEach(function (btn) {
        btn.addEventListener('click', function () {
          if (!startInput || !endInput) return;
          var now = new Date(), start = new Date(now), p = btn.getAttribute('data-range-preset');
          if (p === 'month') start = new Date(now.getFullYear(), now.getMonth(), 1);
          else if (p === 'today') start = now;
          else { var days = parseInt(p, 10); if (!isNaN(days) && days > 0) start.setDate(now.getDate() - (days - 1)); }
          startInput.value = toYmd(start);
          endInput.value = toYmd(now);
          load();
        });
      });
    }

    load();
  }

  // ───────────────────────── Search & Filter (ฝั่ง Client) ─────────────────────────
  // <input data-manager-search="#tbl">   <select data-manager-filter="#tbl" data-filter-key="status">
  // แถวที่ถูกกรองต้องมี data-<key> เช่น <tr data-status="available"> หรือ [data-manager-item] ใน container
  function applyFilters(targetSel) {
    var target = $(targetSel);
    if (!target) return;

    var term = '';
    $$('[data-manager-search]').forEach(function (el) {
      if (el.getAttribute('data-manager-search') === targetSel) term = (el.value || '').trim().toLowerCase();
    });
    var selects = $$('[data-manager-filter]').filter(function (el) {
      return el.getAttribute('data-manager-filter') === targetSel && el.value !== '';
    });

    var visible = 0;
    $$('tbody tr, [data-manager-item]', target).forEach(function (row) {
      if (row.hasAttribute('data-manager-empty-row')) return;
      var ok = !term || (row.textContent || '').toLowerCase().indexOf(term) >= 0;
      if (ok) {
        ok = selects.every(function (s) {
          var key = s.getAttribute('data-filter-key');
          return key && row.getAttribute('data-' + key) === s.value;
        });
      }
      row.classList.toggle('d-none', !ok);
      if (ok) visible++;
    });

    $$('[data-manager-empty-for]').forEach(function (el) {
      if (el.getAttribute('data-manager-empty-for') === targetSel) el.classList.toggle('d-none', visible > 0);
    });
  }

  function initFilters() {
    $$('[data-manager-search],[data-manager-filter]').forEach(function (el) {
      var sel = el.getAttribute('data-manager-search') || el.getAttribute('data-manager-filter');
      if (!sel || !$(sel)) return;
      el.addEventListener(el.tagName === 'INPUT' ? 'input' : 'change', function () { applyFilters(sel); });
      applyFilters(sel);
    });

    // ฟอร์มกรองฝั่ง Server (GET): <form method="get" data-manager-autosubmit>
    $$('form[data-manager-autosubmit]').forEach(function (form) {
      var timer = null;
      $$('select, input[type="date"], input[type="checkbox"]', form).forEach(function (el) {
        el.addEventListener('change', function () { form.submit(); });
      });
      $$('input[type="search"], input[type="text"]', form).forEach(function (el) {
        el.addEventListener('input', function () {
          clearTimeout(timer);
          timer = setTimeout(function () { form.submit(); }, 600);
        });
      });
    });
  }

  // ───────────────────────── Confirm / AJAX actions ─────────────────────────
  async function ajaxSubmit(form, submitter) {
    var btn = submitter || $('button[type="submit"]', form);
    if (btn) btn.disabled = true;
    try {
      var result = await postForm(form.getAttribute('action') || window.location.pathname, new FormData(form), form);
      showAlert(result.success ? 'success' : 'danger', result.message || (result.success ? 'ดำเนินการสำเร็จ' : 'ดำเนินการไม่สำเร็จ'));
      if (result.success && form.getAttribute('data-manager-reload') !== 'false') {
        setTimeout(function () { window.location.reload(); }, 700);
        return;
      }
    } catch (err) {
      showAlert('danger', err.message || 'เกิดข้อผิดพลาด');
    }
    if (btn) btn.disabled = false;
  }

  // <button type="button" data-manager-post="/Manager/TogglePromotion" data-manager-params='{"promotionId":5,"isActive":false}'>
  async function ajaxPostButton(btn) {
    var url = btn.getAttribute('data-manager-post');
    var params = {};
    try { params = JSON.parse(btn.getAttribute('data-manager-params') || '{}'); } catch (e) { params = {}; }

    var body = new FormData();
    Object.keys(params).forEach(function (k) { body.append(k, params[k]); });
    var token = getToken();
    if (token) body.append('__RequestVerificationToken', token);

    btn.disabled = true;
    try {
      var result = await postForm(url, body);
      showAlert(result.success ? 'success' : 'danger', result.message || '');
      if (result.success && btn.getAttribute('data-manager-reload') !== 'false') {
        setTimeout(function () { window.location.reload(); }, 700);
        return;
      }
    } catch (err) {
      showAlert('danger', err.message || 'เกิดข้อผิดพลาด');
    }
    btn.disabled = false;
  }

  function initActions() {
    // ยืนยันก่อนส่งฟอร์ม + AJAX: <form data-manager-confirm="ยืนยัน?" data-manager-ajax>
    document.addEventListener('submit', function (e) {
      var form = e.target;
      if (!form || form.tagName !== 'FORM') return;

      var msg = form.getAttribute('data-manager-confirm');
      if (msg && !window.confirm(msg)) { e.preventDefault(); return; }

      if (form.hasAttribute('data-manager-ajax')) {
        e.preventDefault();
        ajaxSubmit(form, e.submitter);
      }
    });

    document.addEventListener('click', function (e) {
      var target = e.target;
      if (!target || !target.closest) return;

      // ยืนยันบนปุ่ม/ลิงก์ (ไม่ใช่ตัว form เพราะ submit handler ด้านบนจัดการแล้ว)
      var confirmEl = target.closest('[data-manager-confirm]');
      if (confirmEl && confirmEl.tagName !== 'FORM') {
        if (!window.confirm(confirmEl.getAttribute('data-manager-confirm'))) {
          e.preventDefault();
          e.stopImmediatePropagation();
          return;
        }
      }

      var postBtn = target.closest('[data-manager-post]');
      if (postBtn) { e.preventDefault(); ajaxPostButton(postBtn); return; }

      // เติมข้อมูลลงฟอร์มแก้ไข + เปิด Modal:
      // <button data-manager-fill="#priceForm" data-manager-fill-data='{"RateId":3,"PricePerNight":1500}'>
      var fillBtn = target.closest('[data-manager-fill]');
      if (fillBtn) {
        var form = $(fillBtn.getAttribute('data-manager-fill'));
        if (!form) return;
        var data = {};
        try { data = JSON.parse(fillBtn.getAttribute('data-manager-fill-data') || '{}'); } catch (err) { return; }

        form.reset();
        Object.keys(data).forEach(function (name) {
          var field = form.elements[name];
          if (!field) return;
          var v = data[name];
          if (field.type === 'checkbox') field.checked = !!v;
          else field.value = v == null ? '' : v;
        });

        var modalEl = form.closest('.modal');
        if (modalEl && window.bootstrap && window.bootstrap.Modal) {
          window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }
      }
    });
  }

  // ───────────────────────── Init ─────────────────────────
  function init() {
    initActions();
    initFilters();
    initDashboard();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();