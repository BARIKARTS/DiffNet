import { NetworkReader } from './NetworkReader';
import { NetworkWriter } from './NetworkWriter';
import { BaseEntity } from './BaseEntity';

export const PACKET_CONNECT_REQUEST = 0x00;
export const PACKET_SNAPSHOT = 0x01;
export const PACKET_RPC = 0x02;
export const PACKET_ACCEPT = 0x03;
export const PACKET_SPAWN = 0x04;
export const PACKET_DESPAWN = 0x05;

export interface EntityFactory {
    [prefabId: number]: () => BaseEntity;
}

export class NetworkRunner {
    private ws: WebSocket | null = null;
    public localPlayerId: number = 0;
    
    // Entity management
    private entities: Map<number, BaseEntity> = new Map();
    private prefabFactory: EntityFactory;
    
    // Server configuration
    private maxVars: number = 128;
    private tickRate: number = 60;
    public currentServerTick: number = 0;

    // UI Event Callbacks
    public onConnected: (() => void) | null = null;
    public onDisconnected: (() => void) | null = null;
    public onAccepted: ((playerId: number) => void) | null = null;
    public onSpawn: ((entity: BaseEntity) => void) | null = null;
    public onDespawn: ((objectId: number) => void) | null = null;
    public onSnapshot: (() => void) | null = null;

    // Output buffering (simulation of RUDP header)
    private outgoingSequence: number = 0;

    constructor(factory: EntityFactory) {
        this.prefabFactory = factory;
    }

    public getEntities(): Map<number, BaseEntity> {
        return this.entities;
    }

    public connect(url: string, maxVars: number = 128) {
        this.maxVars = maxVars;
        this.ws = new WebSocket(url);
        this.ws.binaryType = 'arraybuffer';

        this.ws.onopen = () => {
            console.log('[NetworkRunner] Connected to server, sending Connect Request...');
            this.sendConnectRequest();
            if (this.onConnected) this.onConnected();
        };

        this.ws.onmessage = (event) => {
            const buffer = event.data as ArrayBuffer;
            // The C# server prepends a 9-byte RudpHeader to ALL WebSocket packets
            // Format: Mode(1), Sequence(2), Ack(2), AckBitfield(4)
            if (buffer.byteLength < 9 + 1) return;

            const reader = new NetworkReader(buffer);
            
            // Skip the 9-byte RudpHeader
            reader.skip(9);

            const packetType = reader.readByte();

            switch (packetType) {
                case PACKET_ACCEPT: this.handleAccept(reader); break;
                case PACKET_SPAWN: this.handleSpawn(reader); break;
                case PACKET_DESPAWN: this.handleDespawn(reader); break;
                case PACKET_SNAPSHOT: this.handleSnapshot(reader); break;
                case PACKET_RPC: /* Implement RPC later */ break;
                default:
                    console.warn(`[NetworkRunner] Unknown packet type: 0x${packetType.toString(16)}`);
            }
        };

        this.ws.onclose = () => {
            console.log('[NetworkRunner] Disconnected.');
            this.entities.clear();
            if (this.onDisconnected) this.onDisconnected();
        };

        this.ws.onerror = (err) => console.error('[NetworkRunner] WebSocket error:', err);
    }

    private handleAccept(reader: NetworkReader) {
        this.localPlayerId = reader.readInt32();
        const serverMaxVars = reader.readInt32();
        this.tickRate = reader.readInt32();

        this.maxVars = serverMaxVars;
        console.log(`[NetworkRunner] Accepted! playerId=${this.localPlayerId}, maxVars=${this.maxVars}, tickRate=${this.tickRate}`);
        if (this.onAccepted) this.onAccepted(this.localPlayerId);
    }

    private handleSpawn(reader: NetworkReader) {
        const objectId = reader.readUInt32();
        const prefabId = reader.readUInt32();
        const ownerId = reader.readInt32();
        
        // Initial setup data (transform wrapper)
        const x = reader.readFloat32();
        const y = reader.readFloat32();
        const z = reader.readFloat32();
        
        // Skip compressed quaternion
        reader.skip(7);

        if (!this.prefabFactory[prefabId]) {
            console.warn(`[NetworkRunner] Unknown prefabId: ${prefabId}`);
            return;
        }

        const entity = this.prefabFactory[prefabId]();
        entity.objectId = objectId;
        entity.prefabId = prefabId;
        entity.ownerId = ownerId;
        
        this.entities.set(objectId, entity);
        entity.onNetworkSpawn(x, y, z);
        
        console.log(`[NetworkRunner] Spawned Obj ${objectId} (Prefab: ${prefabId})`);
        if (this.onSpawn) this.onSpawn(entity);
    }

    private handleDespawn(reader: NetworkReader) {
        const objectId = reader.readUInt32();
        const entity = this.entities.get(objectId);
        if (entity) {
            entity.onNetworkDespawn();
            this.entities.delete(objectId);
            if (this.onDespawn) this.onDespawn(objectId);
        }
    }

    private handleSnapshot(reader: NetworkReader) {
        const serverTick = reader.readInt32();
        const baselineTick = reader.readInt32();
        this.currentServerTick = serverTick;

        while (reader.remaining() >= 4) {
            const objectId = reader.readUInt32();
            if (objectId === 0) break; // End-of-packet marker

            const entity = this.entities.get(objectId);
            if (entity) {
                // Perform zero-allocation delta decompression matching C#
                entity.deserializeDeltaState(reader, baselineTick, serverTick, this.maxVars);
            } else {
                console.warn(`[NetworkRunner] Missing entity ${objectId} in snapshot! Stream may corrupt.`);
            }
        }
        
        if (this.onSnapshot) this.onSnapshot();
    }

    private sendConnectRequest() {
        const w = new NetworkWriter(32); // Max capacity is fine with 32
        
        // Generate mocked 9-byte RUDP header to satisfy the UdpTransport/WebSocketTransport expectations
        w.writeByte(1); // Mode (ReliableOrdered usually = 1 for backend logic)
        w.writeUInt16(this.outgoingSequence++);
        w.writeUInt16(0); // Ack
        w.writeUInt32(0); // AckBitfield
        
        w.writeByte(PACKET_CONNECT_REQUEST);
        w.writeInt32(this.maxVars);
        w.writeInt32(64); // StateHistorySize
        w.writeInt32(10); // AOIGridCellSize
        
        this._send(w.getBuffer());
    }

    public sendRpc(objectId: number, rpcId: number, writerCallback: (writer: NetworkWriter) => void) {
        const w = new NetworkWriter(512);

        w.writeByte(1); // RUDP Mode
        w.writeUInt16(this.outgoingSequence++);
        w.writeUInt16(0);
        w.writeUInt32(0);

        w.writeByte(PACKET_RPC);
        w.writeUInt16(rpcId);
        w.writeInt32(objectId);
        
        writerCallback(w);
        
        this._send(w.getBuffer());
    }

    public sendInput(x: number, z: number) {
        const w = new NetworkWriter(32);
        w.writeByte(1); // Mode
        w.writeUInt16(this.outgoingSequence++);
        w.writeUInt16(0);
        w.writeUInt32(0);
        w.writeByte(0x04); // Packet ID mapping to Input in the mock
        w.writeFloat32(x);
        w.writeFloat32(z);
        this._send(w.getBuffer());
    }

    private _send(buffer: ArrayBuffer) {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(buffer);
        }
    }
}
