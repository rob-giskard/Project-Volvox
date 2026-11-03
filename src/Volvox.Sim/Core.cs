namespace Volvox.Sim;

/// <summary>Small immutable 2D vector in world units.</summary>
public readonly struct V2
{
    public readonly double X;
    public readonly double Y;

    public V2(double x, double y) { X = x; Y = y; }

    public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
    public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
    public static V2 operator *(V2 a, double s) => new(a.X * s, a.Y * s);
    public static V2 operator /(V2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSq => X * X + Y * Y;

    public static double Dist(V2 a, V2 b) => (a - b).Length;
    public static double DistSq(V2 a, V2 b) => (a - b).LengthSq;
    public static double Dot(V2 a, V2 b) => a.X * b.X + a.Y * b.Y;
    public static V2 FromAngle(double a) => new(Math.Cos(a), Math.Sin(a));

    public V2 Normalized()
    {
        double l = Length;
        return l < 1e-9 ? new V2(0, 0) : new V2(X / l, Y / l);
    }

    public override string ToString() => $"({X:0},{Y:0})";
}

/// <summary>Seedable xoshiro256** generator. Deterministic across platforms.</summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextU64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    public double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));
    public double Range(double min, double max) => min + (max - min) * NextDouble();
    public int Int(int maxExclusive) => (int)(NextDouble() * maxExclusive);
}

public enum Need { Energy, Hydration, Warmth, Rest }

public enum ActionKind { Eat, Drink, Bask, Rest }

public enum ObjectKind { BerryBush, Pool, Vent, Hollow, Stone, Fern, Nightshade }

public enum TerrainType : byte { Open, Marsh, Thicket }

/// <summary>Display names and the innate drive → action link.</summary>
public static class Names
{
    public static readonly Need[] Needs = { Need.Energy, Need.Hydration, Need.Warmth, Need.Rest };

    public static string Of(ObjectKind k) => k switch
    {
        ObjectKind.BerryBush => "berry bush",
        ObjectKind.Pool => "pool",
        ObjectKind.Vent => "warm vent",
        ObjectKind.Hollow => "hollow",
        ObjectKind.Stone => "stone",
        ObjectKind.Fern => "fern",
        ObjectKind.Nightshade => "nightshade",
        _ => k.ToString(),
    };

    public static string Of(ActionKind a) => a switch
    {
        ActionKind.Eat => "EAT",
        ActionKind.Drink => "DRINK",
        ActionKind.Bask => "BASK",
        ActionKind.Rest => "REST",
        _ => a.ToString(),
    };

    public static string Of(Need n) => n switch
    {
        Need.Energy => "energy",
        Need.Hydration => "hydration",
        Need.Warmth => "warmth",
        Need.Rest => "rest",
        _ => n.ToString(),
    };

    public static string Of(TerrainType t) => t switch
    {
        TerrainType.Marsh => "marsh",
        TerrainType.Thicket => "thicket",
        _ => "open",
    };

    /// <summary>Innate: hunger makes it try eating, thirst drinking, cold basking, tiredness resting.
    /// What it does NOT know is which objects these actions work on.</summary>
    public static ActionKind ActionFor(Need n) => n switch
    {
        Need.Energy => ActionKind.Eat,
        Need.Hydration => ActionKind.Drink,
        Need.Warmth => ActionKind.Bask,
        _ => ActionKind.Rest,
    };

    public static Need NeedFor(ActionKind a) => a switch
    {
        ActionKind.Eat => Need.Energy,
        ActionKind.Drink => Need.Hydration,
        ActionKind.Bask => Need.Warmth,
        _ => Need.Rest,
    };

    public static string Verb(ActionKind a) => a switch
    {
        ActionKind.Eat => "Ate at",
        ActionKind.Drink => "Drank at",
        ActionKind.Bask => "Basked at",
        _ => "Rested at",
    };

    public static string DeathCause(Need n) => n switch
    {
        Need.Energy => "starvation",
        Need.Hydration => "dehydration",
        Need.Warmth => "hypothermia",
        _ => "exhaustion",
    };

    /// <summary>Simulation ticks are seconds of sim time.</summary>
    public static string Time(long ticks)
    {
        long h = ticks / 3600, m = ticks / 60 % 60, s = ticks % 60;
        return $"{h:00}:{m:00}:{s:00}";
    }
}
