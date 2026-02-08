/**
 * Japanese Learning Subtitles — Item Detail Page Button Injection
 *
 * Adds a "Generate Japanese Subtitles" button to the video detail page.
 * When clicked, calls the plugin API to generate .ja.srt for the current item.
 */
(function () {
    'use strict';

    var BUTTON_ID = 'jls-generate-btn';
    var STATUS_ID = 'jls-status-badge';
    var CONTAINER_ID = 'jls-container';

    // ── Helpers ──────────────────────────────────────────────

    function getApiUrl(path) {
        var base = window.ApiClient ? ApiClient.serverAddress() : '';
        return base + '/JapaneseLearningSubtitles/' + path;
    }

    function getHeaders() {
        var headers = { 'Content-Type': 'application/json' };
        if (window.ApiClient && ApiClient.accessToken()) {
            headers['Authorization'] = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
        }
        return headers;
    }

    /**
     * Extracts the item ID from the current page URL.
     * Works for #!/details?id=xxx and /details?id=xxx patterns.
     */
    function getCurrentItemId() {
        var url = window.location.href + window.location.hash;
        var match = url.match(/[?&#]id=([a-f0-9]+)/i);
        return match ? match[1] : null;
    }

    // ── Status Badge ────────────────────────────────────────

    function createStatusBadge(status) {
        var badge = document.getElementById(STATUS_ID);
        if (!badge) {
            badge = document.createElement('div');
            badge.id = STATUS_ID;
            badge.style.cssText = 'margin:0.5em 0;padding:0.4em 0.8em;border-radius:4px;font-size:0.85em;display:inline-block;';
        }

        if (!status.HasEnglishSrt) {
            badge.textContent = '\u82f1\u8a9e\u5b57\u5e55\u306a\u3057 \u2014 \u65e5\u672c\u8a9e\u5b57\u5e55\u3092\u751f\u6210\u3067\u304d\u307e\u305b\u3093';
            badge.style.background = '#5c3a3a';
            badge.style.color = '#f5a0a0';
        } else if (status.HasJapaneseSrt) {
            var msg = '\u2713 \u65e5\u672c\u8a9e\u5b57\u5e55\u3042\u308a';
            if (status.Source) msg += ' (' + status.Source + ')';
            if (status.CoveragePercent != null) msg += ' \u2014 ' + status.CoveragePercent.toFixed(1) + '% \u30ab\u30d0\u30fc';
            badge.textContent = msg;
            badge.style.background = '#2d4a2d';
            badge.style.color = '#a0f5a0';
        } else {
            badge.textContent = '\u65e5\u672c\u8a9e\u5b57\u5e55\u672a\u751f\u6210';
            badge.style.background = '#4a4a2d';
            badge.style.color = '#f5f5a0';
        }

        return badge;
    }

    // ── Button ──────────────────────────────────────────────

    function createButton() {
        var btn = document.createElement('button');
        btn.id = BUTTON_ID;
        btn.type = 'button';
        btn.className = 'raised button-submit block btnJlsGenerate emby-button';
        btn.style.cssText = 'margin:0.8em 0;max-width:320px;display:flex;align-items:center;justify-content:center;gap:6px;padding:0.6em 1.2em;';
        btn.innerHTML = '<span class="material-icons" style="font-size:1.2em;">subtitles</span>'
            + '<span>\u65e5\u672c\u8a9e\u5b57\u5e55\u3092\u751f\u6210</span>';
        return btn;
    }

    function setButtonLoading(btn) {
        btn.disabled = true;
        btn.innerHTML = '<span class="material-icons" style="font-size:1.2em;animation:jls-spin 1s linear infinite;">sync</span>'
            + '<span>\u751f\u6210\u4e2d...</span>';

        if (!document.getElementById('jls-spin-style')) {
            var style = document.createElement('style');
            style.id = 'jls-spin-style';
            style.textContent = '@keyframes jls-spin{from{transform:rotate(0deg)}to{transform:rotate(360deg)}}';
            document.head.appendChild(style);
        }
    }

    function setButtonDone(btn, success, message) {
        btn.disabled = false;

        if (success) {
            btn.innerHTML = '<span class="material-icons" style="font-size:1.2em;">check_circle</span>'
                + '<span>' + (message || '\u751f\u6210\u5b8c\u4e86') + '</span>';
            btn.style.background = '#2d6a2d';
        } else {
            btn.innerHTML = '<span class="material-icons" style="font-size:1.2em;">error</span>'
                + '<span>' + (message || '\u751f\u6210\u5931\u6557') + '</span>';
            btn.style.background = '#6a2d2d';
        }

        setTimeout(function () {
            btn.style.background = '';
            btn.innerHTML = '<span class="material-icons" style="font-size:1.2em;">subtitles</span>'
                + '<span>\u65e5\u672c\u8a9e\u5b57\u5e55\u3092\u751f\u6210</span>';
        }, 4000);
    }

    async function onButtonClick(itemId) {
        var btn = document.getElementById(BUTTON_ID);
        if (!btn || btn.disabled) return;

        setButtonLoading(btn);

        try {
            var resp = await fetch(getApiUrl(itemId + '/Generate'), {
                method: 'POST',
                headers: getHeaders()
            });

            var data = await resp.json();
            setButtonDone(btn, data.Success, data.Message);
            loadStatus(itemId);
        } catch (err) {
            console.error('[JLS] Generate error:', err);
            setButtonDone(btn, false, '\u30cd\u30c3\u30c8\u30ef\u30fc\u30af\u30a8\u30e9\u30fc');
        }
    }

    // ── Status Loading ──────────────────────────────────────

    async function loadStatus(itemId) {
        try {
            var resp = await fetch(getApiUrl(itemId + '/Status'), {
                method: 'GET',
                headers: getHeaders()
            });

            if (resp.ok) {
                var status = await resp.json();
                var badge = createStatusBadge(status);
                var container = document.getElementById(CONTAINER_ID);
                if (container && !container.contains(badge)) {
                    container.insertBefore(badge, container.firstChild);
                }
            }
        } catch (err) {
            console.error('[JLS] Status error:', err);
        }
    }

    // ── Injection ───────────────────────────────────────────

    /**
     * Finds the best injection point on the detail page.
     *
     * Strategy (in priority order):
     *   1. Subtitle select (.selectSubtitles) → its closest .selectContainer → insertAfter
     *   2. Track selections wrapper (.trackSelections) → insertAfter
     *   3. Main detail buttons (.mainDetailButtons) → insertAfter
     *   4. Broader fallbacks (.detailSection, #itemDetailPage)
     */
    function findInjectionPoint() {
        var target, method;

        // Strategy 1: Right after the subtitle dropdown container
        var subtitleSelect = document.querySelector('.selectSubtitles');
        if (subtitleSelect) {
            // Walk up to the .selectContainer wrapper
            var selectContainer = subtitleSelect.closest('.selectContainer');
            if (selectContainer) {
                return { element: selectContainer, method: 'afterend' };
            }
            // Fallback: use the subtitle select's parent
            return { element: subtitleSelect.parentElement, method: 'afterend' };
        }

        // Strategy 2: After the track selections block (contains video/audio/subtitle selects)
        target = document.querySelector('.trackSelections');
        if (target) {
            return { element: target, method: 'afterend' };
        }

        // Strategy 3: After the main detail buttons (play, shuffle, heart, etc.)
        target = document.querySelector('.mainDetailButtons');
        if (target) {
            return { element: target, method: 'afterend' };
        }

        // Strategy 4: Inside detailSection or itemDetailPage
        target = document.querySelector('.detailSection')
              || document.querySelector('.itemPropsContainer')
              || document.querySelector('#itemDetailPage');
        if (target) {
            return { element: target, method: 'beforeend' };
        }

        return null;
    }

    var _injectRetries = 0;

    function injectUI(itemId) {
        // Don't inject twice
        if (document.getElementById(BUTTON_ID)) return;

        var point = findInjectionPoint();

        if (!point) {
            _injectRetries++;
            if (_injectRetries < 15) {
                setTimeout(function () { injectUI(itemId); }, 500);
            } else {
                console.warn('[JLS] Could not find injection point after retries');
                _injectRetries = 0;
            }
            return;
        }
        _injectRetries = 0;

        // Create container
        var container = document.createElement('div');
        container.id = CONTAINER_ID;
        container.style.cssText = 'margin:0.8em 0 0.5em 0;padding:0;clear:both;';

        var btn = createButton();
        btn.addEventListener('click', function () {
            onButtonClick(itemId);
        });

        container.appendChild(btn);

        // Insert based on method
        if (point.method === 'afterend') {
            point.element.insertAdjacentElement('afterend', container);
        } else if (point.method === 'beforeend') {
            point.element.appendChild(container);
        }

        console.log('[JLS] Button injected via:', point.element.className || point.element.id);

        // Load initial status
        loadStatus(itemId);
    }

    function cleanup() {
        var el = document.getElementById(CONTAINER_ID);
        if (el) el.remove();
        _injectRetries = 0;
    }

    // ── Page Navigation Listener ────────────────────────────

    function isDetailPage() {
        var url = window.location.href + window.location.hash;
        return url.indexOf('details') !== -1 || url.indexOf('item') !== -1;
    }

    function onPageChange() {
        var itemId = getCurrentItemId();

        if (isDetailPage() && itemId) {
            setTimeout(function () { injectUI(itemId); }, 500);
        } else {
            cleanup();
        }
    }

    // Watch for Jellyfin SPA navigation
    window.addEventListener('hashchange', onPageChange);
    window.addEventListener('popstate', onPageChange);

    // MutationObserver for robustness (Jellyfin re-renders detail page content via SPA)
    var _observerTimer = null;
    var observer = new MutationObserver(function () {
        if (_observerTimer) return;
        _observerTimer = setTimeout(function () {
            _observerTimer = null;
            var itemId = getCurrentItemId();
            if (isDetailPage() && itemId && !document.getElementById(BUTTON_ID)) {
                injectUI(itemId);
            }
        }, 600);
    });

    // Start observing when DOM is ready
    function init() {
        var root = document.querySelector('#skinBody')
                || document.querySelector('.mainAnimatedPages')
                || document.querySelector('.view')
                || document.body;
        observer.observe(root, { childList: true, subtree: true });

        // Initial check
        onPageChange();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    console.log('[JLS] Japanese Learning Subtitles button script loaded v2');
})();
