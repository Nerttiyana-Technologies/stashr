// Theme persistence + small UI helpers for the stashr console.
window.stashrTheme = {
    get: function () {
        try { return localStorage.getItem('stashr-theme') || 'light'; } catch (e) { return 'light'; }
    },
    set: function (t) {
        try { localStorage.setItem('stashr-theme', t); } catch (e) { }
        document.documentElement.setAttribute('data-theme', t);
    },
    init: function () {
        var t = null;
        try { t = localStorage.getItem('stashr-theme'); } catch (e) { }
        if (!t) {
            t = (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) ? 'dark' : 'light';
        }
        document.documentElement.setAttribute('data-theme', t);
        return t;
    }
};

// Session token kept in sessionStorage: survives refresh, cleared when the tab closes.
window.stashrSession = {
    get: function () { try { return sessionStorage.getItem('stashr-token'); } catch (e) { return null; } },
    set: function (t) { try { sessionStorage.setItem('stashr-token', t); } catch (e) { } },
    clear: function () { try { sessionStorage.removeItem('stashr-token'); } catch (e) { } }
};

window.stashrClipboard = {
    copy: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        return Promise.resolve();
    }
};
