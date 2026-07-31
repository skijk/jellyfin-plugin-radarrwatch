(() => {
    'use strict';

    const selector = '.jellyseerr-request-button[data-tmdb-id]';
    const pending = new Set();
    const known = new Map();
    let timer = 0;

    function mark(button, status, displayText) {
        if (!button || !status?.monitored) return;
        button.disabled = true;
        button.dataset.radarrWatch = 'monitored';
        button.classList.remove(
            'jellyseerr-button-request',
            'jellyseerr-button-error');
        button.classList.add('jellyseerr-button-pending', 'arr-watch-monitored');
        button.setAttribute(
            'aria-label',
            status.hasFile ? 'Available in Radarr' : 'Monitored in Radarr');
        button.title = status.hasFile
            ? 'Radarr reports that this movie has a file'
            : 'Already monitored in Radarr';
        button.replaceChildren();
        const label = document.createElement('span');
        label.textContent = displayText || 'Requested';
        const icon = document.createElement('span');
        icon.className = 'material-icons arr-watch-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = status.hasFile ? 'check_circle' : 'schedule';
        button.append(label, icon);
    }

    function applyKnown(root = document) {
        root.querySelectorAll?.(selector).forEach(button => {
            if (String(button.dataset.mediaType || '').toLowerCase() !== 'movie') {
                return;
            }
            const id = Number(button.dataset.tmdbId);
            if (known.has(id)) {
                mark(button, known.get(id).status, known.get(id).displayText);
            }
        });
    }

    async function flush() {
        timer = 0;
        const ids = Array.from(pending).slice(0, 100);
        ids.forEach(id => pending.delete(id));
        if (!ids.length || !window.ApiClient?.ajax) return;

        try {
            const response = await ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('ArrWatch/Status', {
                    tmdbIds: ids.join(',')
                }),
                dataType: 'json'
            });
            const displayText = response.displayText || response.DisplayText || 'Requested';
            ids.forEach(id => {
                known.set(id, {
                    status: { tmdbId: id, monitored: false, hasFile: false },
                    displayText
                });
            });
            (response.movies || response.Movies || []).forEach(rawStatus => {
                const status = {
                    tmdbId: Number(rawStatus.tmdbId ?? rawStatus.TmdbId),
                    monitored: Boolean(rawStatus.monitored ?? rawStatus.Monitored),
                    hasFile: Boolean(rawStatus.hasFile ?? rawStatus.HasFile)
                };
                known.set(status.tmdbId, {
                    status,
                    displayText
                });
            });
            applyKnown();
        } catch (error) {
            console.debug('Arr Watch status lookup failed', error);
        }

        if (pending.size) timer = window.setTimeout(flush, 50);
    }

    function scan(root = document) {
        root.querySelectorAll?.(selector).forEach(button => {
            if (String(button.dataset.mediaType || '').toLowerCase() !== 'movie') {
                return;
            }
            const id = Number(button.dataset.tmdbId);
            if (!Number.isInteger(id) || id <= 0) return;
            if (known.has(id)) {
                mark(button, known.get(id).status, known.get(id).displayText);
            } else {
                pending.add(id);
            }
        });
        if (pending.size && !timer) timer = window.setTimeout(flush, 80);
    }

    const observer = new MutationObserver(mutations => {
        mutations.forEach(mutation => {
            mutation.addedNodes.forEach(node => {
                if (node.nodeType !== Node.ELEMENT_NODE) return;
                if (node.matches?.(selector)) scan(node.parentElement || node);
                else if (node.querySelector?.(selector)) scan(node);
            });
        });
    });

    function start() {
        scan();
        observer.observe(document.body, { childList: true, subtree: true });
        window.setInterval(() => scan(), 750);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
