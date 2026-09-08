namespace UniGetUI.Core.Tools
{
    public static class CoreTools
    {
        public struct Version : IComparable, IComparable<Version>, IEquatable<Version>
        {
            public static readonly Version Null = new(-1, -1, -1, -1);

            public readonly int Major;
            public readonly int Minor;
            public readonly int Patch;
            public readonly int Remainder;

            /// <summary>
            /// Segments past the fourth, with trailing zeroes trimmed so that equal versions
            /// always carry an identical array. Null when the version has four segments or fewer.
            /// These are compared too, so that versions differing only past the fourth segment
            /// (e.g. a build revision on a four-part version: 1.2.3.4_1 vs 1.2.3.4_2) order
            /// correctly instead of collapsing into Remainder.
            /// </summary>
            private readonly int[]? _extraSegments;

            public Version(int major, int minor = 0, int patch = 0, int remainder = 0)
                : this(major, minor, patch, remainder, null) { }

            private Version(int major, int minor, int patch, int remainder, int[]? extraSegments)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                Remainder = remainder;
                _extraSegments = extraSegments;
            }

            /// <summary>
            /// Builds a Version from an arbitrary number of segments. Segments past the fourth are
            /// retained for comparison rather than being folded into Remainder.
            /// </summary>
            internal static Version FromSegments(IReadOnlyList<int> segments)
            {
                int count = segments.Count;
                while (count > 4 && segments[count - 1] == 0)
                    count--;

                int[]? extra = null;
                if (count > 4)
                {
                    extra = new int[count - 4];
                    for (int i = 4; i < count; i++)
                        extra[i - 4] = segments[i];
                }

                return new Version(
                    Segment(segments, 0),
                    Segment(segments, 1),
                    Segment(segments, 2),
                    Segment(segments, 3),
                    extra
                );

                static int Segment(IReadOnlyList<int> values, int index) =>
                    index < values.Count ? values[index] : 0;
            }

            private int ExtraSegment(int index) =>
                _extraSegments is not null && index < _extraSegments.Length ? _extraSegments[index] : 0;

            public int CompareTo(object? other_) => other_ is Version other ? CompareTo(other) : 0;

            public int CompareTo(Version other)
            {
                int major = Major.CompareTo(other.Major);
                if (major != 0)
                    return major;

                int minor = Minor.CompareTo(other.Minor);
                if (minor != 0)
                    return minor;

                int patch = Patch.CompareTo(other.Patch);
                if (patch != 0)
                    return patch;

                int remainder = Remainder.CompareTo(other.Remainder);
                if (remainder != 0)
                    return remainder;

                int extraCount = Math.Max(
                    _extraSegments?.Length ?? 0,
                    other._extraSegments?.Length ?? 0
                );
                for (int i = 0; i < extraCount; i++)
                {
                    int extra = ExtraSegment(i).CompareTo(other.ExtraSegment(i));
                    if (extra != 0)
                        return extra;
                }

                return 0;
            }

            /// <summary>
            /// The 1-based position of the most significant segment that differs from
            /// <paramref name="other"/>, or 0 when both versions are equal. Differences past the
            /// fourth segment all report 4, as they are the least significant change there is.
            /// </summary>
            public int FirstDifferingComponent(Version other)
            {
                if (Major != other.Major) return 1;
                if (Minor != other.Minor) return 2;
                if (Patch != other.Patch) return 3;
                if (Remainder != other.Remainder) return 4;
                return CompareTo(other) == 0 ? 0 : 4;
            }

            public static bool operator ==(Version left, Version right) =>
                left.CompareTo(right) == 0;

            public static bool operator !=(Version left, Version right) =>
                left.CompareTo(right) != 0;

            public static bool operator >=(Version left, Version right) =>
                left.CompareTo(right) >= 0;

            public static bool operator <=(Version left, Version right) =>
                left.CompareTo(right) <= 0;

            public static bool operator >(Version left, Version right) => left.CompareTo(right) > 0;

            public static bool operator <(Version left, Version right) => left.CompareTo(right) < 0;

            public bool Equals(Version other) => CompareTo(other) == 0;

            public override bool Equals(object? obj) => obj is Version other && Equals(other);

            public override int GetHashCode()
            {
                // Trailing zeroes are trimmed from _extraSegments, so equal versions hash equally.
                HashCode hash = new();
                hash.Add(Major);
                hash.Add(Minor);
                hash.Add(Patch);
                hash.Add(Remainder);
                foreach (int segment in _extraSegments ?? [])
                    hash.Add(segment);
                return hash.ToHashCode();
            }
        }
    }
}
