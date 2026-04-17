export class NetworkWriter {
    public buffer: DataView;
    private position: number;

    constructor(initialCapacity: number = 1024) {
        this.buffer = new DataView(new ArrayBuffer(initialCapacity));
        this.position = 0;
    }

    private ensureCapacity(size: number) {
        if (this.position + size > this.buffer.byteLength) {
            const newBuffer = new ArrayBuffer(this.buffer.byteLength * 2 + size);
            new Uint8Array(newBuffer).set(new Uint8Array(this.buffer.buffer));
            this.buffer = new DataView(newBuffer);
        }
    }

    public writeByte(value: number) {
        this.ensureCapacity(1);
        this.buffer.setUint8(this.position, value);
        this.position += 1;
    }

    public writeInt16(value: number) {
        this.ensureCapacity(2);
        this.buffer.setInt16(this.position, value, true);
        this.position += 2;
    }

    public writeUInt16(value: number) {
        this.ensureCapacity(2);
        this.buffer.setUint16(this.position, value, true);
        this.position += 2;
    }

    public writeInt32(value: number) {
        this.ensureCapacity(4);
        this.buffer.setInt32(this.position, value, true);
        this.position += 4;
    }

    public writeUInt32(value: number) {
        this.ensureCapacity(4);
        this.buffer.setUint32(this.position, value, true);
        this.position += 4;
    }

    public writeFloat32(value: number) {
        this.ensureCapacity(4);
        this.buffer.setFloat32(this.position, value, true);
        this.position += 4;
    }

    public writeBigInt64(value: bigint) {
        this.ensureCapacity(8);
        this.buffer.setBigInt64(this.position, value, true);
        this.position += 8;
    }

    public writeBigUInt64(value: bigint) {
        this.ensureCapacity(8);
        this.buffer.setBigUint64(this.position, value, true);
        this.position += 8;
    }

    public writeString(value: string) {
        const encoded = new TextEncoder().encode(value);
        this.writeInt32(encoded.length);
        this.ensureCapacity(encoded.length);
        for (let i = 0; i < encoded.length; i++) {
            this.buffer.setUint8(this.position++, encoded[i]);
        }
    }

    public writeBool(value: boolean) {
        this.writeByte(value ? 1 : 0);
    }

    public writeBytes(bytes: Uint8Array) {
        this.ensureCapacity(bytes.length);
        const dest = new Uint8Array(this.buffer.buffer, this.position, bytes.length);
        dest.set(bytes);
        this.position += bytes.length;
    }

    public getBuffer(): ArrayBuffer {
        return this.buffer.buffer.slice(0, this.position) as ArrayBuffer;
    }
}
