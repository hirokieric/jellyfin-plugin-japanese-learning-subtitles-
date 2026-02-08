/**
 * Japanese Learning Subtitles — Item Detail Page Button Injection
 *
 * Adds a "Generate Japanese Subtitles" button to the video detail page.
 * When clicked, calls the plugin API to generate .ja.srt for the current item.
 */
(function () {
    'use strict';

    const PLUGIN_ID = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
    const BUTTON_ID = 'jls-generate-btn';
    const STATUS_ID = 'jls-status-badge';

    // ── Helpers ──────────────────────────────────────────────

    function getApiUrl(path) {
        var base = ApiClient.serverAddress();
        return base + '/JapaneseLearningSubtitles/' + path;
    }

    function getHeaders() {
        return {
            'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"',
            'Content-Type': 'application/json'
        };
    }

    /**
     * Extracts the item ID from the current page URL.
     * Works for #!/details?id=xxx and /details?id=xxx patterns.
     */
    function getCurrentItemId() {
        var url = window.location.href;
        var match = url.match(/[?&]id=([a-f0-9-]+)/i);
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
            badge.textContent = '英語字幕なし — 日本語字幕を生成できません';
            badge.style.background = '#5c3a3a';
            badge.style.color = '#f5a0a0';
        } else if (status.HasJapaneseSrt) {
            var msg = '✓ 日本語字幕あり';
            if (status.Source) msg += ' (' + status.Source + ')';
            if (status.CoveragePercent != null) msg += ' — ' + status.CoveragePercent.toFixed(1) + '% カバー';
            badge.textContent = msg;
            badge.style.background = '#2d4a2d';
            badge.style.color = '#a0f5a0';
        } else {
            badge.textContent = '日本語字幕未生成 — ボタンで生成できます';
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
        btn.className = 'raised button-submit block btnJlsGenerate';
        btn.style.cssText = 'margin:0.5em 0;max-width:320px;';
        btn.innerHTML = '<span class="material-icons" style="vertical-align:middle;margin-right:4px;font-size:1.2em;">subtitles</span>'
            + '<span>日本語字幕を生成</span>';
        return btn;
    }

    function setButtonLoading(btn) {
        btn.disabled = true;
        btn.innerHTML = '<span class="material-icons" style="vertical-align:middle;margin-right:4px;font-size:1.2em;animation:spin 1s linear infinite;">sync</span>'
            + '<span>生成中...</span>';

        // Inject spin keyframe if not yet added
        if (!document.getElementById('jls-spin-style')) {
            var style = document.createElement('style');
            style.id = 'jls-spin-style';
            style.textContent = '@keyframes spin{from{transform:rotate(0deg)}to{transform:rotate(360deg)}}';
            document.head.appendChild(style);
        }
    }

    function setButtonDone(btn, success, message) {
        btn.disabled = false;

        if (success) {
            btn.innerHTML = '<span class="material-icons" style="vertical-align:middle;margin-right:4px;font-size:1.2em;">check_circle</span>'
                + '<span>' + (message || '生成完了') + '</span>';
            btn.style.background = '#2d6a2d';
        } else {
            btn.innerHTML = '<span class="material-icons" style="vertical-align:middle;margin-right:4px;font-size:1.2em;">error</span>'
                + '<span>' + (message || '生成失敗') + '</span>';
            btn.style.background = '#6a2d2d';
        }

        // Reset after 4 seconds
        setTimeout(function () {
            btn.style.background = '';
            btn.innerHTML = '<span class="material-icons" style="vertical-align:middle;margin-right:4px;font-size:1.2em;">subtitles</span>'
                + '<span>日本語字幕を生成</span>';
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

            // Refresh status
            loadStatus(itemId);
        } catch (err) {
            console.error('[JLS] Generate error:', err);
            setButtonDone(btn, false, 'ネットワークエラー');
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
                var container = document.getElementById('jls-container');
                if (container && !container.contains(badge)) {
                    container.insertBefore(badge, container.firstChild);
                }
            }
        } catch (err) {
            console.error('[JLS] Status error:', err);
        }
    }

    // ── Injection ───────────────────────────────────────────

    function injectUI(itemId) {
        // Don't inject twice
        if (document.getElementById(BUTTON_ID)) return;

        // Find the detail page's button area
        // Jellyfin 10.10 uses detailPageContent → mainDetailButtons
        // or we can append after the media info area
        var targets = [
            document.querySelector('.mainDetailButtons'),
            document.querySelector('.detailPageContent .detailButtons'),
            document.querySelector('.detailPageContent')
        ];

        var target = null;
        for (var i = 0; i < targets.length; i++) {
            if (targets[i]) {
                target = targets[i];
                break;
            }
        }

        if (!target) {
            // Retry in a moment (page might still be loading)
            setTimeout(function () { injectUI(itemId); }, 500);
            return;
        }

        // Create container
        var container = document.createElement('div');
        container.id = 'jls-container';
        container.style.cssText = 'margin:1em 0;padding:0;';

        var btn = createButton();
        btn.addEventListener('click', function () {
            onButtonClick(itemId);
        });

        container.appendChild(btn);

        // Insert after the button area, or append to detail content
        if (target.classList.contains('mainDetailButtons') || target.classList.contains('detailButtons')) {
            target.parentNode.insertBefore(container, target.nextSibling);
        } else {
            target.appendChild(container);
        }

        // Load initial status
        loadStatus(itemId);
    }

    function cleanup() {
        var el = document.getElementById('jls-container');
        if (el) el.remove();
    }

    // ── Page Navigation Listener ────────────────────────────

    function onPageChange() {
        var itemId = getCurrentItemId();

        // Only show on detail pages for video items
        var isDetailPage = window.location.href.indexOf('details') !== -1;

        if (isDetailPage && itemId) {
            // Small delay to let Jellyfin render the page first
            setTimeout(function () { injectUI(itemId); }, 300);
        } else {
            cleanup();
        }
    }

    // Watch for Jellyfin SPA navigation
    // Jellyfin uses hashchange or popstate depending on routing mode
    window.addEventListener('hashchange', onPageChange);
    window.addEventListener('popstate', onPageChange);

    // Also use MutationObserver on the main content area for robustness
    var observer = new MutationObserver(function (mutations) {
        var itemId = getCurrentItemId();
        var isDetailPage = window.location.href.indexOf('details') !== -1;
        if (isDetailPage && itemId && !document.getElementById(BUTTON_ID)) {
            setTimeout(function () { injectUI(itemId); }, 300);
        }
    });

    // Start observing when DOM is ready
    function init() {
        var mainContent = document.querySelector('#skinBody') || document.querySelector('.mainAnimatedPages') || document.body;
        observer.observe(mainContent, { childList: true, subtree: true });

        // Initial check
        onPageChange();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    console.log('[JLS] Japanese Learning Subtitles button script loaded');
})();
