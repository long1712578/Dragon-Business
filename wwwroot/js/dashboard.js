/**
 * Dragon PayHub — Dashboard
 * 
 * Architecture: ES Module (type="module" in HTML)
 * ─ Mọi const/let đều scoped trong module, KHÔNG leak ra window
 * ─ Không dùng inline onclick → dùng event delegation
 * ─ Sections: Config → Auth → RBAC → API → Render → UI → Bootstrap
 * 
 * Fix permanent cho bug "staffList already declared":
 * Module scope đảm bảo script chỉ execute 1 lần, const không bao giờ conflict.
 */

// ═══════════════════════════════════════════════════════════════
// SECTION 1 — CONFIG
// ═══════════════════════════════════════════════════════════════

const Config = Object.freeze({
    apiBase:      '/api',
    ssoBase:      'https://sso.longdev.store',
    ssoTokenUrl:  'https://sso.longdev.store/connect/token',
    clientId:     'WebApp',      // Public client — đã bật Password Flow + RefreshToken
    clientSecret: '',            // Public client không cần secret
    tokenKey:     'dragon_access_token',
    userKey:      'dragon_username',
    refreshKey:   'dragon_refresh_token',
    refreshMs:    15_000,        // Auto-refresh interval
});

// ═══════════════════════════════════════════════════════════════
// SECTION 2 — AUTH (Session Management)
// ═══════════════════════════════════════════════════════════════

const Auth = {
    getToken:    () => localStorage.getItem(Config.tokenKey),
    getUser:     () => localStorage.getItem(Config.userKey) || '',

    save(token, username, refreshToken) {
        localStorage.setItem(Config.tokenKey, token);
        localStorage.setItem(Config.userKey, username);
        if (refreshToken) localStorage.setItem(Config.refreshKey, refreshToken);
    },

    clear() {
        localStorage.removeItem(Config.tokenKey);
        localStorage.removeItem(Config.userKey);
        localStorage.removeItem(Config.refreshKey);
    },

    isLoggedIn: () => !!localStorage.getItem(Config.tokenKey),
};

// ═══════════════════════════════════════════════════════════════
// SECTION 3 — RBAC (Role-Based Access Control)
// Decode JWT để lấy roles — không cần extra API call
// ABP đặt roles trong claim 'role' (string hoặc array)
// ═══════════════════════════════════════════════════════════════

const RBAC = {
    getRoles() {
        const token = Auth.getToken();
        if (!token) return [];
        try {
            // Base64url decode (handle padding + url-safe chars)
            const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
            const payload = JSON.parse(atob(b64));
            // ABP role claim — có thể là string hoặc array
            const role = payload['role']
                ?? payload['roles']
                ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
                ?? [];
            return Array.isArray(role) ? role : [role];
        } catch {
            return [];
        }
    },

    isAdmin:    () => RBAC.getRoles().includes('admin'),
    isEmployee: () => RBAC.getRoles().includes('employee'),

    /** Áp dụng UI visibility theo role sau khi login/restore */
    applyUI() {
        const admin = RBAC.isAdmin();
        document.querySelectorAll('[data-role="admin"]').forEach(el => {
            el.style.display = admin ? '' : 'none';
        });
        const badge = document.getElementById('userRoleBadge');
        if (badge) badge.textContent = admin ? 'Admin' : 'Employee';
    },
};

// ═══════════════════════════════════════════════════════════════
// SECTION 4 — API CLIENT
// Centralized fetch với auto Bearer token + 401 handler
// ═══════════════════════════════════════════════════════════════

const ApiClient = {
    async fetch(url, options = {}) {
        const token = Auth.getToken();
        const res = await fetch(url, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                ...(token ? { Authorization: `Bearer ${token}` } : {}),
                ...options.headers,
            },
        });

        if (res.status === 401) {
            Auth.clear();
            UI.Login.show();
            return null;
        }
        return res;
    },

    async get(path)           { return ApiClient.fetch(`${Config.apiBase}${path}`); },
    async post(path, body)    { return ApiClient.fetch(`${Config.apiBase}${path}`, { method: 'POST',   body: JSON.stringify(body) }); },
    async del(path)           { return ApiClient.fetch(`${Config.apiBase}${path}`, { method: 'DELETE' }); },
    async put(path, body)     { return ApiClient.fetch(`${Config.apiBase}${path}`, { method: 'PUT',    body: JSON.stringify(body) }); },
};

// ═══════════════════════════════════════════════════════════════
// SECTION 5 — SSO LOGIN
// ═══════════════════════════════════════════════════════════════

const SsoAuth = {
    async login(username, password) {
        const params = {
            grant_type: 'password',
            client_id:  Config.clientId,
            username,
            password,
            scope: 'openid profile IdentityService',
        };
        if (Config.clientSecret) params.client_secret = Config.clientSecret;

        const res = await fetch(Config.ssoTokenUrl, {
            method:  'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body:    new URLSearchParams(params).toString(),
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new SsoError(err);
        }

        return res.json(); // { access_token, refresh_token, ... }
    },
};

class SsoError extends Error {
    constructor(errBody) {
        const msg = SsoError._message(errBody);
        super(msg);
        this.ssoError = errBody.error;
    }

    static _message(err) {
        if (err.error === 'invalid_client')
            return 'SSO client chưa cấu hình đúng. Kiểm tra client_id trong OpenIddict.';
        if (err.error === 'unsupported_grant_type')
            return 'SSO chưa bật Password Flow. Bật AllowPasswordFlow trong ABP OpenIddict.';
        if (err.error === 'invalid_scope')
            return `Scope không hợp lệ: ${err.error_description ?? ''}`;
        return err.error_description ?? 'Invalid username or password.';
    }
}

// ═══════════════════════════════════════════════════════════════
// SECTION 6 — STATE (App-level state, không dùng global var)
// ═══════════════════════════════════════════════════════════════

const State = {
    allStaff:     [],
    refreshTimer: null,
};

// ═══════════════════════════════════════════════════════════════
// SECTION 7 — DATA LAYER (fetch + transform)
// ═══════════════════════════════════════════════════════════════

const DataService = {
    async fetchStaff() {
        const res = await ApiClient.get('/staff');
        if (!res) return;
        try {
            const data = await res.json();
            State.allStaff = data;
            Renderer.staff(data);
            Renderer.staffSelect(data);
            Dom.statStaffCount.textContent = data.length;
        } catch (e) {
            console.error('[fetchStaff]', e);
        }
    },

    async fetchPayments() {
        const res = await ApiClient.get('/payments');
        if (!res) return;
        try {
            const data = await res.json();
            Renderer.payments(data);
            Renderer.stats(data);
        } catch (e) {
            console.error('[fetchPayments]', e);
        }
    },

    async createPayment(amount, desc, staffId) {
        return ApiClient.post('/payments/create', { amount: parseFloat(amount), desc, staffId });
    },

    async deleteStaff(id) {
        return ApiClient.del(`/staff/${id}`);
    },

    async deletePayment(orderId) {
        return ApiClient.del(`/payments/${orderId}`);
    },
};

// ═══════════════════════════════════════════════════════════════
// SECTION 8 — RENDERER (DOM → HTML, không có logic)
// ═══════════════════════════════════════════════════════════════

const Renderer = {
    staff(data) {
        Dom.staffList.innerHTML = data.length === 0
            ? '<p class="text-gray-500 text-sm text-center py-4">No staff members found.</p>'
            : data.map(s => `
                <div class="flex items-center justify-between p-3 rounded-2xl hover:bg-white/5 transition-all">
                    <div class="flex items-center space-x-4">
                        <div class="w-12 h-12 rounded-full bg-gradient-to-tr from-cyan-500 to-purple-600
                                    flex items-center justify-center text-lg font-bold">
                            ${s.name.charAt(0)}
                        </div>
                        <div>
                            <h5 class="font-semibold">${escHtml(s.name)}</h5>
                            <p class="text-xs text-gray-400">${escHtml(s.role)}</p>
                        </div>
                    </div>
                    <div class="flex items-center gap-3">
                        <div class="text-right">
                            <p class="font-bold text-cyan-400">₫${(s.totalTips || 0).toLocaleString()}</p>
                            <p class="text-[10px] text-gray-500 uppercase tracking-wider font-bold">Total Sales</p>
                        </div>
                        <button data-role="admin" data-action="delete-staff" data-id="${s.id}"
                                class="px-3 py-1 rounded-lg bg-red-500/20 hover:bg-red-500/40
                                       text-red-400 text-xs font-bold transition-all"
                                title="Xóa nhân viên">🗑</button>
                    </div>
                </div>`).join('');
        RBAC.applyUI(); // Re-apply sau khi render dynamic HTML
    },

    staffSelect(data) {
        Dom.selectStaff.innerHTML = data.map(s =>
            `<option value="${s.id}">${escHtml(s.name)} (${escHtml(s.role)})</option>`
        ).join('');
    },

    payments(data) {
        Dom.paymentTableBody.innerHTML = data.length === 0
            ? '<tr><td colspan="6" class="py-8 text-center text-gray-500">No transactions yet.</td></tr>'
            : data.map(p => `
                <tr class="border-b border-white/5 hover:bg-white/5 transition-colors">
                    <td class="py-4 font-mono text-xs text-cyan-400">#${escHtml(p.orderId)}</td>
                    <td class="py-4 font-bold">₫${p.amount.toLocaleString()}</td>
                    <td class="py-4 text-gray-300 text-sm">${escHtml(Renderer._staffName(p.staffId))}</td>
                    <td class="py-4">
                        <span class="badge ${Renderer._statusClass(p.status)}">
                            ${Renderer._statusText(p.status)}
                        </span>
                    </td>
                    <td class="py-4 text-gray-500 text-xs">${new Date(p.createdAt).toLocaleTimeString()}</td>
                    <td class="py-4">
                        <button data-role="admin" data-action="delete-payment" data-id="${escHtml(p.orderId)}"
                                class="px-2 py-1 rounded bg-red-500/20 hover:bg-red-500/40
                                       text-red-400 text-xs font-bold transition-all"
                                title="Xóa payment">🗑</button>
                    </td>
                </tr>`).join('');
        RBAC.applyUI();
    },

    stats(data) {
        const paid = data.filter(p => p.status === 2);
        Dom.statRevenue.textContent      = `₫${paid.reduce((s, p) => s + p.amount, 0).toLocaleString()}`;
        Dom.statSuccessCount.textContent = paid.length;
    },

    userAvatar(username) {
        Dom.userAvatar.title     = username;
        Dom.userAvatar.innerHTML = `<span class="font-bold text-sm">${username.charAt(0).toUpperCase()}</span>`;
    },

    _staffName: id => State.allStaff.find(x => x.id?.toString() === id?.toString())?.name ?? 'N/A',
    _statusClass: s => ({ 2: 'badge-paid', 1: 'badge-pending', 0: 'badge-created' }[s] ?? 'badge-failed'),
    _statusText:  s => (['Created', 'Pending', 'Paid', 'Failed', 'Expired'][s] ?? 'Unknown'),
};

// ═══════════════════════════════════════════════════════════════
// SECTION 9 — UI CONTROLLERS
// ═══════════════════════════════════════════════════════════════

const UI = {
    Login: {
        show() {
            Dom.loginScreen.classList.remove('hidden');
            Dom.loginError.classList.add('hidden');
            document.getElementById('loginPassword').value = '';
        },
        hide()   { Dom.loginScreen.classList.add('hidden'); },
        setLoading(loading) {
            Dom.btnLogin.disabled = loading;
            Dom.loginSpinner.classList.toggle('hidden', !loading);
            Dom.loginBtnText.textContent = loading ? 'Signing in...' : 'Sign In';
        },
        showError(msg) {
            Dom.loginError.textContent = msg;
            Dom.loginError.classList.remove('hidden');
        },
    },

    Modal: {
        open()  { Dom.paymentModal.classList.remove('hidden'); },
        close() {
            Dom.paymentModal.classList.add('hidden');
            document.getElementById('qrResult').classList.add('hidden');
        },
        showResult(paymentUrl) {
            const qrResult    = document.getElementById('qrResult');
            const paymentLink = document.getElementById('paymentLink');
            qrResult.classList.remove('hidden');
            paymentLink.href        = paymentUrl;
            paymentLink.textContent = paymentUrl;
        },
    },
};

// ═══════════════════════════════════════════════════════════════
// SECTION 10 — DOM REFERENCES (centralized, lazy-safe)
// ═══════════════════════════════════════════════════════════════

const Dom = {
    get loginScreen()      { return document.getElementById('loginScreen'); },
    get loginError()       { return document.getElementById('loginError'); },
    get loginSpinner()     { return document.getElementById('loginSpinner'); },
    get loginBtnText()     { return document.getElementById('loginBtnText'); },
    get btnLogin()         { return document.getElementById('btnLogin'); },
    get btnLogout()        { return document.getElementById('btnLogout'); },
    get userAvatar()       { return document.getElementById('userAvatar'); },
    get paymentTableBody() { return document.getElementById('paymentTableBody'); },
    get staffList()        { return document.getElementById('staffList'); },
    get statRevenue()      { return document.getElementById('statRevenue'); },
    get statSuccessCount() { return document.getElementById('statSuccessCount'); },
    get statStaffCount()   { return document.getElementById('statStaffCount'); },
    get paymentModal()     { return document.getElementById('paymentModal'); },
    get btnCreatePayment() { return document.getElementById('btnCreatePayment'); },
    get btnCloseModal()    { return document.getElementById('btnCloseModal'); },
    get btnConfirmPayment(){ return document.getElementById('btnConfirmPayment'); },
    get selectStaff()      { return document.getElementById('selectStaff'); },
};

// ═══════════════════════════════════════════════════════════════
// SECTION 11 — EVENT HANDLERS
// ═══════════════════════════════════════════════════════════════

/** XSS prevention — escape HTML entities */
function escHtml(str) {
    return String(str ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])
    );
}

async function handleLogin() {
    const username = document.getElementById('loginUsername').value.trim();
    const password = document.getElementById('loginPassword').value;
    if (!username || !password) {
        UI.Login.showError('Please enter username and password.');
        return;
    }

    UI.Login.setLoading(true);
    Dom.loginError.classList.add('hidden');

    try {
        const data = await SsoAuth.login(username, password);
        Auth.save(data.access_token, username, data.refresh_token);
        UI.Login.hide();
        Renderer.userAvatar(username);
        RBAC.applyUI();
        await App.init();
    } catch (e) {
        if (e instanceof SsoError) {
            UI.Login.showError(e.message);
        } else if (e instanceof TypeError) {
            UI.Login.showError('Không thể kết nối SSO. Kiểm tra mạng hoặc CORS policy.');
        } else {
            UI.Login.showError('Lỗi không xác định. Vui lòng thử lại.');
        }
        console.error('[handleLogin]', e);
    } finally {
        UI.Login.setLoading(false);
    }
}

async function handleLogout() {
    Auth.clear();
    if (State.refreshTimer) clearInterval(State.refreshTimer);
    State.refreshTimer = null;
    State.allStaff = [];
    Dom.paymentTableBody.innerHTML = '';
    Dom.staffList.innerHTML        = '';
    Dom.statRevenue.textContent       = '₫0';
    Dom.statSuccessCount.textContent  = '0';
    Dom.statStaffCount.textContent    = '0';
    UI.Login.show();
}

async function handleCreatePayment() {
    const amount  = document.getElementById('inputAmount').value;
    const desc    = document.getElementById('inputDesc').value;
    const staffId = Dom.selectStaff.value;
    if (!amount || !desc) { alert('Please fill in all fields'); return; }

    Dom.btnConfirmPayment.disabled    = true;
    Dom.btnConfirmPayment.textContent = 'Generating...';
    try {
        const res = await DataService.createPayment(amount, desc, staffId);
        if (!res) return;
        if (!res.ok) { alert('Failed to create payment'); return; }
        const data = await res.json();
        UI.Modal.showResult(data.paymentUrl);
        await DataService.fetchPayments();
    } catch (e) {
        console.error('[createPayment]', e);
        alert('Failed to create payment');
    } finally {
        Dom.btnConfirmPayment.disabled    = false;
        Dom.btnConfirmPayment.textContent = 'Generate QR Link';
    }
}

async function handleDeleteStaff(id) {
    if (!confirm(`Xóa nhân viên #${id}? Không thể hoàn tác!`)) return;
    const res = await DataService.deleteStaff(id);
    if (res?.ok) {
        await DataService.fetchStaff();
    } else {
        alert('Xóa thất bại. Kiểm tra quyền admin.');
    }
}

async function handleDeletePayment(orderId) {
    if (!confirm(`Xóa payment #${orderId}? Không thể hoàn tác!`)) return;
    const res = await DataService.deletePayment(orderId);
    if (res?.ok) {
        await DataService.fetchPayments();
    } else {
        alert('Xóa thất bại. Kiểm tra quyền admin.');
    }
}

// ═══════════════════════════════════════════════════════════════
// SECTION 12 — EVENT DELEGATION
// Thay thế inline onclick → delegate từ document/container
// Lý do: HTML render động trong renderStaff/renderPayments
// không thể gắn listener trực tiếp vào từng button
// ═══════════════════════════════════════════════════════════════

function registerEvents() {
    // Login
    Dom.btnLogin.addEventListener('click', handleLogin);
    document.getElementById('loginPassword')
        .addEventListener('keydown', e => { if (e.key === 'Enter') handleLogin(); });

    // Logout
    Dom.btnLogout.addEventListener('click', handleLogout);

    // Modal
    Dom.btnCreatePayment.addEventListener('click', () => UI.Modal.open());
    Dom.btnCloseModal.addEventListener('click',    () => UI.Modal.close());
    Dom.btnConfirmPayment.addEventListener('click', handleCreatePayment);

    // Event delegation — xóa staff/payment (dynamic buttons)
    document.addEventListener('click', e => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;

        const action = btn.dataset.action;
        const id     = btn.dataset.id;

        if (action === 'delete-staff')   handleDeleteStaff(id);
        if (action === 'delete-payment') handleDeletePayment(id);
    });
}

// ═══════════════════════════════════════════════════════════════
// SECTION 13 — APP BOOTSTRAP
// ═══════════════════════════════════════════════════════════════

const App = {
    async init() {
        if (State.refreshTimer) clearInterval(State.refreshTimer);
        await DataService.fetchStaff();
        await DataService.fetchPayments();
        State.refreshTimer = setInterval(() => {
            DataService.fetchStaff();
            DataService.fetchPayments();
        }, Config.refreshMs);
    },

    start() {
        registerEvents();

        if (Auth.isLoggedIn()) {
            UI.Login.hide();
            Renderer.userAvatar(Auth.getUser());
            RBAC.applyUI();
            App.init();
        } else {
            UI.Login.show();
        }
    },
};

// ── Entry Point ──
App.start();
