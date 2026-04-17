import { NetworkReader } from './NetworkReader';
import { BitMask } from './BitMask';
import { StateHistoryBuffer } from './StateHistoryBuffer';
import { NetworkType, getNetworkTypeSize } from './NetworkTypes';

export abstract class BaseEntity {
    public objectId: number = 0;
    public prefabId: number = 0;
    public ownerId: number = 0;
    
    // Default transform variables tracked on spawn
    public x: number = 0;
    public y: number = 0;
    public z: number = 0;

    protected stateHistory!: StateHistoryBuffer;
    protected networkSchema!: ReadonlyArray<NetworkType>;

    // Optional interpolation variables
    public currentTick: number = 0;

    constructor(historySize: number = 64) {
        this.networkSchema = this.defineSchema();
        const sizes = this.networkSchema.map(t => getNetworkTypeSize(t));
        this.stateHistory = new StateHistoryBuffer(historySize, sizes);
    }

    /**
     * Subclasses define their networked schema here. 
     * The order MUST perfectly match the C# struct/properties order!
     */
    protected abstract defineSchema(): ReadonlyArray<NetworkType>;

    /**
     * Reads a Delta Snapshot from the server and merges it into the Target Tick.
     * Uses zero-allocation array buffers (StateHistoryBuffer).
     */
    public deserializeDeltaState(reader: NetworkReader, baselineTick: number, targetTick: number, maxVars: number = 128) {
        const mask = new BitMask();
        mask.readFrom(reader, maxVars);

        for (let i = 0; i < this.networkSchema.length; i++) {
            const type = this.networkSchema[i];
            const size = getNetworkTypeSize(type);
            
            const targetVarView = this.stateHistory.getVariableView(targetTick, i);
            if (!targetVarView) continue;

            if (mask.getBit(i)) {
                // Changed value received over network. Read it and save to target tick history.
                // We directly copy raw bytes from the network reader into our local buffer bypassing GC.
                const rawBytes = reader.readBytes(size);
                const destArray = new Uint8Array(targetVarView.view.buffer, targetVarView.offset, size);
                destArray.set(rawBytes);
            } else {
                // Value didn't change from baseline tick, copy it forwards.
                const baselineVarView = this.stateHistory.getVariableView(baselineTick, i);
                if (baselineVarView) {
                    const sourceArray = new Uint8Array(baselineVarView.view.buffer, baselineVarView.offset, size);
                    const destArray = new Uint8Array(targetVarView.view.buffer, targetVarView.offset, size);
                    destArray.set(sourceArray);
                }
            }
        }
        
        this.currentTick = targetTick;
        this.onStateUpdate(targetTick);
    }

    /**
     * Gets the latest value stored in the history buffer for reading in your game loop.
     */
    public readValue(tick: number, varIndex: number): number | bigint | boolean {
        const viewData = this.stateHistory.getVariableView(tick, varIndex);
        if (!viewData) return 0;
        
        const { view, offset } = viewData;
        const type = this.networkSchema[varIndex];

        switch (type) {
            case NetworkType.Bool: return view.getUint8(offset) === 1;
            case NetworkType.Byte: return view.getUint8(offset);
            case NetworkType.Int16: return view.getInt16(offset, true);
            case NetworkType.UInt16: return view.getUint16(offset, true);
            case NetworkType.Int32: return view.getInt32(offset, true);
            case NetworkType.UInt32: return view.getUint32(offset, true);
            case NetworkType.Float32: return view.getFloat32(offset, true);
            case NetworkType.BigInt64: return view.getBigInt64(offset, true);
            case NetworkType.BigUInt64: return view.getBigUint64(offset, true);
            // Vector2, Vector3 and Quaternions should be read separately using custom vector structs
            default: return 0;
        }
    }

    // Callbacks
    public onNetworkSpawn(x: number, y: number, z: number) {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public onNetworkDespawn() {}
    public onStateUpdate(tick: number) { this.currentTick = tick; } // Read the variable to prevent TS error
}
