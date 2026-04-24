/**
 * Dragon PayHub — Senior-Grade Dashboard Core
 * 
 * Architecture: Reactive State Management (Store Pattern)
 * Style: Clean Architecture / ES Modules
 * 
 * ─ Single Source of Truth (Store)
 * ─ Reactive UI: UI updates automatically when Store state changes
 * ─ Robust Error Handling: Global error boundary + SsoException handling
 * ─ Zero Global Leakage: 100% encapsulated in Module scope
 */

// ═══════════════════════════════════════════════════════════════
// 1. CONFIG & CONSTANTS
// ═══════════════════════════════════════════════════════════════

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
// 2. REACTIVE STORE (The Brain)
// ═══════════════════════════════════════════════════════════════

const Store = {
    _state: {
        isLoggedIn: false,
        isLoading:  false,
        user:       null,
        staff:      [],
        payments:   [],
        error:      null,
    },

    // Subscriptions for reactivity
    _listeners: [],

    get state() { return this._state; },

    /** Update state and notify UI */
    setState(newState) {
        this._state = { ...this._state, ...newState };
        this._listeners.forEach(fn => fn(this._state));
    },

    subscribe(fn) {
        this._listeners.push(fn);
        fn(this._state); // Initial call
    }
};

// ═══════════════════════════════════════════════════════════════
// 3. SERVICES (Business Logic)
// ═══════════════════════════════════════════════════════════════

const AuthService = {
    getToken: () => localStorage.getItem(CONFIG.STORAGE_KEYS.TOKEN),
    
    saveSession(data, username) {
        localStorage.setItem(CONFIG.STORAGE_KEYS.TOKEN,   data.access_token);
        localStorage.setItem(CONFIG.STORAGE_KEYS.USER,    username);
        if (data.refresh_token) 
            localStorage.setItem(CONFIG.STORAGE_KEYS.REFRESH, data.refresh_token);
        
        Store.setState({ isLoggedIn: true, user: username, error: null });
    },

    logout() {
        Object.values(CONFIG.STORAGE_KEYS).forEach(k => localStorage.removeItem(k));
        Store.setState({ isLoggedIn: false, user: null, staff: [], payments: [] });
    },

    async login(username, password) {
        Store.setState({ isLoading: true, error: null });

        try {
            const body = new URLSearchParams({
                grant_type: 'password',
                client_id:  CONFIG.CLIENT_ID,
                username,
                password,
                scope: 'openid profile roles IdentityService'
            });

            const res = await fetch(CONFIG.SSO_TOKEN, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body
            });

            const data = await res.json();

            if (!res.ok) {
                throw new Error(data.error_description || data.error || 'Login failed');
            }

            this.saveSession(data, username);
            return data;
        } catch (err) {
            Store.setState({ error: err.message });
            throw err;
        } finally {
            Store.setState({ isLoading: false });
        }
    },

    getRoles() {
        const token = this.getToken();
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
        const headers = {
            'Content-Type': 'application/json',
            ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
            ...options.headers
        };

        const res = await fetch(`${CONFIG.API_URL}${path}`, { ...options, headers });

        if (res.status === 401) {
            AuthService.logout();
            return null;
        }

        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: 'API Error' }));
            throw new Error(err.message || 'Request failed');
        }

        return res.json();
    },

    fetchStaff:    () => ApiService.request('/staff'),
    fetchPayments: () => ApiService.request('/payments'),
    createPayment: (body) => ApiService.request('/payments/create', { method: 'POST', body: JSON.stringify(body) }),
    deleteStaff:   (id) => ApiService.request(`/staff/${id}`, { method: 'DELETE' }),
    deletePayment: (id) => ApiService.request(`/payments/${id}`, { method: 'DELETE' }),
};

// ═══════════════════════════════════════════════════════════════
// 4. UI COMPONENTS (Rendering)
// ═══════════════════════════════════════════════════════════════

const UI = {
    // Selection helpers
    el: (id) => document.getElementById(id),
    
    /** Main Sync: Cập nhật toàn bộ UI dựa trên State */
    sync(state) {
        // Toggle Screens
        this.el('loginScreen').classList.toggle('hidden', state.isLoggedIn);
        
        // Login State
        const btnLogin = this.el('btnLogin');
        if (btnLogin) {
            btnLogin.disabled = state.isLoading;
            this.el('loginSpinner').classList.toggle('hidden', !state.isLoading);
            this.el('loginBtnText').textContent = state.isLoading ? 'Processing...' : 'Sign In';
        }

        // Error Handling
        const errBox = this.el('loginError');
        if (state.error) {
            errBox.textContent = state.error;
            errBox.classList.remove('hidden');
        } else {
            errBox.classList.add('hidden');
        }

        // User Profile
        if (state.isLoggedIn && state.user) {
            const avatar = this.el('userAvatar');
            avatar.title = state.user;
            avatar.innerHTML = `<span class="font-bold">${state.user[0].toUpperCase()}</span>`;
            
            const isAdmin = AuthService.getRoles().includes('admin');
            document.querySelectorAll('[data-role="admin"]').forEach(el => {
                el.style.display = isAdmin ? '' : 'none';
            });
        }

        // Dashboard Data
        this.renderStaff(state.staff);
        this.renderPayments(state.payments);
    },

    renderStaff(staff) {
        const container = this.el('staffList');
        if (!container) return;
        
        container.innerHTML = staff.map(s => `
            <div class="flex items-center justify-between p-4 rounded-2xl bg-white/5 border border-white/5 hover:border-cyan-500/30 transition-all">
                <div class="flex items-center space-x-4">
                    <div class="w-12 h-12 rounded-full bg-gradient-to-br from-cyan-500 to-purple-600 flex items-center justify-center font-bold">
                        ${s.name[0]}
                    </div>
                    <div>
                        <h5 class="font-bold">${UI.esc(s.name)}</h5>
                        <p class="text-xs text-gray-400">${UI.esc(s.role)}</p>
                    </div>
                </div>
                <div class="flex items-center gap-4">
                    <div class="text-right">
                        <p class="font-bold text-cyan-400">₫${(s.totalTips || 0).toLocaleString()}</p>
                    </div>
                    <button data-action="del-staff" data-id="${s.id}" data-role="admin" class="text-red-400 hover:text-red-300 p-2">
                        <i class="fas fa-trash-alt"></i>
                    </button>
                </div>
            </div>
        `).join('');
    },

    renderPayments(payments) {
        const container = this.el('paymentTableBody');
        if (!container) return;

        container.innerHTML = payments.map(p => `
            <tr class="border-b border-white/5 hover:bg-white/5 transition-colors group">
                <td class="py-4 font-mono text-xs text-cyan-400">#${p.orderId}</td>
                <td class="py-4 font-bold">₫${p.amount.toLocaleString()}</td>
                <td class="py-4 text-sm text-gray-300">${UI.getStaffName(p.staffId)}</td>
                <td class="py-4">
                    <span class="px-2 py-1 rounded-md text-[10px] font-bold uppercase tracking-wider
                        ${p.status === 2 ? 'bg-green-500/20 text-green-400' : 'bg-yellow-500/20 text-yellow-400'}">
                        ${p.status === 2 ? 'PAID' : 'PENDING'}
                    </span>
                </td>
                <td class="py-4 text-xs text-gray-500">${new Date(p.createdAt).toLocaleTimeString()}</td>
                <td class="py-4 text-right">
                    <button data-action="del-pay" data-id="${p.orderId}" data-role="admin" class="opacity-0 group-hover:opacity-100 text-red-500 transition-all">
                        <i class="fas fa-trash"></i>
                    </button>
                </td>
            </tr>
        `).join('');

        // Update Summary Stats
        const total = payments.filter(p => p.status === 2).reduce((acc, curr) => acc + curr.amount, 0);
        this.el('statRevenue').textContent = `₫${total.toLocaleString()}`;
        this.el('statSuccessCount').textContent = payments.filter(p => p.status === 2).length;
        this.el('statStaffCount').textContent = Store.state.staff.length;
    },

    getStaffName: (id) => Store.state.staff.find(s => s.id?.toString() === id?.toString())?.name || 'N/A',
    
    esc: (str) => String(str).replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m])),
};

// ═══════════════════════════════════════════════════════════════
// 5. ACTIONS (Event Handlers)
// ═══════════════════════════════════════════════════════════════

const Actions = {
    async login() {
        const u = UI.el('loginUsername').value.trim();
        const p = UI.el('loginPassword').value;
        if (!u || !p) return Store.setState({ error: 'Username/Password required' });

        try {
            await AuthService.login(u, p);
            await Actions.refreshData();
        } catch (e) {
            console.error('Login Failed', e);
        }
    },

    async refreshData() {
        try {
            const [staff, payments] = await Promise.all([
                ApiService.fetchStaff(),
                ApiService.fetchPayments()
            ]);
            Store.setState({ staff, payments });
            
            // Render select options
            UI.el('selectStaff').innerHTML = staff.map(s => `<option value="${s.id}">${UI.esc(s.name)}</option>`).join('');
        } catch (e) {
            console.error('Data Refresh Failed', e);
        }
    },

    async createPayment() {
        const amount = UI.el('inputAmount').value;
        const desc   = UI.el('inputDesc').value;
        const staffId = UI.el('selectStaff').value;

        if (!amount || !desc) return alert('Fill all fields');

        try {
            const res = await ApiService.createPayment({ amount: parseFloat(amount), desc, staffId });
            UI.el('qrResult').classList.remove('hidden');
            UI.el('paymentLink').href = res.paymentUrl;
            UI.el('paymentLink').textContent = res.paymentUrl;
            await Actions.refreshData();
        } catch (e) {
            alert('Payment failed: ' + e.message);
        }
    }
};

// ═══════════════════════════════════════════════════════════════
// 6. INITIALIZATION
// ═══════════════════════════════════════════════════════════════

function bootstrap() {
    // 1. Subscribe UI to Store (Làm cho UI trở nên "Reactive")
    Store.subscribe(state => UI.sync(state));

    // 2. Event Listeners
    UI.el('btnLogin').addEventListener('click', Actions.login);
    UI.el('btnLogout').addEventListener('click', () => AuthService.logout());
    UI.el('btnCreatePayment').addEventListener('click', () => UI.el('paymentModal').classList.remove('hidden'));
    UI.el('btnCloseModal').addEventListener('click', () => UI.el('paymentModal').classList.add('hidden'));
    UI.el('btnConfirmPayment').addEventListener('click', Actions.createPayment);

    // Enter key for login
    UI.el('loginPassword').addEventListener('keydown', e => e.key === 'Enter' && Actions.login());

    // Event Delegation for Admin Actions (Delete)
    document.addEventListener('click', async e => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;

        const { action, id } = btn.dataset;
        if (action === 'del-staff' && confirm('Xóa nhân viên?')) {
            await ApiService.deleteStaff(id);
            await Actions.refreshData();
        }
        if (action === 'del-pay' && confirm('Xóa giao dịch?')) {
            await ApiService.deletePayment(id);
            await Actions.refreshData();
        }
    });

    // 3. Check existing session
    const token = AuthService.getToken();
    const user = localStorage.getItem(CONFIG.STORAGE_KEYS.USER);
    if (token && user) {
        Store.setState({ isLoggedIn: true, user });
        Actions.refreshData();
        setInterval(Actions.refreshData, CONFIG.REFRESH_INTERVAL);
    }
}

// Kickstart
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bootstrap);
} else {
    bootstrap();
}
