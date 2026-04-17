export const NetworkType = {
    Bool: 1,
    Byte: 2,
    Int16: 3,
    UInt16: 4,
    Int32: 5,
    UInt32: 6,
    Float32: 7,
    BigInt64: 8,
    BigUInt64: 9,
    Vector2: 10,
    Vector3: 11,
    QuaternionCompressed: 12
} as const;

export type NetworkType = typeof NetworkType[keyof typeof NetworkType];

export function getNetworkTypeSize(type: NetworkType): number {
    switch (type) {
        case NetworkType.Bool:
        case NetworkType.Byte: return 1;
        case NetworkType.Int16:
        case NetworkType.UInt16: return 2;
        case NetworkType.Int32:
        case NetworkType.UInt32:
        case NetworkType.Float32:
        case NetworkType.QuaternionCompressed: return 4;
        case NetworkType.BigInt64:
        case NetworkType.BigUInt64:
        case NetworkType.Vector2: return 8;
        case NetworkType.Vector3: return 12;
        default: return 4;
    }
}
