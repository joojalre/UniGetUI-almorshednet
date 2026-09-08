using UniGetUI.Core.Tools;

namespace UniGetUI.PackageEngine.Tests;

public sealed class PythonVersionTests
{
    // Every expectation below was generated from Python's packaging library, which is the
    // reference implementation of PEP 440, rather than written by hand.
    [Theory]
    [InlineData("1.0.0rc1", "1.0.0", -1)]
    [InlineData("1.0.0b1", "1.0.0", -1)]
    [InlineData("1.0.0a1", "1.0.0", -1)]
    [InlineData("1.0.0rc1", "1.0.0rc2", -1)]
    [InlineData("1.0.0a2", "1.0.0b1", -1)]
    [InlineData("1.0.0b2", "1.0.0rc1", -1)]
    [InlineData("1.0.0alpha1", "1.0.0a1", 0)]
    [InlineData("1.0.0beta1", "1.0.0b1", 0)]
    [InlineData("1.0.0c1", "1.0.0rc1", 0)]
    [InlineData("1.0.0pre1", "1.0.0rc1", 0)]
    [InlineData("1.0.0preview1", "1.0.0rc1", 0)]
    [InlineData("1.0.0-rc1", "1.0.0rc1", 0)]
    [InlineData("1.0.0_rc1", "1.0.0rc1", 0)]
    [InlineData("1.0.0.rc1", "1.0.0rc1", 0)]
    [InlineData("1.0.0rc", "1.0.0rc0", 0)]
    [InlineData("1.0.0.post1", "1.0.0", 1)]
    [InlineData("1.0.0-post1", "1.0.0.post1", 0)]
    [InlineData("1.0.0.rev1", "1.0.0.post1", 0)]
    [InlineData("1.0.0.r1", "1.0.0.post1", 0)]
    [InlineData("1.0.0-1", "1.0.0", 1)]
    [InlineData("1.0.0-1", "1.0.0.post1", 0)]
    [InlineData("1.0.0-2", "1.0.0-1", 1)]
    [InlineData("1.0.0.dev1", "1.0.0", -1)]
    [InlineData("1.0.0.dev1", "1.0.0a1", -1)]
    [InlineData("1.0.0a1.dev1", "1.0.0a1", -1)]
    [InlineData("1.0.0.post1.dev1", "1.0.0.post1", -1)]
    [InlineData("1.0.0.post1.dev1", "1.0.0", 1)]
    [InlineData("1.0.0.dev2", "1.0.0.dev1", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0.0", "1.0", 0)]
    [InlineData("1.2.3.4.5", "1.2.3.4", 1)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("1!1.0", "2.0", 1)]
    [InlineData("1!1.0", "1.0", 1)]
    [InlineData("2!1.0", "1!9.9", 1)]
    [InlineData("1.0+local", "1.0", 1)]
    [InlineData("1.0+local.2", "1.0+local.1", 1)]
    [InlineData("v1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0RC1", "1.0.0rc1", 0)]
    [InlineData("  1.0.0  ", "1.0.0", 0)]
    public void OrderingMatchesThePep440ReferenceImplementation(string a, string b, int expected)
    {
        Assert.True(PythonVersion.TryParse(a, out var parsedA), $"could not parse {a}");
        Assert.True(PythonVersion.TryParse(b, out var parsedB), $"could not parse {b}");

        Assert.Equal(expected, Math.Sign(parsedA.CompareTo(parsedB)));
        Assert.Equal(-expected, Math.Sign(parsedB.CompareTo(parsedA)));
    }

    [Theory]
    [InlineData("", "!1.0")]
    [InlineData("1.", "")]
    [InlineData("1.0a", "")]
    [InlineData("1.0b", "")]
    [InlineData("1.0rc", "")]
    [InlineData("1.0.post", "")]
    [InlineData("1.0-", "")]
    [InlineData("1.0.dev", "")]
    [InlineData("1.0+local.", "")]
    public void NumericComponentsHaveNoMachineIntegerLimit(string prefix, string suffix)
    {
        string[] numbers =
        [
            "0",
            "2147483647",
            "2147483648",
            "9999999999",
            "10000000000",
            "18446744073709551616",
            new string('9', 1024),
            "1" + new string('0', 1024),
        ];

        for (int i = 1; i < numbers.Length; i++)
        {
            Assert.True(PythonVersion.TryParse(prefix + numbers[i - 1] + suffix, out var lower));
            Assert.True(PythonVersion.TryParse(prefix + numbers[i] + suffix, out var higher));

            Assert.True(lower < higher);
            Assert.True(higher > lower);
            Assert.False(lower == higher);
        }

        string number = numbers[^1];
        Assert.True(PythonVersion.TryParse(prefix + number + suffix, out var plain));
        Assert.True(PythonVersion.TryParse(prefix + "000" + number + suffix, out var padded));

        Assert.Equal(0, plain.CompareTo(padded));
        Assert.Equal(0, padded.CompareTo(plain));
        Assert.True(plain == padded);
        Assert.Equal(plain.GetHashCode(), padded.GetHashCode());
    }

    [Theory]
    [InlineData("000!1.0", "1.0")]
    [InlineData("1.0.000", "1.0")]
    [InlineData("1.0a000", "1.0a")]
    [InlineData("1.0.post000", "1.0.post")]
    [InlineData("1.0-000", "1.0.post")]
    [InlineData("1.0.dev000", "1.0.dev")]
    [InlineData("1.0+000", "1.0+0")]
    [InlineData("1.9999999999.000", "1.9999999999")]
    public void ZeroComponentsNormalizeWithoutChangingEquality(string left, string right)
    {
        Assert.True(PythonVersion.TryParse(left, out var parsedLeft));
        Assert.True(PythonVersion.TryParse(right, out var parsedRight));

        Assert.Equal(0, parsedLeft.CompareTo(parsedRight));
        Assert.Equal(0, parsedRight.CompareTo(parsedLeft));
        Assert.True(parsedLeft == parsedRight);
        Assert.Equal(parsedLeft.GetHashCode(), parsedRight.GetHashCode());
    }

    [Theory]
    [InlineData("1.0+9999999999", "1.0+alpha", 1)]
    [InlineData("1.0+10000000000", "1.0+9999999999", 1)]
    [InlineData("1.0+0001alpha", "1.0+01alpha", -1)]
    [InlineData("1.0-9999999999", "1.0.post9999999999", 0)]
    public void LargeComponentsRetainPep440SegmentRules(string left, string right, int expected)
    {
        Assert.True(PythonVersion.TryParse(left, out var parsedLeft));
        Assert.True(PythonVersion.TryParse(right, out var parsedRight));

        Assert.Equal(expected, Math.Sign(parsedLeft.CompareTo(parsedRight)));
        Assert.Equal(-expected, Math.Sign(parsedRight.CompareTo(parsedLeft)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("1.0.0-abc")]
    [InlineData("1.0.0..0")]
    [InlineData("1..0")]
    [InlineData("1.0.0+")]
    [InlineData("hello")]
    [InlineData("8b640eef")]
    public void UnparseableVersionsAreRejected(string version)
    {
        Assert.False(PythonVersion.TryParse(version, out var parsed));
        Assert.False(parsed.IsValid);
    }

    [Theory]
    [InlineData("1.0.0rc1", true)]
    [InlineData("1.0.0a1", true)]
    [InlineData("1.0.0.dev1", true)]
    [InlineData("1.0.0a1.dev1", true)]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0.post1", false)]
    [InlineData("1.0.0+local", false)]
    public void PreReleaseIsDetected(string version, bool expected)
    {
        Assert.True(PythonVersion.TryParse(version, out var parsed));
        Assert.Equal(expected, parsed.IsPreRelease);
    }

    [Fact]
    public void ALocalSegmentNamedDevIsNotADevRelease()
    {
        Assert.True(PythonVersion.TryParse("1.0.0+devbuild", out var local));
        Assert.True(PythonVersion.TryParse("1.0.0", out var release));

        Assert.False(local.IsPreRelease);
        Assert.True(local > release);
    }

    [Fact]
    public void InvalidVersionsOrderBelowValidOnes()
    {
        Assert.True(PythonVersion.TryParse("0.0.1", out var valid));
        PythonVersion invalid = default;

        Assert.True(valid > invalid);
        Assert.Equal(string.Empty, invalid.ToString());
    }

    [Fact]
    public void OriginalStringIsPreserved()
    {
        Assert.True(PythonVersion.TryParse("1.0.0RC1", out var parsed));
        Assert.Equal("1.0.0RC1", parsed.Original);
    }
}
