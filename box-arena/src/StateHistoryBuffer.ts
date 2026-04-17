export class StateHistoryBuffer {
    private data: ArrayBuffer;
    private view: DataView;
    private variableOffsets: Int32Array;
    private snapshotSize: number;
    private historySize: number;

    constructor(historySize: number, variableSizes: number[]) {
        this.historySize = historySize;
        this.variableOffsets = new Int32Array(variableSizes.length);

        let currentOffset = 0;
        for (let i = 0; i < variableSizes.length; i++) {
            this.variableOffsets[i] = currentOffset;
            currentOffset += variableSizes[i];
        }

        this.snapshotSize = currentOffset;
        
        // Allocate entire history block for this component once (zero allocation during runtime)
        this.data = new ArrayBuffer(this.snapshotSize * this.historySize);
        this.view = new DataView(this.data);
    }

    /**
     * Gets a DataView mapped correctly to the specific variable within the specific tick.
     * Returns null if tick is invalid or out of bounds.
     */
    public getVariableView(tick: number, variableIndex: number): { view: DataView, offset: number, size: number } | null {
        if (tick < 0 || variableIndex >= this.variableOffsets.length) {
            return null;
        }

        const snapshotIndex = tick % this.historySize;
        const startOffset = (snapshotIndex * this.snapshotSize) + this.variableOffsets[variableIndex];
        
        const size = (variableIndex === this.variableOffsets.length - 1)
            ? this.snapshotSize - this.variableOffsets[variableIndex]
            : this.variableOffsets[variableIndex + 1] - this.variableOffsets[variableIndex];

        return { view: this.view, offset: startOffset, size: size };
    }

    /**
     * Snap all variables from a specific tick snapshot to another array or object buffer if needed.
     * Returns the full slice for a tick, allowing fast block copies.
     */
    public getSnapshotSlice(tick: number): Uint8Array {
        const snapshotIndex = tick % this.historySize;
        const startOffset = snapshotIndex * this.snapshotSize;
        return new Uint8Array(this.data, startOffset, this.snapshotSize);
    }
}
