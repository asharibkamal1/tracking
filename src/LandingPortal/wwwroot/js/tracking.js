/*
 * Taxpayer Analytics Tracking SDK
 * -----------------------------------------------------------------
 *  Public surface:
 *    TPTrack.init({ apiBaseUrl, token, campaignCode, heartbeatSeconds })
 *    TPTrack.trackEvent(eventTypeNameOrInt, dataObject)
 *    TPTrack.flush()
 *
 *  Behaviour:
 *   - One session per page load (created via POST /api/v1/session/start).
 *   - Events buffered and flushed every 2s OR when 20 events queued.
 *   - Unload/visibility-hidden flush uses navigator.sendBeacon().
 *   - Heartbeat every 10s with current duration + scroll + video watch.
 *   - Auto-instruments:
 *       scroll depth (fires at 25/50/75/100),
 *       <video data-tptrack="video"> play/pause/complete/progress,
 *       <a data-tptrack-cta="register|file|...> clicks,
 *       page open/close.
 *   - Retries failed POST with exponential backoff (max 4).
 *   - Bots short-circuit on the server (StartSession marks the session
 *     IsBot=true; SDK still runs but server drops the batch).
 */
(function (global) {
  'use strict';

  var EVENT = {
    PageOpen: 1, PageClose: 2, Heartbeat: 3, ScrollDepth: 4,
    VideoPlay: 10, VideoPause: 11, VideoComplete: 12, VideoProgress: 13,
    RegisterClick: 20, FileClick: 21, CtaClick: 22, OutboundRedirect: 23,
    Bounce: 30, Engagement: 50, Error: 90
  };

  // apiBaseUrl defaults to '' which makes every request same-origin — that's the
  // happy path when the SDK is served from the same host as the tracking API.
  var config = {
    apiBaseUrl: '',
    token: '',
    campaignCode: '',
    heartbeatSeconds: 10,
    batchMaxSize: 20,
    batchMaxWaitMs: 2000,
    debug: true
  };

  var state = {
    sessionId: null,
    campaignId: null,
    started: false,
    isBot: false,
    queue: [],
    flushTimer: null,
    eventCounter: 0,
    pageOpenedAt: 0,
    maxScrollDepth: 0,
    videoWatchSeconds: 0,
    videoStartedAt: null
  };

  function log() { if (config.debug) try { console.log.apply(console, ['[TPTrack]'].concat([].slice.call(arguments))); } catch (_) {} }
  function ms() { return Date.now(); }
  function nextClientEventId() { state.eventCounter += 1; return state.sessionId + ':' + state.eventCounter; }

  function enqueue(eventType, data) {
    if (!state.started || state.isBot) return;
    data = data || {};
    state.queue.push({
      sessionId: state.sessionId,
      eventType: eventType,
      clientEventId: nextClientEventId(),
      clientEventTime: new Date().toISOString(),
      eventValue: data.value || null,
      durationSeconds: data.durationSeconds || null,
      scrollDepth: data.scrollDepth || null,
      pageUrl: location.href,
      extra: data.extra || null
    });
    if (state.queue.length >= config.batchMaxSize) {
      flush();
    } else if (!state.flushTimer) {
      state.flushTimer = setTimeout(flush, config.batchMaxWaitMs);
    }
  }

  function flush(useBeacon) {
    if (state.flushTimer) { clearTimeout(state.flushTimer); state.flushTimer = null; }
    if (state.queue.length === 0) return;
    var batch = state.queue.splice(0, state.queue.length);
    var body = JSON.stringify({ events: batch });
    var url = config.apiBaseUrl + '/api/v1/events/batch';

    if (useBeacon && navigator.sendBeacon) {
      try {
        var blob = new Blob([body], { type: 'application/json' });
        if (navigator.sendBeacon(url, blob)) return;
      } catch (e) { log('beacon failed', e); }
    }

    sendWithRetry(url, body, 0);
  }

  function sendWithRetry(url, body, attempt) {
    fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body,
      credentials: 'omit',
      keepalive: true
    }).then(function (r) {
      if (!r.ok) log('batch POST', r.status, url);
      if (!r.ok && r.status >= 500 && attempt < 3) scheduleRetry(url, body, attempt + 1);
    }).catch(function (e) {
      log('batch POST network error', e);
      if (attempt < 3) scheduleRetry(url, body, attempt + 1);
    });
  }

  function scheduleRetry(url, body, attempt) {
    var delay = Math.min(15000, 500 * Math.pow(2, attempt));
    setTimeout(function () { sendWithRetry(url, body, attempt); }, delay);
  }

  function startSession(callback) {
    var url = config.apiBaseUrl + '/api/v1/session/start';
    var payload = {
      token: config.token,
      screenResolution: (screen.width + 'x' + screen.height),
      language: navigator.language || 'en',
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      referrer: document.referrer || null,
      pageUrl: location.href
    };
    fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
      credentials: 'omit'
    }).then(function (r) {
      if (!r.ok) throw new Error('session_start_failed:' + r.status);
      return r.json();
    }).then(function (resp) {
      state.sessionId = resp.sessionId;
      state.campaignId = resp.campaignId;
      state.isBot = !!resp.isBot;
      state.started = true;
      state.pageOpenedAt = ms();
      config.heartbeatSeconds = resp.heartbeatSeconds || 10;
      log('session started', resp);
      callback && callback();
    }).catch(function (err) { log('session start error', err); });
  }

  function wireScroll() {
    function onScroll() {
      var docHeight = Math.max(document.body.scrollHeight, document.documentElement.scrollHeight) - window.innerHeight;
      if (docHeight <= 0) return;
      var pct = Math.min(100, Math.round((window.scrollY / docHeight) * 100));
      if (pct > state.maxScrollDepth) {
        state.maxScrollDepth = pct;
        // Only emit at quartile boundaries to keep event volume bounded.
        if (pct === 25 || pct === 50 || pct === 75 || pct === 100) {
          enqueue(EVENT.ScrollDepth, { scrollDepth: pct, durationSeconds: Math.round((ms() - state.pageOpenedAt) / 1000) });
        }
      }
    }
    window.addEventListener('scroll', throttle(onScroll, 250), { passive: true });
  }

  function wireVideo() {
    var videos = document.querySelectorAll('video[data-tptrack="video"]');
    videos.forEach(function (v) {
      v.addEventListener('play', function () {
        state.videoStartedAt = ms();
        enqueue(EVENT.VideoPlay, { value: v.currentSrc || v.src });
      });
      v.addEventListener('pause', function () {
        if (state.videoStartedAt) {
          state.videoWatchSeconds += Math.round((ms() - state.videoStartedAt) / 1000);
          state.videoStartedAt = null;
        }
        enqueue(EVENT.VideoPause, { durationSeconds: state.videoWatchSeconds });
      });
      v.addEventListener('ended', function () {
        if (state.videoStartedAt) {
          state.videoWatchSeconds += Math.round((ms() - state.videoStartedAt) / 1000);
          state.videoStartedAt = null;
        }
        var pct = v.duration ? Math.round((state.videoWatchSeconds / v.duration) * 100) : 100;
        enqueue(EVENT.VideoComplete, { durationSeconds: state.videoWatchSeconds, extra: { percent: pct } });
      });
      v.addEventListener('timeupdate', throttle(function () {
        if (!v.duration) return;
        var watched = state.videoWatchSeconds + (state.videoStartedAt ? (ms() - state.videoStartedAt) / 1000 : 0);
        var pct = Math.round((watched / v.duration) * 100);
        enqueue(EVENT.VideoProgress, { durationSeconds: Math.round(watched), extra: { percent: pct } });
      }, 5000));
    });
  }

  function wireClicks() {
    document.querySelectorAll('[data-tptrack-cta]').forEach(function (el) {
      el.addEventListener('click', function () {
        var cta = el.getAttribute('data-tptrack-cta');
        var t = cta === 'register' ? EVENT.RegisterClick
              : cta === 'file' ? EVENT.FileClick
              : EVENT.CtaClick;
        enqueue(t, {
          value: cta,
          durationSeconds: Math.round((ms() - state.pageOpenedAt) / 1000),
          extra: { timeToInteractMs: ms() - state.pageOpenedAt, campaignCode: config.campaignCode }
        });
        // Force flush so the click event is durable before the redirect drops us off-page.
        flush(true);
      });
    });
  }

  function wireUnload() {
    function finalize(reason) {
      var duration = Math.round((ms() - state.pageOpenedAt) / 1000);
      enqueue(EVENT.PageClose, { durationSeconds: duration, scrollDepth: state.maxScrollDepth, extra: { reason: reason } });
      flush(true);
    }
    window.addEventListener('pagehide', function () { finalize('pagehide'); });
    window.addEventListener('beforeunload', function () { finalize('beforeunload'); });
    document.addEventListener('visibilitychange', function () {
      if (document.visibilityState === 'hidden') flush(true);
    });
  }

  function wireHeartbeat() {
    setInterval(function () {
      if (!state.started || state.isBot) return;
      if (document.visibilityState !== 'visible') return;
      var duration = Math.round((ms() - state.pageOpenedAt) / 1000);
      fetch(config.apiBaseUrl + '/api/v1/events/heartbeat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'omit',
        keepalive: true,
        body: JSON.stringify({
          sessionId: state.sessionId,
          durationSeconds: duration,
          maxScrollDepth: state.maxScrollDepth,
          videoWatchSeconds: state.videoWatchSeconds
        })
      }).catch(function () {});
    }, config.heartbeatSeconds * 1000);
  }

  function wireError() {
    window.addEventListener('error', function (e) {
      enqueue(EVENT.Error, { value: (e.message || 'error').slice(0, 200) });
    });
  }

  function throttle(fn, wait) {
    var last = 0, timer = null;
    return function () {
      var args = arguments, ctx = this, remaining = wait - (Date.now() - last);
      if (remaining <= 0) {
        if (timer) { clearTimeout(timer); timer = null; }
        last = Date.now();
        fn.apply(ctx, args);
      } else if (!timer) {
        timer = setTimeout(function () { last = Date.now(); timer = null; fn.apply(ctx, args); }, remaining);
      }
    };
  }

  function init(opts) {
    Object.assign(config, opts || {});
    if (!config.token) { log('missing token'); return; }
    startSession(function () {
      wireScroll();
      wireVideo();
      wireClicks();
      wireUnload();
      wireHeartbeat();
      wireError();
    });
  }

  function trackEvent(eventType, data) {
    var t = typeof eventType === 'string' ? (EVENT[eventType] || EVENT.CtaClick) : eventType;
    enqueue(t, data || {});
  }

  global.TPTrack = { init: init, trackEvent: trackEvent, flush: function () { flush(false); }, EVENT: EVENT };
})(window);
