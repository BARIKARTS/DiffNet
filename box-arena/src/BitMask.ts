import { NetworkReader } from './NetworkReader';
import { NetworkWriter } from './NetworkWriter';

/**
 * TypeScript implementation of the C# BitMask.
 * Uses a single BigUint64Array(2) containing two 64-bit unsigned integers (128 bits total).
 * Zero-allocation approach suitable for memory pooling.
 */
export class BitMask {
    public data: BigUint64Array;

    constructor() {
        this.data = new BigUint64Array(2);
    }

    public setBit(index: number) {
        if (index >= 128) return;
        const wordIndex = index >> 6;
        const bitIndex = BigInt(index & 63);
        this.data[wordIndex] |= (1n << bitIndex);
    }

    public getBit(index: number): boolean {
        if (index >= 128) return false;
        const wordIndex = index >> 6;
        const bitIndex = BigInt(index & 63);
        return (this.data[wordIndex] & (1n << bitIndex)) !== 0n;
    }

    public clear() {
        this.data[0] = 0n;
        this.data[1] = 0n;
    }

    /**
     * Compare another BitMask to see if there is any intersection.
     * Useful for checking if updated variables intersect with allowed variables.
     */
    public hasIntersection(other: BitMask): boolean {
        return (this.data[0] & other.data[0]) !== 0n || (this.data[1] & other.data[1]) !== 0n;
    }

    /**
     * Merges another BitMask into this one.
     */
    public merge(other: BitMask) {
        this.data[0] |= other.data[0];
        this.data[1] |= other.data[1];
    }

    public writeTo(writer: NetworkWriter, maxVariables: number = 128) {
        let ulongCount = maxVariables >> 6;
        if (ulongCount === 0) ulongCount = 1;
        if (ulongCount > 2) ulongCount = 2; // Guard for 128 bit max

        for (let i = 0; i < ulongCount; i++) {
            writer.writeBigUInt64(this.data[i]);
        }
    }

    public readFrom(reader: NetworkReader, maxVariables: number = 128) {
        let ulongCount = maxVariables >> 6;
        if (ulongCount === 0) ulongCount = 1;
        if (ulongCount > 2) ulongCount = 2;

        for (let i = 0; i < ulongCount; i++) {
            this.data[i] = reader.readBigUInt64();
        }
    }

    public isEmpty(maxVariables: number = 128): boolean {
        let ulongCount = maxVariables >> 6;
        if (ulongCount === 0) ulongCount = 1;
        if (ulongCount > 2) ulongCount = 2;

        for (let i = 0; i < ulongCount; i++) {
            if (this.data[i] !== 0n) return false;
        }
        return true;
    }
}
