using Unity.Mathematics;
using Unity.Netcode;

using UnityEngine;

// Shared wire format for the host -> client rope-shape stream. The host serialises its one
// authoritative rope's drawn particle positions; clients render them on a non-simulating puppet, so
// every screen shows the same rope and wrap with no second solve.
//
// Per snapshot: ushort particleCount, then that many world positions, each three half-floats (6 bytes).
// World space so it's machine-independent (the two solvers' local frames differ, the game world is
// shared). Sent UnreliableSequenced ~20x/s, which is single-digit KB/s.
public static class RopeStreamProtocol
{
    // CustomMessagingManager channel (no NetworkObject needed to send/receive).
    public const string MessageName = "RBRopeStream";

    // Bytes for a snapshot of count particles: ushort count + count * half3.
    public static int ByteSize(int count) => sizeof(ushort) + count * 3 * sizeof(ushort);

    // Write one world position as three half-floats.
    public static void WritePos(FastBufferWriter w, Vector3 p)
    {
        w.WriteValueSafe((ushort)math.f32tof16(p.x));
        w.WriteValueSafe((ushort)math.f32tof16(p.y));
        w.WriteValueSafe((ushort)math.f32tof16(p.z));
    }

    // Read one world position from three half-floats.
    public static Vector3 ReadPos(FastBufferReader r)
    {
        r.ReadValueSafe(out ushort x);
        r.ReadValueSafe(out ushort y);
        r.ReadValueSafe(out ushort z);
        return new Vector3(math.f16tof32(x), math.f16tof32(y), math.f16tof32(z));
    }
}
