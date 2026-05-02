/**
 * Dragon PayHub — Café Order Management Module v2.0
 * Kanban Board + POS Menu + Payment Checkout Integration
 */

// ─── STATE ───────────────────────────────────────────────────────
let allProducts = [];
let cart = [];       // [{ product, qty }]
let currentOrderId = null;

const STATUS = { Pending:0, Preparing:1, Ready:2, Completed:3, Cancelled:4 };
const STATUS_LABEL = ['Pending','Preparing','Ready','Completed','Cancelled'];
const STATUS_COLORS = {
    0: 'yellow', 1: 'blue', 2: 'green', 3: 'purple', 4: 'red'
};

// ─── HELPERS ─────────────────────────────────────────────────────
const $ = id => document.getElementById(id);
const esc = s => String(s ?? '').replace(/[&<>"']/g, m =>
    ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[m]));
const fmt = n => `đ${Number(n).toLocaleString('vi-VN')}`;

async function api(path, opts = {}) {
    const token = localStorage.getItem('dragon_at');
    const headers = { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...opts.headers };
    const res = await fetch(`/api${path}`, { ...opts, headers });
    if (!res.ok) {
        const err = await res.json().catch(() => ({ message: 'API Error' }));
        throw new Error(err.message || 'Request failed');
    }
    return res.json();
}

// ─── KANBAN BOARD ────────────────────────────────────────────────
export async function refreshOrders() {
    try {
        const orders = await api('/orders');
        renderKanban(orders);
    } catch (e) { console.error('Orders load error:', e); }
}

function renderKanban(orders) {
    const cols = { 0: 'pending', 1: 'preparing', 2: 'ready', 3: 'completed' };
    const counts = { 0:0, 1:0, 2:0, 3:0 };

    Object.values(cols).forEach(id => {
        const el = $(`kanban-${id}`);
        if (el) el.innerHTML = '';
    });

    orders.forEach(o => {
        if (o.status > 3) return;
        counts[o.status]++;
        const col = $(`kanban-${cols[o.status]}`);
        if (!col) return;
        col.insertAdjacentHTML('beforeend', buildCard(o));
    });

    // Update badges & stats
    [0,1,2,3].forEach(s => {
        const badgeEl = $(`badge-${cols[s]}`);
        if (badgeEl) badgeEl.textContent = counts[s];
    });
    if ($('orderStatPending'))   $('orderStatPending').textContent   = counts[0];
    if ($('orderStatPreparing')) $('orderStatPreparing').textContent = counts[1];
    if ($('orderStatReady'))     $('orderStatReady').textContent     = counts[2];
    if ($('orderStatCompleted')) $('orderStatCompleted').textContent = orders.filter(o=>o.status===3).length;
}

function buildCard(o) {
    const color = STATUS_COLORS[o.status];
    const nextActions = getNextActions(o);
    return `
    <div class="bg-[#0d1627] border border-white/8 rounded-2xl p-4 hover:border-${color}-500/30 transition-all cursor-pointer group"
         data-order-id="${o.id}">
        <div class="flex justify-between items-start mb-3">
            <div>
                <span class="text-xs font-mono text-${color}-400 bg-${color}-500/10 px-2 py-0.5 rounded-lg">#${o.id}</span>
                <span class="text-xs text-gray-500 ml-2">Bàn ${esc(o.tableNumber)}</span>
            </div>
            <span class="text-xs text-gray-500">${new Date(o.createdAt).toLocaleTimeString('vi-VN',{hour:'2-digit',minute:'2-digit'})}</span>
        </div>
        ${o.customerName ? `<p class="text-sm font-semibold mb-2 text-white">${esc(o.customerName)}</p>` : ''}
        <p class="text-lg font-bold text-emerald-400 mb-3">${fmt(o.totalAmount)}</p>
        <div class="flex gap-2 flex-wrap">
            ${nextActions.map(a => `
                <button data-order-id="${o.id}" data-action-type="${a.type}" data-action-val="${a.val}"
                    class="text-xs px-3 py-1.5 rounded-lg font-bold transition-all ${a.cls}">
                    <i class="fas ${a.icon} mr-1"></i>${a.label}
                </button>`).join('')}
            <button data-order-id="${o.id}" data-action-type="detail"
                class="text-xs px-3 py-1.5 rounded-lg font-bold text-gray-400 hover:text-white bg-white/5 hover:bg-white/10 transition-all ml-auto">
                <i class="fas fa-eye mr-1"></i>Detail
            </button>
        </div>
    </div>`;
}

function getNextActions(o) {
    const actions = [];
    if (o.status === STATUS.Pending) {
        actions.push({ type:'status', val:1, label:'Bắt đầu làm', icon:'fa-blender', cls:'text-blue-400 bg-blue-500/10 hover:bg-blue-500/20' });
        actions.push({ type:'status', val:4, label:'Huỷ', icon:'fa-times', cls:'text-red-400 bg-red-500/10 hover:bg-red-500/20' });
    } else if (o.status === STATUS.Preparing) {
        actions.push({ type:'status', val:2, label:'Xong - Ready!', icon:'fa-check', cls:'text-green-400 bg-green-500/10 hover:bg-green-500/20' });
    } else if (o.status === STATUS.Ready) {
        actions.push({ type:'checkout', val:0, label:'Checkout 💳', icon:'fa-qrcode', cls:'text-cyan-400 bg-cyan-500/10 hover:bg-cyan-500/20' });
    }
    return actions;
}

// ─── PRODUCT MENU & CART ─────────────────────────────────────────
async function loadMenu() {
    try {
        allProducts = await api('/menu');
        renderMenu(allProducts);
    } catch (e) { 
        $('menuProductGrid').innerHTML = `<p class="col-span-3 text-red-400 text-center py-4">${e.message}</p>`;
    }
}

function renderMenu(products) {
    const grid = $('menuProductGrid');
    if (!grid) return;
    if (!products.length) {
        grid.innerHTML = '<p class="col-span-3 text-gray-500 text-center py-8">Không có sản phẩm nào</p>';
        return;
    }
    grid.innerHTML = products.filter(p => p.isAvailable).map(p => `
        <button data-prod-id="${p.id}" data-add-product
            class="text-left p-4 rounded-2xl bg-white/5 border border-white/8 hover:border-emerald-500/40 hover:bg-emerald-500/5 transition-all group">
            <p class="font-semibold text-sm group-hover:text-emerald-400 transition-colors">${esc(p.name)}</p>
            <p class="text-xs text-gray-400 mt-1">${esc(p.category)}</p>
            <p class="text-emerald-400 font-bold mt-2">${fmt(p.price)}</p>
        </button>
    `).join('');
}

function addToCart(productId) {
    const p = allProducts.find(x => x.id === productId);
    if (!p) return;
    const existing = cart.find(c => c.product.id === productId);
    if (existing) existing.qty++;
    else cart.push({ product: p, qty: 1 });
    renderCart();
}

function removeFromCart(productId) {
    cart = cart.filter(c => c.product.id !== productId);
    renderCart();
}

function changeQty(productId, delta) {
    const item = cart.find(c => c.product.id === productId);
    if (!item) return;
    item.qty = Math.max(1, item.qty + delta);
    renderCart();
}

function renderCart() {
    const cartEl = $('orderCart');
    const totalEl = $('cartTotal');
    if (!cartEl) return;

    if (!cart.length) {
        cartEl.innerHTML = '<p class="text-gray-500 text-xs text-center pt-8">Chưa có món nào</p>';
        if (totalEl) totalEl.textContent = 'đ0';
        return;
    }

    const total = cart.reduce((s, c) => s + c.product.price * c.qty, 0);
    cartEl.innerHTML = cart.map(c => `
        <div class="flex items-center gap-2 p-2 rounded-xl bg-white/5">
            <div class="flex-1 min-w-0">
                <p class="text-xs font-semibold truncate">${esc(c.product.name)}</p>
                <p class="text-xs text-emerald-400">${fmt(c.product.price)}</p>
            </div>
            <div class="flex items-center gap-1 flex-shrink-0">
                <button data-cart-dec="${c.product.id}" class="w-6 h-6 rounded-lg bg-white/10 hover:bg-white/20 text-xs font-bold transition-all">−</button>
                <span class="w-6 text-center text-xs font-bold">${c.qty}</span>
                <button data-cart-inc="${c.product.id}" class="w-6 h-6 rounded-lg bg-white/10 hover:bg-white/20 text-xs font-bold transition-all">+</button>
                <button data-cart-del="${c.product.id}" class="w-6 h-6 rounded-lg bg-red-500/20 hover:bg-red-500/40 text-red-400 text-xs transition-all ml-1">✕</button>
            </div>
        </div>
    `).join('');
    if (totalEl) totalEl.textContent = fmt(total);
}

// ─── ORDER CREATION ──────────────────────────────────────────────
async function createOrder(staffList) {
    if (!cart.length) { alert('Vui lòng chọn ít nhất 1 món!'); return; }
    const table = $('orderTable')?.value?.trim() || '1';
    const customer = $('orderCustomer')?.value?.trim() || null;
    const note = $('orderNote')?.value?.trim() || null;
    const staffId = $('orderStaff')?.value || null;

    const payload = {
        tableNumber: table,
        customerName: customer,
        note,
        staffId,
        items: cart.map(c => ({ productId: c.product.id, quantity: c.qty, customNote: null }))
    };

    try {
        const res = await api('/orders', { method: 'POST', body: JSON.stringify(payload) });
        alert(`✅ Đã tạo đơn hàng #${res.id} - Bàn ${table}`);
        cart = [];
        renderCart();
        closeOrderModal();
        await refreshOrders();
    } catch (e) { alert('Lỗi tạo đơn: ' + e.message); }
}

// ─── ORDER DETAIL MODAL ──────────────────────────────────────────
export async function openOrderDetail(orderId) {
    currentOrderId = orderId;
    try {
        const o = await api(`/orders/${orderId}`);
        $('orderDetailTitle').textContent = `Order #${o.id}`;
        $('orderDetailTable').textContent = `Bàn ${o.tableNumber}${o.customerName ? ' • ' + o.customerName : ''}`;
        $('orderDetailTotal').textContent = fmt(o.totalAmount);

        // Items list
        $('orderDetailItems').innerHTML = (o.items || []).map(i => `
            <div class="flex justify-between text-sm py-2 border-b border-white/5">
                <span class="text-gray-300">${esc(i.productName)} × ${i.quantity}</span>
                <span class="text-emerald-400 font-bold">${fmt(i.unitPrice * i.quantity)}</span>
            </div>
        `).join('') || '<p class="text-gray-500 text-sm">Không có items</p>';

        // Action buttons based on status
        const actionsEl = $('orderDetailActions');
        const checkoutEl = $('checkoutSection');
        actionsEl.innerHTML = '';
        checkoutEl.classList.add('hidden');

        const statusMap = {
            0: [{ label:'Bắt đầu làm', val:1, cls:'bg-blue-500/20 text-blue-400 hover:bg-blue-500/30' }, { label:'Huỷ đơn', val:4, cls:'bg-red-500/20 text-red-400 hover:bg-red-500/30' }],
            1: [{ label:'✓ Xong - Ready', val:2, cls:'bg-green-500/20 text-green-400 hover:bg-green-500/30' }],
            2: [],
            3: [],
            4: []
        };

        (statusMap[o.status] || []).forEach(btn => {
            const b = document.createElement('button');
            b.className = `px-4 py-2 rounded-xl font-bold text-sm transition-all ${btn.cls}`;
            b.textContent = btn.label;
            b.onclick = async () => {
                await api(`/orders/${orderId}/status`, { method: 'PUT', body: JSON.stringify({ status: btn.val }) });
                closeOrderDetail();
                await refreshOrders();
            };
            actionsEl.appendChild(b);
        });

        if (o.status === STATUS.Ready) {
            checkoutEl.classList.remove('hidden');
            $('checkoutResult').classList.add('hidden');
        }

        $('orderDetailModal').classList.remove('hidden');
    } catch (e) { alert('Không thể tải chi tiết: ' + e.message); }
}

async function doCheckout() {
    if (!currentOrderId) return;
    const provider = $('checkoutProvider')?.value || 'Mock';
    try {
        const res = await api(`/orders/${currentOrderId}/checkout`, {
            method: 'POST',
            body: JSON.stringify({ provider, staffId: null })
        });
        const linkEl = $('checkoutPayLink');
        const resultEl = $('checkoutResult');
        if (res.paymentUrl) {
            linkEl.href = res.paymentUrl;
            linkEl.textContent = provider === 'Mock' ? 'Mở link thanh toán 🔗' : 'Mở ứng dụng ZaloPay 🔗';
            
            // Hiển thị ảnh QR
            let qrImg = $('checkoutQrImg');
            if (!qrImg) {
                qrImg = document.createElement('img');
                qrImg.id = 'checkoutQrImg';
                qrImg.style.cssText = 'width:200px;height:200px;border-radius:12px;display:block;margin:12px auto;border:4px solid rgba(99,179,237,0.3)';
                resultEl.insertBefore(qrImg, linkEl);
            }
            qrImg.src = res.paymentUrl;
            qrImg.style.display = 'block';

        } else {
            linkEl.href = '#';
            linkEl.textContent = `Payment ID: ${res.paymentOrderId}`;
            if ($('checkoutQrImg')) $('checkoutQrImg').style.display = 'none';
        }
        resultEl.classList.remove('hidden');
        await refreshOrders();
    } catch (e) { alert('Checkout lỗi: ' + e.message); }
}

// ─── MODAL HELPERS ───────────────────────────────────────────────
function openOrderModal(staffList) {
    cart = [];
    renderCart();
    if ($('orderTable')) $('orderTable').value = '1';
    if ($('orderCustomer')) $('orderCustomer').value = '';
    if ($('orderNote')) $('orderNote').value = '';

    // Populate staff select — dark themed options
    const sel = $('orderStaff');
    if (sel && staffList) {
        sel.innerHTML = '<option value="" style="background:#0d1627;color:#9ca3af">— Chọn nhân viên —</option>' +
            staffList.map(s => `<option value="${s.id}" style="background:#0d1627;color:#e5e7eb">${esc(s.name)}</option>`).join('');
    }

    loadMenu();
    $('orderModal').classList.remove('hidden');
}

function closeOrderModal() {
    $('orderModal').classList.add('hidden');
}

function closeOrderDetail() {
    $('orderDetailModal').classList.add('hidden');
    currentOrderId = null;
}

// ─── EVENT DELEGATION ────────────────────────────────────────────
export function initOrderModule(getStaffFn) {
    // Open/Close modal
    $('btnOpenOrderModal')?.addEventListener('click', () => openOrderModal(getStaffFn()));
    $('btnCloseOrderModal')?.addEventListener('click', closeOrderModal);
    $('btnCloseOrderDetail')?.addEventListener('click', closeOrderDetail);

    // Create order
    $('btnConfirmOrder')?.addEventListener('click', () => createOrder(getStaffFn()));

    // Checkout
    $('btnCheckout')?.addEventListener('click', doCheckout);

    // Category filter
    $('categoryFilters')?.addEventListener('click', e => {
        const btn = e.target.closest('[data-cat]');
        if (!btn) return;
        document.querySelectorAll('.cat-btn').forEach(b => {
            b.classList.remove('active-cat','border-emerald-500/50','text-emerald-400','bg-emerald-500/10');
            b.classList.add('border-white/10','text-gray-400');
        });
        btn.classList.add('active-cat','border-emerald-500/50','text-emerald-400','bg-emerald-500/10');
        btn.classList.remove('border-white/10','text-gray-400');
        const cat = btn.dataset.cat;
        renderMenu(cat ? allProducts.filter(p => p.category === cat) : allProducts);
    });

    // Add product to cart
    $('menuProductGrid')?.addEventListener('click', e => {
        const btn = e.target.closest('[data-add-product]');
        if (!btn) return;
        addToCart(parseInt(btn.dataset.prodId));
    });

    // Cart controls
    $('orderCart')?.addEventListener('click', e => {
        const dec = e.target.closest('[data-cart-dec]');
        const inc = e.target.closest('[data-cart-inc]');
        const del = e.target.closest('[data-cart-del]');
        if (dec) changeQty(parseInt(dec.dataset.cartDec), -1);
        if (inc) changeQty(parseInt(inc.dataset.cartInc), +1);
        if (del) removeFromCart(parseInt(del.dataset.cartDel));
    });

    // Kanban card actions (delegated on main doc)
    document.addEventListener('click', async e => {
        const actionBtn = e.target.closest('[data-action-type]');
        if (!actionBtn) return;

        const orderId = parseInt(actionBtn.dataset.orderId);
        const type    = actionBtn.dataset.actionType;
        const val     = actionBtn.dataset.actionVal;

        if (type === 'status') {
            try {
                await api(`/orders/${orderId}/status`, { method: 'PUT', body: JSON.stringify({ status: parseInt(val) }) });
                await refreshOrders();
            } catch (ex) { alert(ex.message); }
        } else if (type === 'checkout') {
            await openOrderDetail(orderId);
        } else if (type === 'detail') {
            await openOrderDetail(orderId);
        }
    });
}
