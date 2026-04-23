// API URLs
const API_BASE = '/api';

// DOM Elements
const paymentTableBody = document.getElementById('paymentTableBody');
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
