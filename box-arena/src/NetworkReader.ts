export class NetworkReader {
    public buffer: DataView;
    private position: number;

    constructor(buffer: ArrayBuffer) {
        this.buffer = new DataView(buffer);
        this.position = 0;
    }

    public readByte(): number {
        const val = this.buffer.getUint8(this.position);
        this.position += 1;
        return val;
    }

    public readInt16(): number {
        const val = this.buffer.getInt16(this.position, true);
        this.position += 2;
        return val;
    }

    public readUInt16(): number {
        const val = this.buffer.getUint16(this.position, true);
        this.position += 2;
        return val;
    }

    public readInt32(): number {
        const val = this.buffer.getInt32(this.position, true);
        this.position += 4;
        return val;
    }

    public readUInt32(): number {
        const val = this.buffer.getUint32(this.position, true);
        this.position += 4;
        return val;
    }

    public readFloat32(): number {
        const val = this.buffer.getFloat32(this.position, true);
        this.position += 4;
        return val;
    }

    public readBigInt64(): bigint {
        const val = this.buffer.getBigInt64(this.position, true);
        this.position += 8;
        return val;
    }

    public readBigUInt64(): bigint {
        const val = this.buffer.getBigUint64(this.position, true);
        this.position += 8;
        return val;
    }

    public readString(): string {
        const length = this.readInt32();
        if (length === 0) return "";
        const bytes = new Uint8Array(this.buffer.buffer, this.position, length);
        this.position += length;
        return new TextDecoder().decode(bytes);
    }

    public readBool(): boolean {
        return this.readByte() === 1;
    }

    public readBytes(length: number): Uint8Array {
        const bytes = new Uint8Array(this.buffer.buffer, this.position, length);
        this.position += length;
        return bytes;
    }
    
    public getBytesRead(): number {
        return this.position;
    }

    public remaining(): number {
        return this.buffer.byteLength - this.position;
    }

    public skip(bytes: number): void {
        this.position += bytes;
    }
}
