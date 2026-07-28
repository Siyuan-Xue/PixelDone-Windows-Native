namespace PixelDone.Core;

public static class PixelDoneProduct
{
    public const string Version = "4.0.0-beta.1";
    public const string CloudSchema = "3.2";
}

public readonly record struct ProductVersion(
    int Major,
    int Minor,
    int Patch,
    int? Beta = null) : IComparable<ProductVersion>
{
    public static bool TryParse(string value, out ProductVersion version)
    {
        version = default;
        var normalized = value.Trim().TrimStart('v', 'V');
        var parts = normalized.Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length != 3 ||
            !int.TryParse(numbers[0], out var major) ||
            !int.TryParse(numbers[1], out var minor) ||
            !int.TryParse(numbers[2], out var patch))
        {
            return false;
        }

        int? beta = null;
        if (parts.Length == 2)
        {
            var pre = parts[1].Split('.');
            if (pre.Length != 2 ||
                !string.Equals(pre[0], "beta", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(pre[1], out var parsedBeta))
            {
                return false;
            }

            beta = parsedBeta;
        }

        version = new ProductVersion(major, minor, patch, beta);
        return true;
    }

    public int CompareTo(ProductVersion other)
    {
        var stable = Major.CompareTo(other.Major);
        if (stable == 0)
        {
            stable = Minor.CompareTo(other.Minor);
        }

        if (stable == 0)
        {
            stable = Patch.CompareTo(other.Patch);
        }

        if (stable != 0)
        {
            return stable;
        }

        return (Beta, other.Beta) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } left, { } right) => left.CompareTo(right),
        };
    }

    public override string ToString() =>
        Beta is { } beta
            ? $"{Major}.{Minor}.{Patch}-beta.{beta}"
            : $"{Major}.{Minor}.{Patch}";
}
