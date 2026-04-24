// ─────────────────────────────────────────────────────────────
// Config
// ─────────────────────────────────────────────────────────────
const API_BASE      = '/api';
const SSO_BASE      = 'https://sso.longdev.store';
const SSO_TOKEN     = `${SSO_BASE}/connect/token`;
// Dùng client 'WebApp' có sẵn trong SSO seed (đã bật Password Flow + RefreshToken)
// Roles: 'admin' (full access) | 'employee' (view + create only)
const CLIENT_ID     = 'WebApp';
const CLIENT_SECRET = ''; // WebApp là Public client — không cần secret
const TOKEN_KEY     = 'dragon_access_token';
const USER_KEY      = 'dragon_username';

// ─────────────────────────────────────────────────────────────
// Auth helpers
// ─────────────────────────────────────────────────────────────
function getToken()  { return localStorage.getItem(TOKEN_KEY); }
function getUser()   { return localStorage.getItem(USER_KEY) || ''; }

function saveSession(token, username) {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USER_KEY, username);
}

function clearSession() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem('dragon_refresh_token');
}

// ─────────────────────────────────────────────────────────────
// RBAC — Decode JWT để lấy roles (ABP puts roles in 'role' claim)
// Roles trong SSO seed: 'admin' | 'employee'
// ─────────────────────────────────────────────────────────────
function getRolesFromToken() {
    const token = getToken();
    if (!token) return [];
    try {
        const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
        const role = payload['role'] ?? payload['roles'] ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ?? [];
        return Array.isArray(role) ? role : [role];
    } catch { return []; }
}

function isAdmin() {
    return getRolesFromToken().includes('admin');
}

// Show/hide các phần tử admin-only sau khi login
function applyRoleUI() {
    const admin = isAdmin();
    document.querySelectorAll('.admin-only').forEach(el => {
        el.style.display = admin ? '' : 'none';
    });
    // Hiển thị role badge
    const badge = document.getElementById('userRoleBadge');
    if (badge) badge.textContent = admin ? 'Admin' : 'Employee';
}

// Authenticated fetch — auto logout on 401
async function apiFetch(url, options = {}) {
    const token = getToken();
    const headers = {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(options.headers || {}),
    };
    const res = await fetch(url, { ...options, headers });
    if (res.status === 401) {
        clearSession();
        showLoginScreen();
        return null;
    }
    return res;
}

// ─────────────────────────────────────────────────────────────
// Login / Logout UI
// ─────────────────────────────────────────────────────────────
const loginScreen    = document.getElementById('loginScreen');
const loginError     = document.getElementById('loginError');
const loginSpinner   = document.getElementById('loginSpinner');
const loginBtnText   = document.getElementById('loginBtnText');
const btnLogin       = document.getElementById('btnLogin');
const btnLogout      = document.getElementById('btnLogout');
const userAvatar     = document.getElementById('userAvatar');

function showLoginScreen() {
    loginScreen.classList.remove('hidden');
    loginError.classList.add('hidden');
    document.getElementById('loginPassword').value = '';
}

function hideLoginScreen() {
    loginScreen.classList.add('hidden');
}

function setLoginLoading(loading) {
    btnLogin.disabled = loading;
    loginSpinner.classList.toggle('hidden', !loading);
    loginBtnText.textContent = loading ? 'Signing in...' : 'Sign In';
}

function showLoginError(msg) {
    loginError.textContent = msg;
    loginError.classList.remove('hidden');
}

async function doLogin() {
    const username = document.getElementById('loginUsername').value.trim();
    const password = document.getElementById('loginPassword').value;
    if (!username || !password) {
        showLoginError('Please enter username and password.');
        return;
    }

    setLoginLoading(true);
    loginError.classList.add('hidden');

    try {
        // ABP Identity Server / OpenIddict — Resource Owner Password Flow
        // Client 'WebApp' đã được seed với GrantTypes: [password, refresh_token]
        // Scope 'IdentityService' đã được seed trong SSO → claim 'role' sẽ có trong JWT
        const params = {
            grant_type: 'password',
            client_id:  CLIENT_ID,
            username,
            password,
            scope: 'openid profile IdentityService',
        };
        // Chỉ đính kèm client_secret nếu client là confidential
        if (CLIENT_SECRET) params.client_secret = CLIENT_SECRET;

        const res = await fetch(SSO_TOKEN, {
            method:  'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body:    new URLSearchParams(params).toString(),
        });

        if (!res.ok) {
            let errMsg = 'Invalid username or password.';
            try {
                const err = await res.json();
                if (err.error === 'invalid_client')
                    errMsg = 'SSO client chưa được cấu hình đúng (client_id/secret). Kiểm tra lại OpenIddict.';
                else if (err.error === 'unsupported_grant_type')
                    errMsg = 'SSO chưa bật Password Flow. Bật AllowPasswordFlow trong ABP OpenIddict.';
                else if (err.error_description)
                    errMsg = err.error_description;
            } catch (_) { /* ignore */ }
            showLoginError(errMsg);
            return;
        }

        const data = await res.json();
        saveSession(data.access_token, username);
        if (data.refresh_token) localStorage.setItem('dragon_refresh_token', data.refresh_token);
        hideLoginScreen();
        setUserAvatar(username);
        applyRoleUI(); // ← áp dụng RBAC ngay sau login
        init();
    } catch (e) {
        // Phân biệt lỗi mạng vs CORS
        if (e instanceof TypeError && e.message.includes('fetch'))
            showLoginError('Không thể kết nối tới SSO (CORS hoặc mạng). Kiểm tra CORS policy tại sso.longdev.store.');
        else
            showLoginError('Lỗi không xác định. Vui lòng thử lại.');
        console.error('[doLogin]', e);
    } finally {
        setLoginLoading(false);
    }
}

function setUserAvatar(username) {
    userAvatar.title     = username;
    userAvatar.innerHTML = `<span class="font-bold text-sm">${username.charAt(0).toUpperCase()}</span>`;
}

btnLogin.addEventListener('click', doLogin);
document.getElementById('loginPassword').addEventListener('keydown', e => {
    if (e.key === 'Enter') doLogin();
});

btnLogout.addEventListener('click', () => {
    clearSession();
    if (refreshTimer) clearInterval(refreshTimer);
    document.getElementById('paymentTableBody').innerHTML = '';
    document.getElementById('staffList').innerHTML        = '';
    document.getElementById('statRevenue').textContent       = '₫0';
    document.getElementById('statSuccessCount').textContent  = '0';
    document.getElementById('statStaffCount').textContent    = '0';
    showLoginScreen();
});

// ─────────────────────────────────────────────────────────────
// DOM
// ─────────────────────────────────────────────────────────────
const paymentTableBody  = document.getElementById('paymentTableBody');
const staffList         = document.getElementById('staffList');
const statRevenue       = document.getElementById('statRevenue');
const statSuccessCount  = document.getElementById('statSuccessCount');
const statStaffCount    = document.getElementById('statStaffCount');
const paymentModal      = document.getElementById('paymentModal');
const btnCreatePayment  = document.getElementById('btnCreatePayment');
const btnCloseModal     = document.getElementById('btnCloseModal');
const btnConfirmPayment = document.getElementById('btnConfirmPayment');
const selectStaff       = document.getElementById('selectStaff');

let allStaff     = [];
let refreshTimer = null;

// ─────────────────────────────────────────────────────────────
// Data fetching
// ─────────────────────────────────────────────────────────────
async function fetchStaff() {
    const res = await apiFetch(`${API_BASE}/staff`);
    if (!res) return;
    try {
        const data = await res.json();
        allStaff = data;
        renderStaff(data);
        renderStaffSelect(data);
        statStaffCount.textContent = data.length;
    } catch (e) { console.error('fetchStaff', e); }
}

async function fetchPayments() {
    const res = await apiFetch(`${API_BASE}/payments`);
    if (!res) return;
    try {
        const data = await res.json();
        renderPayments(data);
        updateStats(data);
    } catch (e) { console.error('fetchPayments', e); }
}

// ─────────────────────────────────────────────────────────────
// Render
// ─────────────────────────────────────────────────────────────
function renderStaff(data) {
    staffList.innerHTML = data.length === 0
        ? '<p class="text-gray-500 text-sm text-center py-4">No staff members found.</p>'
        : data.map(s => `
            <div class="flex items-center justify-between p-3 rounded-2xl hover:bg-white/5 transition-all">
                <div class="flex items-center space-x-4">
                    <div class="w-12 h-12 rounded-full bg-gradient-to-tr from-cyan-500 to-purple-600 flex items-center justify-center text-lg font-bold">
                        ${s.name.charAt(0)}
                    </div>
                    <div>
                        <h5 class="font-semibold">${s.name}</h5>
                        <p class="text-xs text-gray-400">${s.role}</p>
                    </div>
                </div>
                <div class="flex items-center gap-3">
                    <div class="text-right">
                        <p class="font-bold text-cyan-400">₫${(s.totalTips || 0).toLocaleString()}</p>
                        <p class="text-[10px] text-gray-500 uppercase tracking-wider font-bold">Total Sales</p>
                    </div>
                    <button class="admin-only px-3 py-1 rounded-lg bg-red-500/20 hover:bg-red-500/40 text-red-400 text-xs font-bold transition-all"
                            onclick="deleteStaff(${s.id})" title="Xóa nhân viên">
                        🗑
                    </button>
                </div>
            </div>`).join('');
}

function renderStaffSelect(data) {
    selectStaff.innerHTML = data.map(s =>
        `<option value="${s.id}">${s.name} (${s.role})</option>`).join('');
}

function renderPayments(data) {
    paymentTableBody.innerHTML = data.length === 0
        ? '<tr><td colspan="6" class="py-8 text-center text-gray-500">No transactions yet.</td></tr>'
        : data.map(p => `
            <tr class="border-b border-white/5 hover:bg-white/5 transition-colors">
                <td class="py-4 font-mono text-xs text-cyan-400">#${p.orderId}</td>
                <td class="py-4 font-bold">₫${p.amount.toLocaleString()}</td>
                <td class="py-4 text-gray-300 text-sm">${getStaffName(p.staffId)}</td>
                <td class="py-4"><span class="badge ${getStatusClass(p.status)}">${getStatusText(p.status)}</span></td>
                <td class="py-4 text-gray-500 text-xs">${new Date(p.createdAt).toLocaleTimeString()}</td>
                <td class="py-4">
                    <button class="admin-only px-2 py-1 rounded bg-red-500/20 hover:bg-red-500/40 text-red-400 text-xs font-bold transition-all"
                            onclick="deletePayment('${p.orderId}')" title="Xóa payment">
                        🗑
                    </button>
                </td>
            </tr>`).join('');
}

function getStaffName(id) {
    const s = allStaff.find(x => x.id?.toString() === id?.toString());
    return s ? s.name : 'N/A';
}

function getStatusClass(status) {
    return { 2: 'badge-paid', 1: 'badge-pending', 0: 'badge-created' }[status] || 'badge-failed';
}

function getStatusText(status) {
    return ['Created', 'Pending', 'Paid', 'Failed', 'Expired'][status] ?? 'Unknown';
}

function updateStats(data) {
    const paid  = data.filter(p => p.status === 2);
    statRevenue.textContent      = `₫${paid.reduce((s, p) => s + p.amount, 0).toLocaleString()}`;
    statSuccessCount.textContent = paid.length;
}

// ─────────────────────────────────────────────────────────────
// Modal
// ─────────────────────────────────────────────────────────────
btnCreatePayment.onclick = () => paymentModal.classList.remove('hidden');
btnCloseModal.onclick    = () => {
    paymentModal.classList.add('hidden');
    document.getElementById('qrResult').classList.add('hidden');
};

btnConfirmPayment.onclick = async () => {
    const amount  = document.getElementById('inputAmount').value;
    const desc    = document.getElementById('inputDesc').value;
    const staffId = selectStaff.value;
    if (!amount || !desc) { alert('Please fill in all fields'); return; }

    btnConfirmPayment.disabled    = true;
    btnConfirmPayment.textContent = 'Generating...';
    try {
        const res = await apiFetch(`${API_BASE}/payments/create`, {
            method: 'POST',
            body:   JSON.stringify({ amount: parseFloat(amount), desc, staffId }),
        });
        if (!res) return;
        const data = await res.json();
        const qrResult    = document.getElementById('qrResult');
        const paymentLink = document.getElementById('paymentLink');
        qrResult.classList.remove('hidden');
        paymentLink.href        = data.paymentUrl;
        paymentLink.textContent = data.paymentUrl;
        fetchPayments();
    } catch (e) {
        alert('Failed to create payment');
    } finally {
        btnConfirmPayment.disabled    = false;
        btnConfirmPayment.textContent = 'Generate QR Link';
    }
};

// ─────────────────────────────────────────────────────────────
// Bootstrap
// ─────────────────────────────────────────────────────────────
async function init() {
    if (refreshTimer) clearInterval(refreshTimer);
    await fetchStaff();
    await fetchPayments();
    refreshTimer = setInterval(() => { fetchStaff(); fetchPayments(); }, 15000);
}

// On page load — restore session nếu đã có token
const existingToken = getToken();
if (existingToken) {
    hideLoginScreen();
    const u = getUser();
    if (u) setUserAvatar(u);
    applyRoleUI(); // ← restore RBAC từ token cũ
    init();
} else {
    showLoginScreen();
}

// ─────────────────────────────────────────────────────────────
// Admin actions — xóa staff / payment
// ─────────────────────────────────────────────────────────────
async function deleteStaff(id) {
    if (!confirm(`Xóa nhân viên #${id}? Không thể hoàn tác!`)) return;
    const res = await apiFetch(`${API_BASE}/staff/${id}`, { method: 'DELETE' });
    if (res && res.ok) {
        fetchStaff();
    } else {
        alert('Xóa thất bại. Kiểm tra quyền admin.');
    }
}

async function deletePayment(orderId) {
    if (!confirm(`Xóa payment #${orderId}? Không thể hoàn tác!`)) return;
    const res = await apiFetch(`${API_BASE}/payments/${orderId}`, { method: 'DELETE' });
    if (res && res.ok) {
        fetchPayments();
    } else {
        alert('Xóa thất bại. Kiểm tra quyền admin.');
    }
}


