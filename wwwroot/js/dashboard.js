/**
 * Dragon PayHub — Senior-Grade Dashboard Core
 * 
 * Architecture: Reactive State Management (Store Pattern)
 * Style: Clean Architecture / ES Modules / SPA Routing
 */

const CONFIG = Object.freeze({
    API_URL:      '/api',
    SSO_TOKEN:    'https://sso.longdev.store/connect/token',
    CLIENT_ID:    'dragon-payhub',
    CLIENT_SECRET: '', 
    STORAGE_KEYS: {
        TOKEN:   'dragon_at',
        USER:    'dragon_un',
        REFRESH: 'dragon_rt'
    },
    REFRESH_INTERVAL: 15000,
});

// ═══════════════════════════════════════════════════════════════
// 1. REACTIVE STORE
// ═══════════════════════════════════════════════════════════════
const Store = {
    _state: {
        isLoggedIn: false,
        isLoading:  false,
        user:       null,
        staff:      [],
        payments:   [],
        error:      null,
        currentView: 'Dashboard'
    },
    _listeners: [],
    get state() { return this._state; },
    setState(newState) {
        this._state = { ...this._state, ...newState };
        this._listeners.forEach(fn => fn(this._state));
    },
    subscribe(fn) {
        this._listeners.push(fn);
        fn(this._state);
    }
};

// ═══════════════════════════════════════════════════════════════
// 2. SERVICES
// ═══════════════════════════════════════════════════════════════
const AuthService = {
    getToken: () => localStorage.getItem(CONFIG.STORAGE_KEYS.TOKEN),
    saveSession(data, username) {
        localStorage.setItem(CONFIG.STORAGE_KEYS.TOKEN,   data.access_token);
        localStorage.setItem(CONFIG.STORAGE_KEYS.USER,    username);
        if (data.refresh_token) localStorage.setItem(CONFIG.STORAGE_KEYS.REFRESH, data.refresh_token);
        Store.setState({ isLoggedIn: true, user: { name: username, role: this.getRoles(data.access_token)[0] } });
    },
    logout() {
        Object.values(CONFIG.STORAGE_KEYS).forEach(k => localStorage.removeItem(k));
        Store.setState({ isLoggedIn: false, user: null });
        window.location.reload();
    },
    getRoles(token) {
        token = token || this.getToken();
        if (!token) return [];
        try {
            const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
            const roles = payload['role'] || payload['roles'] || [];
            return Array.isArray(roles) ? roles : [roles];
        } catch { return []; }
    }
};

const ApiService = {
    async request(path, options = {}) {
        const token = AuthService.getToken();
        const headers = { 'Content-Type': 'application/json', ...(token ? { 'Authorization': `Bearer ${token}` } : {}), ...options.headers };
        const res = await fetch(`${CONFIG.API_URL}${path}`, { ...options, headers });
        if (res.status === 401) return AuthService.logout();
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: 'API Error' }));
            throw new Error(err.message || 'Request failed');
        }
        return res.json();
    }
};

// ═══════════════════════════════════════════════════════════════
// 3. UI ENGINE
// ═══════════════════════════════════════════════════════════════
const UI = {
    el: (id) => document.getElementById(id),
    
    sync(state) {
        this.el('loginScreen').classList.toggle('hidden', state.isLoggedIn);
        if (!state.isLoggedIn) return;

        // Navigation Sync
        this.showView(state.currentView);

        // Header
        this.el('userRoleBadge').textContent = (state.user?.role || 'user').toUpperCase();
        
        // Stats
        const revenue = state.payments.filter(p => p.status === 2).reduce((a, b) => a + b.amount, 0);
        this.el('statRevenue').textContent = `đ${revenue.toLocaleString()}`;
        this.el('statSuccessCount').textContent = state.payments.filter(p => p.status === 2).length;
        this.el('statStaffCount').textContent = state.staff.length;

        // Recent Payments (Dashboard)
        this.el('paymentTableBody').innerHTML = state.payments.slice(0, 10).map(p => `
            <tr class="hover:bg-white/5 transition-colors group">
                <td class="py-4 font-mono text-cyan-400">#${p.orderId}</td>
                <td class="py-4 font-bold">đ${p.amount.toLocaleString()}</td>
                <td class="py-4 text-gray-400">${state.staff.find(s => s.id.toString() === p.staffId)?.name || 'N/A'}</td>
                <td class="py-4"><span class="badge ${p.status === 2 ? 'badge-paid' : 'badge-pending'}">${p.status === 2 ? 'Paid' : 'Pending'}</span></td>
                <td class="py-4 text-xs text-gray-500">${new Date(p.createdAt).toLocaleTimeString()}</td>
                <td class="py-4"><button data-action="del-pay" data-id="${p.orderId}" class="text-red-500 opacity-0 group-hover:opacity-100 transition-all"><i class="fas fa-trash"></i></button></td>
            </tr>
        `).join('');

        // Staff Ranking (Dashboard)
        this.el('staffList').innerHTML = [...state.staff].sort((a,b) => b.totalTips - a.totalTips).slice(0, 5).map((s, idx) => `
            <div class="flex items-center justify-between p-3 rounded-xl hover:bg-white/5 transition-all">
                <div class="flex items-center gap-4">
                    <div class="w-10 h-10 rounded-full bg-gradient-to-br ${idx === 0 ? 'from-yellow-400 to-orange-500' : 'from-slate-700 to-slate-800'} flex items-center justify-center font-bold">
                        ${idx + 1}
                    </div>
                    <div><p class="font-bold text-sm">${this.esc(s.name)}</p><p class="text-xs text-gray-500">${this.esc(s.role)}</p></div>
                </div>
                <div class="text-right"><p class="text-emerald-400 font-bold text-sm">đ${s.totalTips.toLocaleString()}</p></div>
            </div>
        `).join('');

        // Staff Management Table
        const mTable = this.el('staffManagementTable');
        if (mTable) {
            mTable.innerHTML = state.staff.map(s => `
                <tr class="hover:bg-white/5 transition-colors">
                    <td class="py-4 text-gray-500">#${s.id}</td>
                    <td class="py-4 font-bold">${this.esc(s.name)}</td>
                    <td class="py-4"><span class="text-xs bg-white/5 px-2 py-1 rounded">${this.esc(s.role)}</span></td>
                    <td class="py-4 text-emerald-400">đ${s.totalTips.toLocaleString()}</td>
                    <td class="py-4"><button data-action="del-staff" data-id="${s.id}" class="text-red-500 hover:scale-110 transition-transform"><i class="fas fa-user-minus"></i></button></td>
                </tr>
            `).join('');
        }
        
        // Modal Select
        const select = this.el('selectStaff');
        if (select) select.innerHTML = state.staff.map(s => `<option value="${s.id}">${this.esc(s.name)}</option>`).join('');
    },

    showView(viewName) {
        // Native AOT optimization: Minimal DOM manipulation
        document.querySelectorAll('.view-section').forEach(v => v.classList.add('hidden'));
        const target = this.el(`view${viewName}`);
        if (target) {
            target.classList.remove('hidden');
            target.classList.add('fade-in-up');
        }

        document.querySelectorAll('.nav-link').forEach(l => {
            l.classList.remove('active', 'bg-white/10', 'text-white');
            l.classList.add('text-gray-400');
        });
        const nav = this.el(`nav${viewName}`);
        if (nav) {
            nav.classList.add('active', 'bg-white/10', 'text-white');
            nav.classList.remove('text-gray-400');
        }

        // Close mobile menu if open
        const sidebar = document.querySelector('aside');
        if (window.innerWidth < 768) {
            sidebar.classList.add('hidden');
        }
    },

    initMobileMenu() {
        const btn = this.el('btnMobileMenu');
        const sidebar = document.querySelector('aside');
        if (btn && sidebar) {
            btn.onclick = (e) => {
                e.stopPropagation();
                sidebar.classList.toggle('hidden');
                sidebar.classList.add('z-[110]', 'fixed', 'inset-y-0', 'left-0', 'w-64', 'bg-[#0f172a]', 'shadow-2xl');
            };
            document.addEventListener('click', () => {
                if (window.innerWidth < 768) sidebar.classList.add('hidden');
            });
            sidebar.onclick = (e) => e.stopPropagation();
        }
    },

    esc: (str) => String(str).replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m])),
};

// ═══════════════════════════════════════════════════════════════
// 4. ACTIONS
// ═══════════════════════════════════════════════════════════════
const Actions = {
    async login() {
        const u = UI.el('loginUsername').value;
        const p = UI.el('loginPassword').value;
        try {
            await AuthService.login(u, p);
            this.refreshData();
        } catch (e) { alert(e.message); }
    },

    async refreshData() {
        try {
            const [staff, payments] = await Promise.all([
                ApiService.request('/staff'),
                ApiService.request('/payments')
            ]);
            Store.setState({ staff, payments });
        } catch (e) { console.error(e); }
    },

    async createStaff() {
        const name = UI.el('staffName').value;
        const role = UI.el('staffRole').value;
        try {
            await ApiService.request('/staff', { method: 'POST', body: JSON.stringify({ name, role }) });
            UI.el('staffModal').classList.add('hidden');
            this.refreshData();
        } catch (e) { alert(e.message); }
    },

    async deleteStaff(id) {
        if (!confirm('Xóa nhân viên?')) return;
        try {
            await ApiService.request(`/staff/${id}`, { method: 'DELETE' });
            this.refreshData();
        } catch (e) { alert(e.message); }
    },

    async createPayment() {
        const amount = parseFloat(UI.el('inputAmount').value);
        const desc = UI.el('inputDesc').value;
        const staffId = UI.el('selectStaff').value;
        try {
            const res = await ApiService.request('/payments/create', { method: 'POST', body: JSON.stringify({ amount, desc, staffId }) });
            UI.el('qrResult').classList.remove('hidden');
            UI.el('paymentLink').href = res.paymentUrl;
            UI.el('paymentLink').textContent = 'Open Payment Link';
            this.refreshData();
        } catch (e) { alert(e.message); }
    }
};

// ═══════════════════════════════════════════════════════════════
// 5. BOOTSTRAP
// ═══════════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', () => {
    Store.subscribe(state => UI.sync(state));

    // Nav
    ['Dashboard', 'Payments', 'Staff', 'Settings'].forEach(v => {
        UI.el(`nav${v}`).onclick = (e) => { e.preventDefault(); Store.setState({ currentView: v }); };
    });

    // Modals
    UI.el('btnCreatePayment').onclick = () => UI.el('paymentModal').classList.remove('hidden');
    UI.el('btnCloseModal').onclick = () => UI.el('paymentModal').classList.add('hidden');
    UI.el('btnConfirmPayment').onclick = () => Actions.createPayment();

    UI.el('btnOpenStaffModal').onclick = () => UI.el('staffModal').classList.remove('hidden');
    UI.el('btnCloseStaffModal').onclick = () => UI.el('staffModal').classList.add('hidden');
    UI.el('btnConfirmStaff').onclick = () => Actions.createStaff();

    // Auth
    UI.el('btnLogin').onclick = () => Actions.login();
    UI.el('btnLogout').onclick = () => AuthService.logout();

    // Delegation
    document.addEventListener('click', e => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;
        const { action, id } = btn.dataset;
        if (action === 'del-staff') Actions.deleteStaff(id);
        if (action === 'del-pay')   ApiService.request(`/payments/${id}`, { method: 'DELETE' }).then(() => Actions.refreshData());
    });

    // Init
    UI.initMobileMenu();
    const token = AuthService.getToken();
    if (token) {
        Store.setState({ isLoggedIn: true, user: { name: localStorage.getItem(CONFIG.STORAGE_KEYS.USER), role: AuthService.getRoles()[0] } });
        Actions.refreshData();
        setInterval(() => Actions.refreshData(), CONFIG.REFRESH_INTERVAL);
    }
});
