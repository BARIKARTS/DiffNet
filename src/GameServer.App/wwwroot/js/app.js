const API_KEY = 'secret-admin-key-123'; // In a real project, this can be retrieved from localStorage or a login form
let metricHistory = { times: [], ccu: [], cpu: [], ram: [] }; // In case Chart.js is added in the future
let isConnected = false;

// UI Elements
const els = {
    ccu: document.getElementById('val-ccu'),
    rooms: document.getElementById('val-rooms'),
    bandwidthIn: document.getElementById('val-bw-in'),
    bandwidthOut: document.getElementById('val-bw-out'),
    packetsIn: document.getElementById('val-packets-in'),
    packetsOut: document.getElementById('val-packets-out'),
    cpu: document.getElementById('val-cpu'),
    cpuBar: document.getElementById('bar-cpu'), // Added
    memory: document.getElementById('val-mem'),
    gen0: document.getElementById('val-gen0'),
    gen1: document.getElementById('val-gen1'),
    gen2: document.getElementById('val-gen2'),
    timestamp: document.getElementById('val-timestamp'),
    statusDot: document.getElementById('status-dot'),
    statusText: document.getElementById('status-text'),
    toastContainer: document.getElementById('toast-container')
};

// To update the DOM only when there is a change in the UI (simple virtual-dom approach)
function updateUI(id, value) {
    if (els[id] && els[id].innerText !== value.toString()) {
        els[id].innerText = value;
    }
}


// SignalR Connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/dashboardHub")
    .withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000]) // Custom auto-reconnect strategy
    .configureLogging(signalR.LogLevel.Warning)
    .build();

connection.on("ReceiveMetricsTick", (data) => {
    updateUI('ccu', data.ccu);
    updateUI('rooms', data.roomCount);
    updateUI('bandwidthIn', data.bandwidthInKbps + ' KB/s');
    updateUI('bandwidthOut', data.bandwidthOutKbps + ' KB/s');
    updateUI('packetsIn', data.packetsInPerSec + ' p/s');
    updateUI('packetsOut', data.packetsOutPerSec + ' p/s');
    updateUI('cpu', data.cpuUsage + '%');
    if (els.cpuBar) els.cpuBar.style.width = Math.min(data.cpuUsage, 100) + '%';
    updateUI('memory', data.memoryUsedMb + ' MB');
    updateUI('gen0', data.gcGen0);
    updateUI('gen1', data.gcGen1);
    updateUI('gen2', data.gcGen2);
    updateUI('timestamp', data.timestamp);

    // Update Server Running Status
    const btnStart = document.getElementById('btn-start');
    const btnStop = document.getElementById('btn-stop');
    
    if (data.isRunning) {
        if (btnStart) btnStart.classList.add('hidden');
        if (btnStop) btnStop.classList.remove('hidden');
    } else {
        if (btnStart) btnStart.classList.remove('hidden');
        if (btnStop) btnStop.classList.add('hidden');
    }
});

// Connection state handlers
connection.onreconnecting(error => {
    isConnected = false;
    els.statusDot.className = 'w-3 h-3 rounded-full bg-yellow-400 live-indicator';
    els.statusText.innerText = 'Reconnecting...';
    els.statusText.className = 'text-sm text-yellow-400 font-medium';
});

connection.onreconnected(connectionId => {
    isConnected = true;
    els.statusDot.className = 'w-3 h-3 rounded-full bg-green-500 live-indicator';
    els.statusText.innerText = 'Live';
    els.statusText.className = 'text-sm text-green-400 font-medium tracking-wider uppercase';
    showToast('Reconnected to server', 'success');
});

connection.onclose(error => {
    isConnected = false;
    els.statusDot.className = 'w-3 h-3 rounded-full bg-red-500';
    els.statusText.innerText = 'Disconnected';
    els.statusText.className = 'text-sm text-red-500 font-medium';
    showToast('Connection lost', 'error');
});

async function start() {
    try {
        await connection.start();
        isConnected = true;
        els.statusDot.className = 'w-3 h-3 rounded-full bg-green-500 live-indicator';
        els.statusText.innerText = 'Live';
        els.statusText.className = 'text-sm text-green-400 font-medium tracking-wider uppercase';
        showToast('Connected to Game Server', 'success');
    } catch (err) {
        console.error(err);
        els.statusDot.className = 'w-3 h-3 rounded-full bg-red-500';
        els.statusText.innerText = 'Connection Failed';
        els.statusText.className = 'text-sm text-red-500 font-medium';
        setTimeout(start, 5000);
    }
}

// REST API Helpers
async function apiPost(endpoint, body = null) {
    try {
        const response = await fetch(`/api/admin/${endpoint}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Api-Key': API_KEY
            },
            body: body ? JSON.stringify(body) : null
        });
        
        const data = await response.json();
        if (response.ok) {
            showToast(data.message, 'success');
        } else {
            showToast(data.message || 'Action failed', 'error');
        }
    } catch (err) {
        showToast('Network error during API call', 'error');
    }
}

// Controller Actions
window.actionKick = function() {
    const pId = prompt("Enter Player ID to kick:");
    if (pId) apiPost(`kick/${pId}`);
}

window.actionCloseRoom = function() {
    const rId = prompt("Enter Room ID to close:");
    if (rId) apiPost(`room/${rId}/close`);
}

window.actionBroadcast = function() {
    const msg = prompt("Enter message to broadcast:");
    if (msg) apiPost('broadcast', { message: msg });
}

window.actionStartServer = function() {
    apiPost('server/start');
}

window.actionStopServer = function() {
    if (confirm("Are you sure you want to stop the game server? All players will be disconnected!")) {
        apiPost('server/stop');
    }
}

// UI Utilities
function showToast(message, type = 'info') {
    const toast = document.createElement('div');
    toast.className = 'toast flex items-center gap-3';
    
    let icon = '';
    let color = '';
    if (type === 'success') {
        icon = '<i class="fa-solid fa-check-circle text-green-400"></i>';
        color = 'border-green-500/30';
    } else if (type === 'error') {
        icon = '<i class="fa-solid fa-triangle-exclamation text-red-400"></i>';
        color = 'border-red-500/30';
    } else {
        icon = '<i class="fa-solid fa-info-circle text-blue-400"></i>';
        color = 'border-blue-500/30';
    }

    toast.classList.add(color);
    toast.innerHTML = `${icon} <span class="text-sm font-medium">${message}</span>`;
    
    els.toastContainer.appendChild(toast);
    
    // Trigger animation
    setTimeout(() => toast.classList.add('show'), 10);
    
    // Remove after 3s
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    start();
});
