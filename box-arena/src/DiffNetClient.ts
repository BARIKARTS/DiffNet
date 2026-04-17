import { NetworkReader } from './NetworkReader';
import { NetworkWriter } from './NetworkWriter';

// ── DiffNet Packet Types ────────────────────────────────────────────────────
// These must match the C# NetworkRunner packet constants exactly.
export const PACKET_CONNECT_REQUEST = 0x00;
export const PACKET_SNAPSHOT = 0x01;
export const PACKET_RPC = 0x02;
export const PACKET_ACCEPT = 0x03;
export const PACKET_SPAWN = 0x04;
export const PACKET_DESPAWN = 0x05;

export interface SpawnEvent {
    objectId: number;
    prefabId: number;
    ownerId: number;
    x: number;
    y: number;
    z: number;
}

export interface DespawnEvent {
    objectId: number;
}

export interface SnapshotEvent {
    serverTick: number;
    baselineTick: number;
    reader: NetworkReader; // positioned after the header, caller reads object states
}

// ── DiffNetClient ──────────────────────────────────────────────────────────
export class DiffNetClient {
    private ws: WebSocket | null = null;
    public localPlayerId: number = 0;

    // ── Callbacks (set by caller) ──
    public onConnected: (() => void) | null = null;
    public onDisconnected: (() => void) | null = null;
    public onAccepted: ((playerId: number) => void) | null = null;
    public onSpawn: ((evt: SpawnEvent) => void) | null = null;
    public onDespawn: ((evt: DespawnEvent) => void) | null = null;
    public onSnapshot: ((evt: SnapshotEvent) => void) | null = null;

    public connect(url: string) {
        this.ws = new WebSocket(url);
        this.ws.binaryType = 'arraybuffer';

        this.ws.onopen = () => {
            console.log('[DiffNetClient] Connected to server, sending Connect Request...');
            this._sendConnectRequest();
            if (this.onConnected) this.onConnected();
        };

        this.ws.onmessage = (event) => {
            const buffer = event.data as ArrayBuffer;
            if (buffer.byteLength < 1) return;

            const reader = new NetworkReader(buffer);
            const packetType = reader.readByte();

            switch (packetType) {
                case PACKET_ACCEPT: this._handleAccept(reader); break;
                case PACKET_SPAWN: this._handleSpawn(reader); break;
                case PACKET_DESPAWN: this._handleDespawn(reader); break;
                case PACKET_SNAPSHOT: this._handleSnapshot(reader); break;
                default:
                    console.warn(`[DiffNetClient] Unknown packet type: 0x${packetType.toString(16)}`);
            }
        };

        this.ws.onclose = () => {
            console.log('[DiffNetClient] Disconnected.');
            if (this.onDisconnected) this.onDisconnected();
        };

        this.ws.onerror = (err) => console.error('[DiffNetClient] WebSocket error:', err);
    }

    // ── Packet Handlers ───────────────────────────────────────────────────

    private _handleAccept(reader: NetworkReader) {
        this.localPlayerId = reader.readByte();
        console.log(`[DiffNetClient] Accepted! Assigned playerId = ${this.localPlayerId}`);
        if (this.onAccepted) this.onAccepted(this.localPlayerId);
    }

    private _handleSpawn(reader: NetworkReader) {
        // Layout: [objectId:uint4][prefabId:uint4][ownerId:int4][x:f4][y:f4][z:f4][rot: 7 bytes compressed quat]
        const objectId = reader.readUInt32();
        const prefabId = reader.readUInt32();
        const ownerId = reader.readInt32();
        const x = reader.readFloat32();
        const y = reader.readFloat32();
        const z = reader.readFloat32();
        // Skip compressed quaternion (7 bytes: 1 byte index + 3 × ushort compressed components)
        reader.readByte();    // largestIndex
        reader.readUInt16();  // a
        reader.readUInt16();  // b
        reader.readUInt16();  // c

        console.log(`[DiffNetClient] SPAWN obj=${objectId} prefab=${prefabId} owner=${ownerId} pos=(${x.toFixed(1)},${y.toFixed(1)},${z.toFixed(1)})`);
        if (this.onSpawn) this.onSpawn({ objectId, prefabId, ownerId, x, y, z });
    }

    private _handleDespawn(reader: NetworkReader) {
        const objectId = reader.readUInt32();
        console.log(`[DiffNetClient] DESPAWN obj=${objectId}`);
        if (this.onDespawn) this.onDespawn({ objectId });
    }

    private _handleSnapshot(reader: NetworkReader) {
        const serverTick = reader.readInt32();
        const baselineTick = reader.readInt32();
        if (this.onSnapshot) this.onSnapshot({ serverTick, baselineTick, reader });
    }

    // ── Outgoing Packets ──────────────────────────────────────────────────

    /**
     * Sends a DiffNet Connect Request (0x00) to the server on WebSocket connect.
     * Layout mirrors C# SendConnectRequest():
     *   [0x00][MaxNetworkedVariables:int4][StateHistorySize:int4][AOIGridCellSize:int4]
     */
    private _sendConnectRequest() {
        const w = new NetworkWriter(16);
        w.writeByte(PACKET_CONNECT_REQUEST);
        w.writeInt32(64);  // MaxNetworkedVariables (must match server)
        w.writeInt32(64);  // StateHistorySize
        w.writeInt32(10);  // AOIGridCellSize
        this._send(w.getBuffer());
    }

    /**
     * Sends player position to the server (simple input packet for stress testing).
     * Layout: [0x04 / custom type][x:f4][y:f4]
     * In production, this would be input struct. For the stress test we just send position.
     */
    public sendPosition(x: number, y: number) {
        const w = new NetworkWriter(16);
        w.writeByte(0x04); // Re-using slot as input hint (server ignores — WebSocket path is read-only for now)
        w.writeFloat32(x);
        w.writeFloat32(y);
        this._send(w.getBuffer());
    }

    private _send(buffer: ArrayBuffer) {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(buffer);
        }
    }
}
