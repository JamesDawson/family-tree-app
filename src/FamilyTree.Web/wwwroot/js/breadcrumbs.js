// Recently-visited breadcrumb trail. Kept in localStorage (the app has no user identity) and rendered
// into #breadcrumbs, which sits outside the hx-boost swap target and so persists across navigation.
(function () {
    var KEY = 'familytree.trail';
    var MAX = 8;
    var TITLE_SUFFIX = ' - Family Tree';

    function load() {
        try {
            var parsed = JSON.parse(localStorage.getItem(KEY) || '[]');
            return Array.isArray(parsed) ? parsed : [];
        } catch (e) {
            return [];
        }
    }

    function save(trail) {
        try {
            localStorage.setItem(KEY, JSON.stringify(trail));
        } catch (e) { /* storage unavailable: trail is just the current page */ }
    }

    function currentLabel() {
        var title = document.title || location.pathname;
        return title.endsWith(TITLE_SUFFIX) ? title.slice(0, -TITLE_SUFFIX.length) : title;
    }

    function render(trail) {
        var nav = document.getElementById('breadcrumbs');
        if (!nav) { return; }
        nav.textContent = '';
        var list = document.createElement('ol');
        trail.forEach(function (entry, i) {
            var item = document.createElement('li');
            if (i === trail.length - 1) {
                item.textContent = entry.label;
                item.setAttribute('aria-current', 'page');
            } else {
                var link = document.createElement('a');
                link.href = entry.url;
                link.textContent = entry.label;
                item.appendChild(link);
            }
            list.appendChild(item);
        });
        nav.appendChild(list);
    }

    // reorder=false (back/forward) keeps an existing entry where it is, so stepping through history
    // doesn't scramble the trail.
    function record(reorder) {
        var url = location.pathname + location.search;
        var label = currentLabel();
        var trail = load();
        var index = trail.findIndex(function (e) { return e.url === url; });

        if (index >= 0 && !reorder) {
            trail[index].label = label;
        } else {
            if (index >= 0) { trail.splice(index, 1); }
            trail.push({ url: url, label: label });
        }

        trail = trail.slice(-MAX);
        save(trail);
        render(trail);
    }

    function forgetPerson(path) {
        var match = /^\/people\/([^\/]+)\/delete$/.exec(path);
        if (!match) { return; }
        var prefix = '/people/' + match[1];
        var trail = load().filter(function (e) {
            return e.url !== prefix && e.url.indexOf(prefix + '/') !== 0 && e.url.indexOf(prefix + '?') !== 0;
        });
        save(trail);
    }

    document.addEventListener('DOMContentLoaded', function () { record(true); });
    document.body.addEventListener('htmx:afterSettle', function () { record(true); });
    document.body.addEventListener('htmx:historyRestore', function () { record(false); });
    // beforeOnLoad rather than afterRequest: a delete answers with HX-Redirect (a full page load), and
    // afterRequest doesn't reliably fire for that.
    document.body.addEventListener('htmx:beforeOnLoad', function (evt) {
        var detail = evt.detail || {};
        var info = detail.pathInfo;
        if (info && detail.xhr && detail.xhr.status >= 200 && detail.xhr.status < 400) {
            forgetPerson(info.requestPath);
        }
    });
    window.addEventListener('popstate', function () { record(false); });
})();
