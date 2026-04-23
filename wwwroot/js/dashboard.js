// ─────────────────────────────────────────────────────────────
// Config
// ─────────────────────────────────────────────────────────────
const API_BASE   = '/api';
const SSO_TOKEN  = 'https://sso.longdev.store/connect/token';
const CLIENT_ID  = 'dragon-business-dashboard';
const TOKEN_KEY  = 'dragon_access_token';
const USER_KEY   = 'dragon_username';

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
        const body = new URLSearchParams({
            grant_type: 'password',
            client_id:  CLIENT_ID,
            username,
            password,
            scope: 'openid profile',
        });

        const res = await fetch(SSO_TOKEN, {
            method:  'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body:    body.toString(),
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            showLoginError(err.error_description || 'Invalid username or password.');
            return;
        }

        const data = await res.json();
        saveSession(data.access_token, username);
        hideLoginScreen();
        setUserAvatar(username);
        init();
    } catch (e) {
        showLoginError('Cannot connect to SSO. Please try again.');
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
                <div class="text-right">
                    <p class="font-bold text-cyan-400">₫${(s.totalTips || 0).toLocaleString()}</p>
                    <p class="text-[10px] text-gray-500 uppercase tracking-wider font-bold">Total Sales</p>
                </div>
            </div>`).join('');
}

function renderStaffSelect(data) {
    selectStaff.innerHTML = data.map(s =>
        `<option value="${s.id}">${s.name} (${s.role})</option>`).join('');
}

function renderPayments(data) {
    paymentTableBody.innerHTML = data.length === 0
        ? '<tr><td colspan="5" class="py-8 text-center text-gray-500">No transactions yet.</td></tr>'
        : data.map(p => `
            <tr class="border-b border-white/5 hover:bg-white/5 transition-colors">
                <td class="py-4 font-mono text-xs text-cyan-400">#${p.orderId}</td>
                <td class="py-4 font-bold">₫${p.amount.toLocaleString()}</td>
                <td class="py-4 text-gray-300 text-sm">${getStaffName(p.staffId)}</td>
                <td class="py-4"><span class="badge ${getStatusClass(p.status)}">${getStatusText(p.status)}</span></td>
                <td class="py-4 text-gray-500 text-xs">${new Date(p.createdAt).toLocaleTimeString()}</td>
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
    init();
} else {
    showLoginScreen();
}

const staffList = document.getElementById('staffList');
const statRevenue = document.getElementById('statRevenue');
const statSuccessCount = document.getElementById('statSuccessCount');
const statStaffCount = document.getElementById('statStaffCount');

const paymentModal = document.getElementById('paymentModal');
const btnCreatePayment = document.getElementById('btnCreatePayment');
const btnCloseModal = document.getElementById('btnCloseModal');
const btnConfirmPayment = document.getElementById('btnConfirmPayment');
const selectStaff = document.getElementById('selectStaff');

// Global Data
let allStaff = [];

// Init
async function init() {
    await fetchStaff();
    await fetchPayments();
    
    // Auto refresh every 10 seconds
    setInterval(() => {
        fetchPayments();
        fetchStaff();
    }, 10000);
}

// Fetch Staff
async function fetchStaff() {
    try {
        const res = await fetch(`${API_BASE}/staff`);
        const data = await res.json();
        allStaff = data;
        renderStaff(data);
        renderStaffSelect(data);
        statStaffCount.innerText = data.length;
    } catch (err) {
        console.error('Fetch staff error:', err);
    }
}

// Fetch Payments
async function fetchPayments() {
    try {
        const res = await fetch(`${API_BASE}/payments`);
        const data = await res.json();
        renderPayments(data);
        updateStats(data);
    } catch (err) {
        console.error('Fetch payments error:', err);
    }
}

// Render Staff
function renderStaff(data) {
    staffList.innerHTML = data.map(s => `
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
            <div class="text-right">
                <p class="font-bold text-cyan-400">₫${s.totalTips.toLocaleString()}</p>
                <p class="text-[10px] text-gray-500 uppercase tracking-wider font-bold">Total Sales</p>
            </div>
        </div>
    `).join('');
}

function renderStaffSelect(data) {
    selectStaff.innerHTML = data.map(s => `<option value="${s.id}">${s.name} (${s.role})</option>`).join('');
}

// Render Payments
function renderPayments(data) {
    paymentTableBody.innerHTML = data.map(p => `
        <tr class="border-b border-white/5 hover:bg-white/5 transition-colors group">
            <td class="py-4 font-mono text-xs text-cyan-400">#${p.orderId}</td>
            <td class="py-4 font-bold">₫${p.amount.toLocaleString()}</td>
            <td class="py-4 text-gray-300 text-sm">${getStaffName(p.staffId)}</td>
            <td class="py-4">
                <span class="badge ${getStatusClass(p.status)}">${getStatusText(p.status)}</span>
            </td>
            <td class="py-4 text-gray-500 text-xs">${new Date(p.createdAt).toLocaleTimeString()}</td>
        </tr>
    `).join('');
}

function getStaffName(id) {
    const s = allStaff.find(x => x.id.toString() === id);
    return s ? s.name : 'Unknown';
}

function getStatusClass(status) {
    switch (status) {
        case 2: return 'badge-paid';
        case 1: return 'badge-pending';
        case 0: return 'badge-created';
        default: return 'badge-failed';
    }
}

function getStatusText(status) {
    const names = ['Created', 'Pending', 'Paid', 'Failed', 'Expired'];
    return names[status] || 'Unknown';
}

function updateStats(data) {
    const paidOnly = data.filter(p => p.status === 2);
    const total = paidOnly.reduce((sum, p) => sum + p.amount, 0);
    statRevenue.innerText = `₫${total.toLocaleString()}`;
    statSuccessCount.innerText = paidOnly.length;
}

// Modal Logic
btnCreatePayment.onclick = () => paymentModal.classList.remove('hidden');
btnCloseModal.onclick = () => {
    paymentModal.classList.add('hidden');
    document.getElementById('qrResult').classList.add('hidden');
};

btnConfirmPayment.onclick = async () => {
    const amount = document.getElementById('inputAmount').value;
    const desc = document.getElementById('inputDesc').value;
    const staffId = document.getElementById('selectStaff').value;

    if (!amount || !desc) return alert('Please fill in all fields');

    try {
        const res = await fetch(`${API_BASE}/payments/create`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ amount: parseFloat(amount), desc, staffId })
        });
        const data = await res.json();
        
        const qrResult = document.getElementById('qrResult');
        const paymentLink = document.getElementById('paymentLink');
        
        qrResult.classList.remove('hidden');
        paymentLink.href = data.paymentUrl;
        paymentLink.innerText = data.paymentUrl;
        
        fetchPayments(); // Refresh list
    } catch (err) {
        alert('Failed to create payment');
    }
};

// Start
init();
