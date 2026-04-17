import './style.css';
import { NetworkRunner } from './NetworkRunner';
import { BaseEntity } from './BaseEntity';
import { NetworkType } from './NetworkTypes';
import { NetworkWriter } from './NetworkWriter';
import { NetworkReader } from './NetworkReader';

// ═══════════════════════════════════════════════════════════════════════════
//  Box Arena — DiffNet AOI Stress Test
//  Tests: Spawn/Despawn protocol (0x04/0x05), Delta Compression, Grid AOI
// ═══════════════════════════════════════════════════════════════════════════

// ── Canvas Setup ────────────────────────────────────────────────────────────
const appDiv = document.querySelector<HTMLDivElement>('#app')!;
appDiv.innerHTML = `
  <canvas id="gameCanvas"></canvas>
  <div id="hud">
    <div id="status">
      <span id="statusDot" class="dot dot--disconnected"></span>
      <span id="statusText">Disconnected</span>
    </div>
    <div id="stats">
      <div class="stat"><span class="label">Player ID</span><span id="statId">—</span></div>
      <div class="stat"><span class="label">Server Tick</span><span id="statTick">0</span></div>
      <div class="stat"><span class="label">Objects</span><span id="statObjects">0</span></div>
      <div class="stat"><span class="label">Rx Bytes/s</span><span id="statBandwidth">0</span></div>
      <div class="stat"><span class="label">Packets/s</span><span id="statPps">0</span></div>
    </div>
    <div id="eventLog"></div>
  </div>
  <div id="connectPanel">
    <h2>⬡ Box Arena</h2>
    <p>DiffNet AOI Stress Test</p>
    <input id="serverUrl" type="text" value="ws://localhost:5000/ws" placeholder="ws://host:port/ws"/>
    <button id="connectBtn">Connect to Server</button>
    <div id="connectHint">WASD to move · AOI radius shown as white circle</div>
  </div>
`;

const canvas  = document.querySelector<HTMLCanvasElement>('#gameCanvas')!;
const ctx     = canvas.getContext('2d')!;
let W = canvas.width  = window.innerWidth;
let H = canvas.height = window.innerHeight;
window.addEventListener('resize', () => { W = canvas.width = window.innerWidth; H = canvas.height = window.innerHeight; });

// ── HUD Refs ────────────────────────────────────────────────────────────────
const $statusDot   = document.getElementById('statusDot')!;
const $statusText  = document.getElementById('statusText')!;
const $statId      = document.getElementById('statId')!;
const $statTick    = document.getElementById('statTick')!;
const $statObjects = document.getElementById('statObjects')!;
const $statBw      = document.getElementById('statBandwidth')!;
const $statPps     = document.getElementById('statPps')!;
const $eventLog    = document.getElementById('eventLog')!;
const $connectPanel = document.getElementById('connectPanel')!;
const $connectBtn  = document.getElementById('connectBtn')!;
const $serverUrl   = document.getElementById('serverUrl') as HTMLInputElement;

export class BoxEntity extends BaseEntity {
    // Basic Transform Sync: X and Z in top-down view (or X and Y for server). 
    protected defineSchema(): readonly NetworkType[] {
        // According to our mock snapshot format, server sends X (Float32) and Z (Float32).
        return [NetworkType.Float32, NetworkType.Float32] as const;
    }

    // Visual Extras
    public color: string = '#ffffff';
    public spawnedAt: number = 0;

    public onNetworkSpawn(x: number, y: number, z: number) {
        super.onNetworkSpawn(x, y, z);
        this.spawnedAt = Date.now();
        this.color = colorFor(this.prefabId, this.ownerId);
    }
}

let localPlayerId = 0;
let serverTick    = 0;

// AOI grid cell size (must match NetworkConfig.AOIGridCellSize on server = 10)
// AOI radius = 1 grid cell radius → 3×3 = 9 cells → 3 × cellSize = 30 world units
// We'll display AOI as a circle in screen space (scale: 20px per world unit)
const WORLD_SCALE  = 20;   // pixels per Unity world unit
const AOI_RADIUS_W = 30;   // world units = 3 cells × 10 units

// ── Camera ──────────────────────────────────────────────────────────────────
// Very simple camera that follows the local player
let camX = 0, camY = 0;

function worldToScreen(wx: number, wz: number): [number, number] {
    return [
        W / 2 + (wx - camX) * WORLD_SCALE,
        H / 2 + (wz - camY) * WORLD_SCALE,
    ];
}

// ── Color palette per prefabId ──────────────────────────────────────────────
const PREFAB_COLORS: Record<number, string> = {
    1: '#3498db', // Player    → Blue
    2: '#e74c3c', // NPC       → Red
    3: '#2ecc71', // Item      → Green
};
function colorFor(prefabId: number, ownerId: number): string {
    if (ownerId === localPlayerId) return '#f39c12'; // Self → Gold
    return PREFAB_COLORS[prefabId] ?? '#9b59b6';
}

// ── Input ───────────────────────────────────────────────────────────────────
const keys = { w: false, a: false, s: false, d: false };
window.addEventListener('keydown', e => { const k = e.key.toLowerCase(); if (k in keys) keys[k as keyof typeof keys] = true; });
window.addEventListener('keyup',   e => { const k = e.key.toLowerCase(); if (k in keys) keys[k as keyof typeof keys] = false; });

// Local prediction position (before server ack)
let localX = 0, localZ = 0;
const MOVE_SPEED = 0.15; // world units per ms tick (approx 60Hz)

// ── Bandwidth counters ──────────────────────────────────────────────────────
let rxBytesThisSecond = 0;
let ppsThisSecond     = 0;
let lastBwUpdate      = Date.now();

// ── Event Log ──────────────────────────────────────────────────────────────
const MAX_LOG_LINES = 8;
function logEvent(msg: string, type: 'spawn' | 'despawn' | 'info' = 'info') {
    const line = document.createElement('div');
    line.className = `log-line log-line--${type}`;
    line.textContent = `[${new Date().toLocaleTimeString()}] ${msg}`;
    $eventLog.prepend(line);
    while ($eventLog.children.length > MAX_LOG_LINES) $eventLog.lastChild?.remove();
}

// ── DiffNet Client ──────────────────────────────────────────────────────────
const client = new NetworkRunner({
    1: () => new BoxEntity(),
    2: () => new BoxEntity(),
    3: () => new BoxEntity(),
});

$connectBtn.addEventListener('click', () => {
    const url = $serverUrl.value.trim();
    $connectBtn.setAttribute('disabled', 'true');
    $connectBtn.textContent = 'Connecting…';

    if (url === 'mock') {
        startMockSimulation();
        return;
    }

    client.connect(url);
});

client.onConnected = () => {
    $statusDot.className  = 'dot dot--connecting';
    $statusText.textContent = 'Negotiating…';
    logEvent('WebSocket open — waiting for Accept packet…', 'info');
};

client.onAccepted = (playerId: number) => {
    localPlayerId = playerId;
    $connectPanel.style.display = 'none';
    $statusDot.className  = 'dot dot--connected';
    $statusText.textContent = `Connected (Player ${playerId})`;
    $statId.textContent = String(playerId);
    logEvent(`Accepted as Player ${playerId}`, 'info');

    // Start sending inputs at ~60Hz
    setInterval(tickInput, 16);
};

client.onDisconnected = () => {
    $statusDot.className  = 'dot dot--disconnected';
    $statusText.textContent = 'Disconnected';
    $connectBtn.removeAttribute('disabled');
    $connectBtn.textContent = 'Connect to Server';
    $connectPanel.style.display = 'flex';
    logEvent('Disconnected from server.', 'info');
};

// ── 0x04 Spawn ──────────────────────────────────────────────────────────────
client.onSpawn = (entity: BaseEntity) => {
    rxBytesThisSecond += 32; // approx spawn packet size
    ppsThisSecond++;

    const who = entity.ownerId === localPlayerId ? 'SELF' : `Player ${entity.ownerId}`;
    logEvent(`SPAWN obj=${entity.objectId} (${who}) prefab=${entity.prefabId}`, 'spawn');
    $statObjects.textContent = String(client.getEntities().size);
};

// ── 0x05 Despawn ────────────────────────────────────────────────────────────
client.onDespawn = (objectId: number) => {
    rxBytesThisSecond += 5;
    ppsThisSecond++;

    logEvent(`DESPAWN obj=${objectId} (left AOI)`, 'despawn');
    $statObjects.textContent = String(client.getEntities().size);
};

// ── 0x01 Snapshot ───────────────────────────────────────────────────────────
client.onSnapshot = () => {
    rxBytesThisSecond += 64; // Approximated sizes since delta reduces payload overhead
    ppsThisSecond++;
    serverTick = client.currentServerTick;
    $statTick.textContent = String(serverTick);

    for (const entity of client.getEntities().values()) {
        const box = entity as BoxEntity;
        
        // Zero-allocation: Values directly come out of the StateHistory ArrayBuffer!
        // Variable index 0 is Float32(X), 1 is Float32(Z/Y)
        const sX = box.readValue(serverTick, 0); 
        const sY = box.readValue(serverTick, 1);
        
        // Very basic interpolation directly mirroring state onto visuals
        if (sX !== undefined && sY !== undefined) {
            box.x = Number(sX);
            box.y = Number(sY);
            
            if (box.ownerId === localPlayerId) {
                localX = box.x;
                localZ = box.y;
            }
        }
    }
};

// ── Input Tick ──────────────────────────────────────────────────────────────
function tickInput() {
    let dx = 0, dz = 0;
    if (keys.w) dz -= MOVE_SPEED;
    if (keys.s) dz += MOVE_SPEED;
    if (keys.a) dx -= MOVE_SPEED;
    if (keys.d) dx += MOVE_SPEED;

    // Normalize diagonal
    if (dx !== 0 && dz !== 0) { dx *= 0.707; dz *= 0.707; }

    localX += dx;
    localZ += dz;

    // If using Mock Server, feed input back instantly to our mock variables
    if ($serverUrl.value.trim() === 'mock') {
        mockServerState.x = localX;
        mockServerState.z = localZ;
    } else {
        // Send position to server 
        client.sendInput(localX, localZ);
    }
}

// ── Bandwidth HUD Update (1/s) ──────────────────────────────────────────────
setInterval(() => {
    const now = Date.now();
    const dt  = (now - lastBwUpdate) / 1000;
    $statBw.textContent  = Math.round(rxBytesThisSecond / dt).toLocaleString();
    $statPps.textContent = Math.round(ppsThisSecond / dt).toLocaleString();
    rxBytesThisSecond = 0;
    ppsThisSecond     = 0;
    lastBwUpdate = now;
}, 1000);

// ── Render Loop ─────────────────────────────────────────────────────────────
function render() {
    // Smooth camera follow
    const selfObj = Array.from(client.getEntities().values()).find(o => o.ownerId === localPlayerId);
    if (selfObj) {
        camX += (selfObj.x - camX) * 0.12;
        camY += ((selfObj as BoxEntity).y - camY) * 0.12;
    } else {
        camX += (localX - camX) * 0.12;
        camY += (localZ - camY) * 0.12;
    }

    // ── Background ──────────────────────────────────────────────────────────
    ctx.fillStyle = '#0d1117';
    ctx.fillRect(0, 0, W, H);

    // ── World Grid ──────────────────────────────────────────────────────────
    drawGrid();

    // ── AOI Circle ──────────────────────────────────────────────────────────
    const [cx, cy] = worldToScreen(camX, camY);
    const aoiPx = AOI_RADIUS_W * WORLD_SCALE;
    ctx.beginPath();
    ctx.arc(cx, cy, aoiPx, 0, Math.PI * 2);
    ctx.fillStyle   = 'rgba(52, 152, 219, 0.04)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(52, 152, 219, 0.25)';
    ctx.lineWidth   = 1.5;
    ctx.stroke();

    // ── Network Objects ──────────────────────────────────────────────────────
    const now = Date.now();
    for (const entity of client.getEntities().values()) {
        const obj = entity as BoxEntity;
        const [sx, sy] = worldToScreen(obj.x, obj.y);
        const age      = now - obj.spawnedAt;
        const isLocal  = obj.ownerId === localPlayerId;
        const size     = isLocal ? 14 : 10;

        // Spawn flash: briefly glow white
        const flashT   = Math.max(0, 1 - age / 400);
        const baseColor = obj.color;

        // Shadow
        ctx.shadowBlur  = isLocal ? 16 : (flashT > 0 ? 20 : 6);
        ctx.shadowColor = flashT > 0 ? '#ffffff' : baseColor;

        // Box body
        ctx.fillStyle = flashT > 0
            ? `rgba(255,255,255,${0.4 + 0.6 * flashT})`
            : baseColor;
        ctx.fillRect(sx - size, sy - size, size * 2, size * 2);

        // Border
        ctx.shadowBlur  = 0;
        ctx.strokeStyle = isLocal ? '#f39c12' : 'rgba(255,255,255,0.2)';
        ctx.lineWidth   = isLocal ? 2.5 : 1;
        ctx.strokeRect(sx - size, sy - size, size * 2, size * 2);

        // Label (only show if close to screen center — performance guard)
        if (Math.abs(sx - W/2) < 400 && Math.abs(sy - H/2) < 300) {
            ctx.fillStyle = 'rgba(255,255,255,0.5)';
            ctx.font      = '10px monospace';
            ctx.textAlign = 'center';
            ctx.fillText(isLocal ? '★ YOU' : `#${obj.objectId}`, sx, sy - size - 4);
        }
    }

    ctx.shadowBlur = 0;

    requestAnimationFrame(render);
}

function drawGrid() {
    const cellPx   = 10 * WORLD_SCALE; // 10 world units × 20px = 200px
    const offsetX  = (camX * WORLD_SCALE) % cellPx;
    const offsetY  = (camY * WORLD_SCALE) % cellPx;
    const startX   = (W / 2) - offsetX - cellPx * Math.ceil(W / 2 / cellPx);
    const startY   = (H / 2) - offsetY - cellPx * Math.ceil(H / 2 / cellPx);

    ctx.strokeStyle = 'rgba(255,255,255,0.04)';
    ctx.lineWidth   = 1;

    for (let x = startX; x < W + cellPx; x += cellPx) {
        ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, H); ctx.stroke();
    }
    for (let y = startY; y < H + cellPx; y += cellPx) {
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
    }
}

// ── Mock Server Simulation ─────────────────────────────────────────────────
const mockServerState = { x: 0, z: 0, tick: 1, objectId: 999 };

function startMockSimulation() {
    logEvent('[Mock] Initializing Local Simulation without backend...', 'info');
    
    // Simulate Connect
    (client as any).maxVars = 128; // set default
    if (client.onConnected) client.onConnected();

    // Send Accept Packet (0x03)
    const cw = new NetworkWriter(64);
    cw.writeBytes(new Uint8Array(9)); // mock RUDP
    cw.writeByte(0x03); // PACKET_ACCEPT
    cw.writeInt32(1);   // player ID
    cw.writeInt32(128); // maxVars
    cw.writeInt32(60);  // tickRate
    feedMockPacket(cw);

    // Send Spawn Packet (0x04)
    const sw = new NetworkWriter(64);
    sw.writeBytes(new Uint8Array(9));
    sw.writeByte(0x04); // PACKET_SPAWN
    sw.writeUInt32(mockServerState.objectId);
    sw.writeUInt32(1); // prefabId
    sw.writeInt32(1); // ownerId
    sw.writeFloat32(0); // x
    sw.writeFloat32(0); // y
    sw.writeFloat32(0); // z
    sw.writeBytes(new Uint8Array(7)); // Quat
    feedMockPacket(sw);

    // Snapshot loop (60 Hz)
    setInterval(() => {
        mockServerState.tick++;
        const bw = new NetworkWriter(128);
        bw.writeBytes(new Uint8Array(9));
        bw.writeByte(0x01); // PACKET_SNAPSHOT
        bw.writeInt32(mockServerState.tick); // serverTick
        bw.writeInt32(mockServerState.tick - 1); // baseline

        // Payload logic: Write Object ID
        bw.writeUInt32(mockServerState.objectId);

        // BitMask simulation: 0x03 -> first two bits are 1 (meaning X & Z both changed!)
        bw.writeBigUInt64(3n); 
        bw.writeBigUInt64(0n); 

        // Delta Payload (Because bits are 1, we must write the actual variable sizes!)
        bw.writeFloat32(mockServerState.x);
        bw.writeFloat32(mockServerState.z);

        // Terminate packet
        bw.writeUInt32(0);

        feedMockPacket(bw);
    }, 16);
}

function feedMockPacket(writer: NetworkWriter) {
    const buffer = writer.getBuffer();
    // Directly run the decoder
    const reader = new NetworkReader(buffer);
    reader.skip(9);
    const packetType = reader.readByte();
    
    switch (packetType) {
        case 0x03: (client as any).handleAccept(reader); break;
        case 0x04: (client as any).handleSpawn(reader); break;
        case 0x01: (client as any).handleSnapshot(reader); break;
    }
}

requestAnimationFrame(render);
