/*!
 * StayMate Manager Portal - manager.js
 * โหลดจาก _ManagerLayout ทุกหน้า ทุกฟังก์ชันจึงตรวจ Element ก่อนทำงาน
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
  var PALETTE = ['#2563EB', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#06B6D4', '#F97316', '#64748B'];
  var nativeState = {};
  var chartShowValues = {
    managerRevenueChart: true,
    managerBookingChart: true
  };
  var lastDashboardData = null;
  var reloadDashboardFn = null;

  var revenueValueLabelsPlugin = {
    id: 'managerRevenueValueLabels',
    afterDatasetsDraw: function (chart, args, pluginOpts) {
      if (!pluginOpts || !pluginOpts.enabled) return;
      var meta = chart.getDatasetMeta(0);
      if (!meta || !meta.data || !meta.data.length) return;

      var ctx = chart.ctx;
      ctx.save();
      ctx.font = '600 11px "Prompt", system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'bottom';

      var values = chart.data.datasets[0].data || [];
      var step = values.length > 24 ? Math.ceil(values.length / 12) : 1;

      meta.data.forEach(function (pt, idx) {
        var val = Number(values[idx]) || 0;
        if (val <= 0 || (step > 1 && idx % step !== 0 && idx !== values.length - 1)) return;
        if (pt.x == null || pt.y == null) return;

        ctx.fillStyle = '#065F46';
        ctx.fillText('฿' + fmt.number(val), pt.x, pt.y - 8);
      });

      ctx.restore();
    }
  };

  var barTopLabelsPlugin = {
    id: 'managerBarTopLabels',
    afterDatasetsDraw: function (chart, args, pluginOpts) {
      if (!pluginOpts || !pluginOpts.enabled) return;
      var meta = chart.getDatasetMeta(0);
      if (!meta || !meta.data || !meta.data.length) return;

      var ctx = chart.ctx;
      ctx.save();
      ctx.font = '700 11px "Prompt", system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'bottom';
      ctx.fillStyle = '#1D4ED8';

      var values = chart.data.datasets[0].data || [];
      meta.data.forEach(function (bar, idx) {
        var val = Number(values[idx]) || 0;
        if (val <= 0 || bar.x == null || bar.y == null) return;
        ctx.fillText(fmt.number(val), bar.x, bar.y - 5);
      });

      ctx.restore();
    }
  };

  var doughnutCenterPlugin = {
    id: 'managerDoughnutCenter',
    afterDraw: function (chart, args, pluginOpts) {
      if (!pluginOpts || !pluginOpts.enabled) return;
      var meta = chart.getDatasetMeta(0);
      if (!meta || !meta.data || !meta.data.length) return;

      var firstArc = meta.data[0];
      var cx = firstArc.x;
      var cy = firstArc.y;
      var values = chart.data.datasets[0].data || [];
      var total = values.reduce(function (a, b) { return a + (Number(b) || 0); }, 0);

      var activeElements = chart.getActiveElements ? chart.getActiveElements() : [];
      var displayVal = total;
      var displaySub = pluginOpts.totalLabel || 'ทั้งหมด';

      if (activeElements && activeElements.length > 0) {
        var idx = activeElements[0].index;
        displayVal = Number(values[idx]) || 0;
        displaySub = chart.data.labels[idx] || displaySub;
      }

      var ctx = chart.ctx;
      ctx.save();
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';

      ctx.font = '700 20px "Prompt", system-ui, sans-serif';
      ctx.fillStyle = '#0F172A';
      ctx.fillText(fmt.number(displayVal), cx, cy - 7);

      ctx.font = '500 11px "Prompt", system-ui, sans-serif';
      ctx.fillStyle = '#64748B';
      var subText = displaySub.length > 14 ? displaySub.slice(0, 13) + '…' : displaySub;
      ctx.fillText(subText, cx, cy + 13);
      ctx.restore();
    }
  };

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
    var isRevenueLine = canvasId === 'managerRevenueChart' && type === 'line';
    var isBookingBar = canvasId === 'managerBookingChart' && type === 'bar';
    var isDoughnut = type === 'doughnut';
    var showValues = !!chartShowValues[canvasId];
    var colors = isDoughnut
      ? labels.map(function (_, i) { return PALETTE[i % PALETTE.length]; })
      : [opts.color || PALETTE[0]];

    if (window.Chart) {
      destroyChartJs(canvas);
      var ctx2d = canvas.getContext('2d');
      var areaFill = colors[0] + '33';

      if (isRevenueLine && ctx2d.createLinearGradient) {
        var gradRev = ctx2d.createLinearGradient(0, 0, 0, canvas.clientHeight || 260);
        gradRev.addColorStop(0, 'rgba(16, 185, 129, 0.34)');
        gradRev.addColorStop(1, 'rgba(16, 185, 129, 0.01)');
        areaFill = gradRev;
      } else if (isBookingBar && ctx2d.createLinearGradient) {
        var gradBar = ctx2d.createLinearGradient(0, 0, 0, canvas.clientHeight || 220);
        gradBar.addColorStop(0, '#3B82F6');
        gradBar.addColorStop(1, 'rgba(59, 130, 246, 0.45)');
        areaFill = gradBar;
      }

      var plugins = [];
      if (isRevenueLine) plugins.push(revenueValueLabelsPlugin);
      if (isBookingBar) plugins.push(barTopLabelsPlugin);
      if (isDoughnut) plugins.push(doughnutCenterPlugin);

      var totalSum = values.reduce(function (a, b) { return a + (Number(b) || 0); }, 0);
      var centerSub = canvasId === 'managerRoomChart' ? 'ห้องทั้งหมด' : 'ใบจองทั้งหมด';

      new window.Chart(ctx2d, {
        type: type,
        plugins: plugins,
        data: {
          labels: labels,
          datasets: [{
            label: opts.label || '',
            data: values,
            backgroundColor: isDoughnut ? colors : (type === 'line' ? areaFill : (isBookingBar ? areaFill : colors[0])),
            borderColor: isDoughnut ? '#ffffff' : colors[0],
            borderWidth: isDoughnut ? 2.5 : (isRevenueLine ? 2.5 : 1.5),
            borderRadius: isBookingBar ? 6 : 0,
            borderSkipped: isBookingBar ? 'bottom' : false,
            hoverOffset: isDoughnut ? 8 : 0,
            fill: type === 'line',
            tension: 0.35,
            pointRadius: isRevenueLine ? (labels.length > 40 ? 2 : 4.5) : (type === 'line' && labels.length > 40 ? 0 : 3),
            pointHoverRadius: isRevenueLine ? 7 : 5,
            pointBackgroundColor: isRevenueLine ? '#10B981' : colors[0],
            pointBorderColor: '#ffffff',
            pointBorderWidth: isRevenueLine ? 2 : 1
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          aspectRatio: isDoughnut ? 1.5 : 2,
          cutout: isDoughnut ? '64%' : undefined,
          interaction: {
            mode: isDoughnut ? 'nearest' : 'index',
            intersect: isDoughnut
          },
          animation: {
            duration: isDoughnut ? 900 : 850,
            easing: 'easeOutQuart'
          },
          plugins: {
            legend: {
              display: isDoughnut,
              position: 'right',
              labels: {
                usePointStyle: true,
                pointStyle: 'circle',
                boxWidth: 8,
                padding: 10,
                font: {
                  family: '"Prompt", system-ui, sans-serif',
                  size: 11.5
                }
              }
            },
            managerRevenueValueLabels: { enabled: isRevenueLine && showValues },
            managerBarTopLabels: { enabled: isBookingBar && showValues },
            managerDoughnutCenter: { enabled: isDoughnut, totalLabel: centerSub },
            tooltip: {
              enabled: true,
              backgroundColor: 'rgba(7, 13, 30, 0.94)',
              titleColor: '#00E5FF',
              bodyColor: '#FFFFFF',
              padding: 10,
              cornerRadius: 10,
              displayColors: true,
              callbacks: {
                label: function (context) {
                  var raw = context.parsed && context.parsed.y != null ? context.parsed.y : context.parsed;
                  var num = Number(raw) || 0;
                  var formatted = (opts.valueFmt || fmt.number)(num);
                  if (isDoughnut && totalSum > 0) {
                    var pct = ((num / totalSum) * 100).toFixed(1);
                    return ' ' + formatted + ' รายการ (' + pct + '%)';
                  }
                  return ' ' + (context.dataset.label || 'มูลค่า') + ': ' + (isRevenueLine ? '฿' + formatted : formatted);
                }
              }
            }
          },
          scales: isDoughnut
            ? {}
            : {
                x: {
                  grid: { display: false },
                  ticks: {
                    font: { family: '"Prompt", system-ui, sans-serif', size: 11 }
                  }
                },
                y: {
                  beginAtZero: true,
                  grace: (isRevenueLine || isBookingBar) && showValues ? '15%' : '5%',
                  grid: { color: 'rgba(148, 163, 184, 0.18)' },
                  ticks: {
                    precision: 0,
                    font: { family: '"Prompt", system-ui, sans-serif', size: 11 },
                    callback: function (val) {
                      return isRevenueLine ? '฿' + fmt.number(val) : fmt.number(val);
                    }
                  }
                }
              }
        }
      });
      delete nativeState[canvasId];
      return;
    }

    var prev = nativeState[canvasId];
    nativeState[canvasId] = {
      type: type,
      labels: labels,
      values: values,
      colors: colors,
      label: opts.label || '',
      valueFmt: opts.valueFmt,
      showValues: showValues,
      animProgress: 0,
      hoverIndex: (prev && prev.hoverIndex) != null ? prev.hoverIndex : -1
    };
    attachNativeHover(canvas);
    animateNative(canvasId);
  }

  function animateNative(canvasId) {
    var s = nativeState[canvasId];
    if (!s) return;
    var startTime = null;
    var duration = 850;

    function step(ts) {
      if (!nativeState[canvasId]) return;
      if (!startTime) startTime = ts;
      var t = Math.min(1, (ts - startTime) / duration);
      s.animProgress = 1 - Math.pow(1 - t, 4);

      var canvas = document.getElementById(canvasId);
      if (canvas) drawNative(canvas, s);
      if (t < 1) requestAnimationFrame(step);
    }

    requestAnimationFrame(step);
  }

  function attachNativeHover(canvas) {
    if (canvas.getAttribute('data-native-bound') === '1') return;
    canvas.setAttribute('data-native-bound', '1');

    function updateIndexFromEvent(e) {
      var s = nativeState[canvas.id];
      if (!s || !s.values.length) return;
      var rect = canvas.getBoundingClientRect();
      var x = e.clientX - rect.left;
      var y = e.clientY - rect.top;
      var nextIdx = -1;

      if (s.type === 'doughnut') {
        var h = Number(canvas.getAttribute('data-height')) || 220;
        var cx = Math.min(h * 0.48, rect.width * 0.24);
        var cy = h / 2;
        var r = Math.min(cx, cy) - 10;
        var dx = x - cx;
        var dy = y - cy;
        var dist = Math.sqrt(dx * dx + dy * dy);
        if (dist >= r * 0.56 && dist <= r + 8) {
          var ang = Math.atan2(dy, dx) + Math.PI / 2;
          if (ang < 0) ang += Math.PI * 2;
          var total = s.values.reduce(function (a, b) { return a + (Number(b) || 0); }, 0);
          if (total > 0) {
            var acc = 0;
            for (var i = 0; i < s.values.length; i++) {
              var seg = ((Number(s.values[i]) || 0) / total) * Math.PI * 2;
              if (ang >= acc && ang <= acc + seg) {
                nextIdx = i;
                break;
              }
              acc += seg;
            }
          }
        }
      } else {
        var isRevenue = canvas.id === 'managerRevenueChart';
        var padL = isRevenue ? 68 : 52, padR = 14;
        var pw = Math.max(10, rect.width - padL - padR);
        var step = pw / s.values.length;
        var idx = Math.floor((x - padL) / step);
        if (idx >= 0 && idx < s.values.length && x >= padL && x <= rect.width - padR) {
          nextIdx = idx;
        }
      }

      if (nextIdx !== s.hoverIndex) {
        s.hoverIndex = nextIdx;
        drawNative(canvas, s);
      }
    }

    canvas.addEventListener('mousemove', updateIndexFromEvent);
    canvas.addEventListener('click', updateIndexFromEvent);
    canvas.addEventListener('mouseleave', function () {
      var s = nativeState[canvas.id];
      if (!s) return;
      s.hoverIndex = -1;
      drawNative(canvas, s);
    });
  }

  function drawNative(canvas, s) {
    var dpr = window.devicePixelRatio || 1;
    var parent = canvas.parentElement;
    var w = Math.max(280, (parent && parent.clientWidth) || 520);
    var h = Number(canvas.getAttribute('data-height')) || 220;
    canvas.style.width = w + 'px';
    canvas.style.height = h + 'px';
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);

    var ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.font = '12px "Prompt", system-ui, sans-serif';
    var text = getComputedStyle(canvas).color || '#334155';
    var vf = s.valueFmt || fmt.number;
    var p = s.animProgress == null ? 1 : s.animProgress;
    var isRevenue = canvas.id === 'managerRevenueChart';

    var total = s.values.reduce(function (a, b) { return a + (Number(b) || 0); }, 0);
    if (!s.values.length || (s.type === 'doughnut' && total === 0)) {
      ctx.fillStyle = text; ctx.textAlign = 'center';
      ctx.fillText('ไม่มีข้อมูลในช่วงที่เลือก', w / 2, h / 2);
      return;
    }

    if (s.type === 'doughnut') {
      var cx = Math.min(h * 0.48, w * 0.24);
      var cy = h / 2;
      var r = Math.min(cx, cy) - 10;
      var start = -Math.PI / 2;

      s.values.forEach(function (v, i) {
        var ang = ((Number(v) || 0) / total * Math.PI * 2) * p;
        var isHov = i === s.hoverIndex;
        var rad = isHov ? r + 4 : r;
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.arc(cx, cy, rad, start, start + ang);
        ctx.closePath();
        ctx.fillStyle = s.colors[i];
        ctx.fill();
        ctx.lineWidth = 2;
        ctx.strokeStyle = '#ffffff';
        ctx.stroke();
        start += ang;
      });

      ctx.globalCompositeOperation = 'destination-out';
      ctx.beginPath();
      ctx.arc(cx, cy, r * 0.58, 0, Math.PI * 2);
      ctx.fill();
      ctx.globalCompositeOperation = 'source-over';

      var centerNum = s.hoverIndex >= 0 && s.hoverIndex < s.values.length ? (Number(s.values[s.hoverIndex]) || 0) : total;
      var centerSub = s.hoverIndex >= 0 && s.hoverIndex < s.labels.length ? s.labels[s.hoverIndex] : (canvas.id === 'managerRoomChart' ? 'ห้องทั้งหมด' : 'ใบจองทั้งหมด');
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.font = '700 18px "Prompt", system-ui, sans-serif';
      ctx.fillStyle = '#0F172A';
      ctx.fillText(fmt.number(centerNum), cx, cy - 7);
      ctx.font = '500 10.5px "Prompt", system-ui, sans-serif';
      ctx.fillStyle = '#64748B';
      ctx.fillText(centerSub.length > 13 ? centerSub.slice(0, 12) + '…' : centerSub, cx, cy + 11);

      var legX = cx * 2 + 22;
      var maxLegW = Math.max(100, w - legX - 10);
      var rowH = Math.min(26, Math.max(20, (h - 20) / Math.max(1, s.labels.length)));
      var startY = Math.max(14, (h - s.labels.length * rowH) / 2 + rowH / 2);

      ctx.textAlign = 'left';
      ctx.textBaseline = 'middle';
      s.labels.forEach(function (label, i) {
        var y = startY + i * rowH;
        var val = Number(s.values[i]) || 0;
        var pct = total > 0 ? ((val / total) * 100).toFixed(0) : '0';
        var itemText = label + ' · ' + vf(val) + ' (' + pct + '%)';

        ctx.fillStyle = s.colors[i];
        ctx.beginPath();
        ctx.arc(legX + 5, y, 5, 0, Math.PI * 2);
        ctx.fill();

        ctx.font = (i === s.hoverIndex ? '700 ' : '500 ') + '11.5px "Prompt", system-ui, sans-serif';
        ctx.fillStyle = i === s.hoverIndex ? '#0F172A' : '#334155';
        var clipped = itemText;
        while (clipped.length > 4 && ctx.measureText(clipped).width > maxLegW - 18) {
          clipped = clipped.slice(0, -2) + '…';
        }
        ctx.fillText(clipped, legX + 16, y);
      });
      return;
    }

    var padL = isRevenue ? 68 : 52, padR = 14, padT = 24, padB = 28;
    var pw = w - padL - padR, ph = h - padT - padB;
    var max = Math.max.apply(null, s.values.map(Number));
    if (!(max > 0)) max = 1;
    ctx.strokeStyle = 'rgba(148,163,184,.22)'; ctx.fillStyle = text;
    ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
    for (var g = 0; g <= 4; g++) {
      var gy = padT + ph - (ph * g / 4);
      ctx.beginPath(); ctx.moveTo(padL, gy); ctx.lineTo(w - padR, gy); ctx.stroke();
      var yVal = vf(max * g / 4);
      ctx.fillText(isRevenue ? '฿' + yVal : yVal, padL - 8, gy);
    }
    var n = s.values.length, step = pw / n, color = s.colors[0];
    ctx.textAlign = 'center'; ctx.textBaseline = 'top';
    var every = Math.ceil(n / 8);
    s.labels.forEach(function (label, i) {
      if (i % every === 0) { ctx.fillStyle = text; ctx.fillText(label, padL + step * i + step / 2, h - padB + 8); }
    });

    if (s.type === 'bar') {
      s.values.forEach(function (v, i) {
        var raw = Number(v) || 0;
        var bh = (raw / max * ph) * p;
        var bw = Math.max(6, Math.min(28, step * 0.52));
        var bx = padL + step * i + (step - bw) / 2;
        var by = padT + ph - bh;
        var isHov = i === s.hoverIndex;

        var barGrad = ctx.createLinearGradient(0, by, 0, padT + ph);
        barGrad.addColorStop(0, isHov ? '#1D4ED8' : '#3B82F6');
        barGrad.addColorStop(1, isHov ? 'rgba(29, 78, 216, 0.7)' : 'rgba(59, 130, 246, 0.42)');
        ctx.fillStyle = barGrad;

        ctx.beginPath();
        var rad = Math.min(6, bw / 2, bh / 2);
        if (ctx.roundRect && bh > 0) {
          ctx.roundRect(bx, by, bw, bh, [rad, rad, 0, 0]);
        } else {
          ctx.rect(bx, by, bw, bh);
        }
        ctx.fill();

        if (s.showValues && raw > 0 && p > 0.6) {
          ctx.font = '700 11px "Prompt", system-ui, sans-serif';
          ctx.fillStyle = '#1D4ED8';
          ctx.textAlign = 'center';
          ctx.textBaseline = 'bottom';
          ctx.fillText(vf(raw), bx + bw / 2, by - 4);
        }
      });

      if (s.hoverIndex >= 0 && s.hoverIndex < s.values.length) {
        var hIdx = s.hoverIndex;
        var hRaw = Number(s.values[hIdx]) || 0;
        var hx = padL + step * hIdx + step / 2;
        var hy = padT + ph - ((hRaw / max * ph) * p);
        var tip = s.labels[hIdx] + ' · ' + vf(hRaw) + ' รายการ';
        ctx.font = '600 11.5px "Prompt", system-ui, sans-serif';
        var tw = ctx.measureText(tip).width + 18;
        var th = 26;
        var tx = Math.min(w - tw - 8, Math.max(8, hx - tw / 2));
        var ty = Math.max(4, hy - th - 16);

        ctx.fillStyle = '#070D1E';
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(tx, ty, tw, th, 8);
        else ctx.rect(tx, ty, tw, th);
        ctx.fill();

        ctx.fillStyle = '#00E5FF';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(tip, tx + tw / 2, ty + th / 2);
      }
    } else {
      var points = s.values.map(function (v, i) {
        var val = (Number(v) || 0) * p;
        return {
          x: padL + step * i + step / 2,
          y: padT + ph - (val / max * ph),
          raw: Number(v) || 0,
          label: s.labels[i]
        };
      });

      ctx.save();
      ctx.beginPath();
      points.forEach(function (pt, idx) {
        if (idx === 0) ctx.moveTo(pt.x, pt.y);
        else ctx.lineTo(pt.x, pt.y);
      });
      if (points.length) {
        ctx.lineTo(points[points.length - 1].x, padT + ph);
        ctx.lineTo(points[0].x, padT + ph);
        ctx.closePath();
        var grad = ctx.createLinearGradient(0, padT, 0, padT + ph);
        grad.addColorStop(0, 'rgba(16, 185, 129, 0.28)');
        grad.addColorStop(1, 'rgba(16, 185, 129, 0.01)');
        ctx.fillStyle = grad;
        ctx.fill();
      }
      ctx.restore();

      ctx.strokeStyle = color;
      ctx.lineWidth = 2.5;
      ctx.beginPath();
      points.forEach(function (pt, idx) {
        if (idx === 0) ctx.moveTo(pt.x, pt.y);
        else ctx.lineTo(pt.x, pt.y);
      });
      ctx.stroke();

      var labelStep = points.length > 24 ? Math.ceil(points.length / 12) : 1;
      points.forEach(function (pt, idx) {
        var isHovered = idx === s.hoverIndex;
        ctx.beginPath();
        ctx.arc(pt.x, pt.y, isHovered ? 6 : 4, 0, Math.PI * 2);
        ctx.fillStyle = isHovered ? '#059669' : '#ffffff';
        ctx.fill();
        ctx.lineWidth = 2;
        ctx.strokeStyle = color;
        ctx.stroke();

        if (s.showValues && pt.raw > 0 && (labelStep === 1 || idx % labelStep === 0 || idx === points.length - 1 || isHovered)) {
          ctx.font = '600 11px "Prompt", system-ui, sans-serif';
          ctx.fillStyle = '#065F46';
          ctx.textAlign = 'center';
          ctx.textBaseline = 'bottom';
          ctx.fillText('฿' + vf(pt.raw), pt.x, pt.y - 8);
        }
      });

      if (s.hoverIndex >= 0 && s.hoverIndex < points.length) {
        var hPt = points[s.hoverIndex];
        var tipText = hPt.label + ' · ฿' + vf(hPt.raw);
        ctx.font = '600 12px "Prompt", system-ui, sans-serif';
        var tw2 = ctx.measureText(tipText).width + 20;
        var th2 = 28;
        var tx2 = Math.min(w - tw2 - 8, Math.max(8, hPt.x - tw2 / 2));
        var ty2 = Math.max(4, hPt.y - th2 - 18);

        ctx.fillStyle = '#070D1E';
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(tx2, ty2, tw2, th2, 8);
        else ctx.rect(tx2, ty2, tw2, th2);
        ctx.fill();

        ctx.fillStyle = '#00E5FF';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(tipText, tx2 + tw2 / 2, ty2 + th2 / 2);
      }
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
  function initDashboard() {
    var root = $('[data-manager-dashboard]');
    if (!root) return;
    var endpoint = root.getAttribute('data-endpoint');
    if (!endpoint) return;

    var form = $('[data-manager-range-form]');
    var startInput = form ? $('[name="startDate"]', form) : null;
    var endInput = form ? $('[name="endDate"]', form) : null;

    function applyData(d) {
      lastDashboardData = d;
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

    reloadDashboardFn = function () {
      if (lastDashboardData) applyData(lastDashboardData);
      else load();
    };

    if (form) {
      form.addEventListener('submit', function (e) { e.preventDefault(); load(); });
      $$('[name="startDate"],[name="endDate"]', form).forEach(function (i) { i.addEventListener('change', load); });

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

      var toggleBtn = target.closest('[data-chart-toggle-values]');
      if (toggleBtn) {
        var cid = toggleBtn.getAttribute('data-chart-toggle-values');
        if (cid) {
          chartShowValues[cid] = !chartShowValues[cid];
          toggleBtn.classList.toggle('active', !!chartShowValues[cid]);
          if (typeof reloadDashboardFn === 'function') reloadDashboardFn();
        }
        return;
      }

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