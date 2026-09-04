using System;
using System.Collections.Generic;

namespace WW2.Core.Model
{
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        private static readonly HexCoord[] DirectionVectors =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int Q { get; }
        public int R { get; }
        public int S => -Q - R;

        public static IReadOnlyList<HexCoord> Directions => DirectionVectors;

        public HexCoord Neighbor(int direction)
        {
            var vector = DirectionVectors[((direction % 6) + 6) % 6];
            return this + vector;
        }

        public int DistanceTo(HexCoord other)
        {
            return (Math.Abs(Q - other.Q) + Math.Abs(R - other.R) + Math.Abs(S - other.S)) / 2;
        }

        public static HexCoord operator +(HexCoord left, HexCoord right)
        {
            return new HexCoord(left.Q + right.Q, left.R + right.R);
        }

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => (Q * 397) ^ R;
        public override string ToString() => $"({Q},{R})";
    }
}

