/* Dashboard front-end helpers. Three small modules expose to window:
 *   DashRealtime — single SignalR connection per page, filtered by scope.
 *   DashFeed     — prepends LiveEvent rows into a list container.
 *   DashCharts   — Chart.js wrappers for visits / devices / geo.
 */
(function (g) {
  'use strict';

  var DashRealtime = {
    start: function (opts, onMessage) {
      var conn = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/dashboard')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

      conn.on('liveEvent', function (p) { onMessage && onMessage('liveEvent', p); });
      conn.on('activeUsers', function (p) { onMessage && onMessage('activeUsers', p); });

      conn.onreconnecting(function () { opts.onStatus && opts.onStatus('connecting'); });
      conn.onreconnected(function () { opts.onStatus && opts.onStatus('connected'); rejoin(); });
      conn.onclose(function () { opts.onStatus && opts.onStatus('disconnected'); });

      function rejoin() {
        if (opts.scope === 'all') conn.invoke('JoinAll');
        else if (opts.scope === 'campaign') conn.invoke('JoinCampaign', Number(opts.campaignId));
        else if (opts.scope === 'recipient') conn.invoke('JoinRecipient', Number(opts.recipientId));
      }

      conn.start().then(function () {
        opts.onStatus && opts.onStatus('connected');
        rejoin();
      }).catch(function (e) {
        opts.onStatus && opts.onStatus('disconnected');
        console.error('SignalR start failed', e);
      });

      return conn;
    }
  };

  var DashFeed = {
    prepend: function (containerId, payload) {
      var container = document.getElementById(containerId);
      if (!container) return;
      var row = document.createElement('div');
      row.className = 'live-row';
      row.innerHTML =
        '<div class="live-time">' + formatTime(payload.at) + '</div>' +
        '<div class="live-body">' +
          badge(payload.eventTypeName) +
          ' <code>' + escape(payload.maskedNtn || '****') + '</code>' +
          ' <span class="text-secondary small">on ' + escape(payload.campaignCode || '') + '</span>' +
          (payload.eventValue ? ' <span class="ms-2 text-info small">' + escape(payload.eventValue) + '</span>' : '') +
          ' <span class="text-secondary small ms-2">· ' + escape(payload.city || '-') + ', ' + escape(payload.country || '-') +
            ' · ' + escape(payload.browser || '-') + ' · ' + escape(payload.deviceType || '-') + '</span>' +
        '</div>';
      container.insertBefore(row, container.firstChild);
      // Keep the list small so DOM doesn't grow forever on long-lived dashboards.
      while (container.childElementCount > 200) container.removeChild(container.lastChild);
    }
  };

  function badge(text) {
    return '<span class="badge bg-dark border border-secondary">' + escape(text) + '</span>';
  }

  function escape(s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function formatTime(iso) {
    try {
      var d = new Date(iso);
      return d.toTimeString().slice(0, 8);
    } catch (_) { return ''; }
  }

  var DashCharts = {
    renderVisits: function (canvasId, points) {
      return new Chart(document.getElementById(canvasId), {
        type: 'line',
        data: {
          labels: points.map(function (p) { return new Date(p.bucket).toLocaleString(); }),
          datasets: [{ data: points.map(function (p) { return p.value; }), label: 'Visits', borderColor: '#38bdf8', backgroundColor: 'rgba(56,189,248,.15)', tension: .3, fill: true }]
        },
        options: { plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true, ticks: { color: '#94a3b8' } }, x: { ticks: { color: '#94a3b8' } } } }
      });
    },
    renderDevices: function (canvasId, rows) {
      return new Chart(document.getElementById(canvasId), {
        type: 'doughnut',
        data: {
          labels: rows.map(function (r) { return r.deviceType; }),
          datasets: [{ data: rows.map(function (r) { return r.sessions; }), backgroundColor: ['#38bdf8', '#34d399', '#fbbf24', '#f87171', '#a78bfa'] }]
        },
        options: { plugins: { legend: { labels: { color: '#e2e8f0' } } } }
      });
    },
    renderGeo: function (tableId, rows) {
      var tb = document.getElementById(tableId).querySelector('tbody');
      tb.innerHTML = rows.map(function (r) {
        return '<tr><td>' + escape(r.country || '-') + '</td><td>' + escape(r.city || '-') + '</td><td class="text-end">' + r.sessions + '</td></tr>';
      }).join('') || '<tr><td colspan="3" class="text-secondary text-center small">No data</td></tr>';
    }
  };

  /**
   * Animates a target integer count from 0 to its rendered value.
   * Usage: <span data-count-up>1234</span>  — call DashAnim.countUp() once on DOM ready.
   */
  var DashAnim = {
    countUp: function (selector) {
      var nodes = document.querySelectorAll(selector || '[data-count-up]');
      nodes.forEach(function (el) {
        var raw = el.textContent.trim();
        // Preserve any non-digit prefix/suffix (e.g. "75.00%" or "$1,234").
        var match = raw.match(/^([^\d-]*)(-?\d+(?:[\.,]\d+)?)(.*)$/);
        if (!match) return;
        var prefix = match[1], target = parseFloat(match[2].replace(/,/g, '')), suffix = match[3];
        if (!isFinite(target)) return;
        var isFloat = match[2].indexOf('.') !== -1;
        var duration = 900;
        var start = performance.now();
        function tick(now) {
          var t = Math.min(1, (now - start) / duration);
          var eased = 1 - Math.pow(1 - t, 3);   // easeOutCubic
          var v = target * eased;
          el.textContent = prefix + (isFloat ? v.toFixed(1) : Math.round(v).toLocaleString()) + suffix;
          if (t < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
      });
    }
  };

  // Run count-up on any decorated cell after DOM ready.
  document.addEventListener('DOMContentLoaded', function () {
    DashAnim.countUp();
  });

  /**
   * Generic live-data binder used by every dashboard page. On each
   * SignalR `liveEvent` push (debounced) and on a periodic timer, calls
   * opts.url and hands the JSON to opts.onData. opts.onActiveUsers is
   * fired separately for the `activeUsers` push.
   */
  var DashLive = {
    attach: function (opts) {
      var debounceTimer = null;

      function refresh() {
        fetch(opts.url, { credentials: 'include' })
          .then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status + ' on ' + opts.url);
            return r.json();
          })
          .then(function (d) {
            try { opts.onData && opts.onData(d); }
            catch (e) { console.error('DashLive.onData failed for ' + opts.url, e); }
          })
          .catch(function (e) { console.warn('DashLive refresh failed', e); });
      }
      function debouncedRefresh() {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(refresh, opts.debounceMs || 600);
      }

      var iv = setInterval(refresh, opts.intervalMs || 20000);
      refresh();

      DashRealtime.start(opts.hubScope || { scope: 'all' }, function (kind, payload) {
        if (kind === 'liveEvent') {
          if (opts.onLiveEvent) opts.onLiveEvent(payload);
          debouncedRefresh();
        }
        if (kind === 'activeUsers' && opts.onActiveUsers) opts.onActiveUsers(payload);
      });

      return { refresh: refresh, stop: function () { clearInterval(iv); } };
    },

    /**
     * Updates [data-field=...] inside scope to a new value, briefly flashing
     * the element if the value actually changed. Skips count-up to avoid
     * re-animating from 0 on every refresh.
     */
    setField: function (scope, field, value) {
      var el = scope.querySelector('[data-field="' + field + '"]');
      if (!el) return;
      var current = el.textContent.trim();
      var next = String(value);
      if (current === next) return;
      el.textContent = next;
      el.classList.remove('flash');
      // Force reflow so the animation restarts.
      void el.offsetWidth;
      el.classList.add('flash');
    }
  };

  g.DashRealtime = DashRealtime;
  g.DashFeed = DashFeed;
  g.DashCharts = DashCharts;
  g.DashAnim = DashAnim;
  g.DashLive = DashLive;
})(window);
