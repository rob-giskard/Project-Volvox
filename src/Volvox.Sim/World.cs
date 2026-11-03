namespace Volvox.Sim;

/// <summary>Height field (sampled at integer coordinates) plus a terrain type per 1×1 cell.</summary>
public sealed class Terrain
{
    public int Size { get; }
    public double[,] Height { get; }      // [Size+1, Size+1]
    public TerrainType[,] Type { get; }   // [Size, Size]
    public double MaxHeight { get; private set; }

    public Terrain(int size)
    {
        Size = size;
        Height = new double[size + 1, size + 1];
        Type = new TerrainType[size, size];
    }

    /// <summary>Smooth cosine-profile hill: clear contours and a real flat area around it.</summary>
    public void AddHill(V2 c, double radius, double height)
    {
        int x0 = Math.Max(0, (int)(c.X - radius)), x1 = Math.Min(Size, (int)(c.X + radius) + 1);
        int y0 = Math.Max(0, (int)(c.Y - radius)), y1 = Math.Min(Size, (int)(c.Y + radius) + 1);
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        {
            double d = V2.Dist(new V2(x, y), c);
            if (d >= radius) continue;
            Height[x, y] += height * 0.5 * (1 + Math.Cos(Math.PI * d / radius));
            if (Height[x, y] > MaxHeight) MaxHeight = Height[x, y];
        }
    }

    /// <summary>Irregular round patch of a terrain type.</summary>
    public void AddBlob(TerrainType type, V2 c, double radius, Rng rng)
    {
        double p1 = rng.Range(0, Math.PI * 2), p2 = rng.Range(0, Math.PI * 2);
        int reach = (int)(radius * 1.4) + 1;
        for (int x = Math.Max(0, (int)c.X - reach); x < Math.Min(Size, (int)c.X + reach); x++)
        for (int y = Math.Max(0, (int)c.Y - reach); y < Math.Min(Size, (int)c.Y + reach); y++)
        {
            var cell = new V2(x + 0.5, y + 0.5);
            var off = cell - c;
            double ang = Math.Atan2(off.Y, off.X);
            double r = radius * (1 + 0.18 * Math.Sin(3 * ang + p1) + 0.1 * Math.Sin(5 * ang + p2));
            if (off.Length < r) Type[x, y] = type;
        }
    }

    public double HeightAt(V2 p)
    {
        double x = Math.Clamp(p.X, 0, Size - 1e-6), y = Math.Clamp(p.Y, 0, Size - 1e-6);
        int ix = (int)x, iy = (int)y;
        double fx = x - ix, fy = y - iy;
        double h00 = Height[ix, iy], h10 = Height[ix + 1, iy], h01 = Height[ix, iy + 1], h11 = Height[ix + 1, iy + 1];
        double a = h00 + (h10 - h00) * fx;
        double b = h01 + (h11 - h01) * fx;
        return a + (b - a) * fy;
    }

    public TerrainType TypeAt(V2 p)
    {
        int x = Math.Clamp((int)p.X, 0, Size - 1), y = Math.Clamp((int)p.Y, 0, Size - 1);
        return Type[x, y];
    }
}

public sealed class WorldObject
{
    public int Id { get; init; }
    public ObjectKind Kind { get; init; }
    public V2 Pos { get; init; }
    public double Stock { get; set; }
    public double MaxStock { get; init; }
    public double Regen { get; init; }

    public bool IsSource => Kind is ObjectKind.BerryBush or ObjectKind.Pool or ObjectKind.Vent or ObjectKind.Hollow;
    public bool HasStock => MaxStock > 0;
    public string Label => $"{Names.Of(Kind)} #{Id}";
}

/// <summary>What an interaction did to the body. The organism feels this; it doesn't read the rules.</summary>
public sealed class Effect
{
    public double Stomach;
    public double Hydration;
    public double Warmth;
    public double Rest;
    public double Health;
    public bool Depleted;

    public double GainFor(Need n) => n switch
    {
        Need.Energy => Stomach,
        Need.Hydration => Hydration,
        Need.Warmth => Warmth,
        _ => Rest,
    };

    public string Describe()
    {
        var parts = new List<string>();
        if (Stomach != 0) parts.Add($"{Stomach:+0;-0} stomach");
        if (Hydration != 0) parts.Add($"{Hydration:+0;-0} hydration");
        if (Warmth != 0) parts.Add($"{Warmth:+0;-0} warmth");
        if (Rest != 0) parts.Add($"{Rest:+0;-0} rest");
        if (Health != 0) parts.Add($"{Health:+0;-0} health");
        if (parts.Count == 0) return Depleted ? "nothing left" : "no effect";
        return string.Join(", ", parts);
    }
}

public sealed class World
{
    public SimConfig Cfg { get; }
    public int Size { get; }
    public Terrain Terrain { get; }
    public V2 Spawn { get; }
    public List<WorldObject> Objects { get; } = new();
    private readonly List<WorldObject> _vents = new();
    private readonly List<WorldObject> _pools = new();
    private readonly List<WorldObject> _bushes = new();

    public World(SimConfig cfg, Rng rng)
    {
        Cfg = cfg;
        Size = Math.Max(60, cfg.WorldSize);
        Terrain = new Terrain(Size);
        Spawn = new V2(Size / 2.0, Size / 2.0);
        Generate(rng);
    }

    private void Generate(Rng rng)
    {
        var c = Cfg;

        // Background hills, kept away from the spawn point.
        for (int i = 0; i < c.ExtraHills; i++)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var p = new V2(rng.Range(12, Size - 12), rng.Range(12, Size - 12));
                double r = rng.Range(6, 14);
                if (V2.Dist(p, Spawn) < r + 10) continue;
                Terrain.AddHill(p, r, rng.Range(4, 11));
                break;
            }
        }

        // One source per need, roughly a quarter-turn apart, each behind its own obstacle.
        double a0 = rng.Range(0, Math.PI * 2);
        var kinds = new[] { ObjectKind.BerryBush, ObjectKind.Pool, ObjectKind.Vent, ObjectKind.Hollow };
        for (int i = 0; i < kinds.Length; i++)
        {
            double ang = a0 + i * Math.PI / 2 + rng.Range(-0.35, 0.35);
            double dist = rng.Range(c.SourceMinDistance, c.SourceMaxDistance);
            var pos = ClampInside(Spawn + V2.FromAngle(ang) * dist, 8);
            var toward = pos - Spawn;
            double realDist = toward.Length;

            switch (kinds[i])
            {
                case ObjectKind.BerryBush:
                    // A ridge between spawn and the bush: straight over it or walk around.
                    Terrain.AddHill(Spawn + toward * 0.62, Math.Max(8, realDist * 0.33), c.FoodHillHeight);
                    _bushes.Add(AddObject(ObjectKind.BerryBush, pos, 100, 100, 0.03));
                    break;
                case ObjectKind.Pool:
                    Terrain.AddHill(Spawn + toward * 0.55, Math.Max(7, realDist * 0.27), c.WaterHillHeight);
                    Terrain.AddBlob(TerrainType.Marsh, pos, c.MarshRadius, rng);
                    _pools.Add(AddObject(ObjectKind.Pool, pos, 200, 200, 0.15));
                    break;
                case ObjectKind.Vent:
                    Terrain.AddHill(Spawn + toward * 0.5, Math.Max(7, realDist * 0.28), c.HeatHillHeight);
                    _vents.Add(AddObject(ObjectKind.Vent, pos, 0, 0, 0));
                    break;
                case ObjectKind.Hollow:
                    Terrain.AddBlob(TerrainType.Thicket, pos, c.ThicketRadius, rng);
                    AddObject(ObjectKind.Hollow, pos, 0, 0, 0);
                    break;
            }
        }

        // Decoys: things to poke at that teach nothing (or teach caution).
        for (int i = 0; i < c.DecoyCount; i++)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var p = new V2(rng.Range(4, Size - 4), rng.Range(4, Size - 4));
                if (V2.Dist(p, Spawn) < 5) continue;
                if (Objects.Any(o => V2.DistSq(o.Pos, p) < 9)) continue;
                double roll = rng.NextDouble();
                if (roll < c.NightshadeShare) AddObject(ObjectKind.Nightshade, p, 40, 40, 0.01);
                else if (roll < c.NightshadeShare + (1 - c.NightshadeShare) * 0.5) AddObject(ObjectKind.Stone, p, 0, 0, 0);
                else AddObject(ObjectKind.Fern, p, 0, 0, 0);
                break;
            }
        }
    }

    private V2 ClampInside(V2 p, double margin) =>
        new(Math.Clamp(p.X, margin, Size - margin), Math.Clamp(p.Y, margin, Size - margin));

    public V2 ClampToWorld(V2 p) => ClampInside(p, 0.5);

    private WorldObject AddObject(ObjectKind kind, V2 pos, double stock, double max, double regen)
    {
        var o = new WorldObject { Id = Objects.Count, Kind = kind, Pos = pos, Stock = stock, MaxStock = max, Regen = regen };
        Objects.Add(o);
        return o;
    }

    public WorldObject? Find(int id) => id >= 0 && id < Objects.Count ? Objects[id] : null;

    public double AmbientAt(V2 p)
    {
        double t = Cfg.AmbientBase - Cfg.HeightCooling * Terrain.HeightAt(p);
        double r2 = Cfg.VentRadius * Cfg.VentRadius;
        foreach (var v in _vents)
            t += Cfg.VentWarmth * Math.Exp(-V2.DistSq(p, v.Pos) / r2);
        return t;
    }

    /// <summary>Damp air around open water (0 = dry).</summary>
    public double HumidityAt(V2 p) => Field(_pools, p, Cfg.HumidityRadius);

    /// <summary>Sweet smell around fruiting plants (0 = none).</summary>
    public double ScentAt(V2 p) => Field(_bushes, p, Cfg.ScentRadius);

    private static double Field(List<WorldObject> sources, V2 p, double radius)
    {
        double r2 = radius * radius, v = 0;
        foreach (var o in sources) v += Math.Exp(-V2.DistSq(p, o.Pos) / r2);
        return v;
    }

    public void Update()
    {
        foreach (var o in Objects)
            if (o.Regen > 0 && o.Stock < o.MaxStock)
                o.Stock = Math.Min(o.MaxStock, o.Stock + o.Regen);
    }

    /// <summary>The world's rules for what an action on an object does.</summary>
    public Effect Interact(WorldObject o, ActionKind a)
    {
        var e = new Effect();
        switch (o.Kind, a)
        {
            case (ObjectKind.BerryBush, ActionKind.Eat):
            {
                double take = Math.Min(25, o.Stock);
                if (take < 1) { e.Depleted = true; break; }
                o.Stock -= take;
                e.Stomach = take;
                break;
            }
            case (ObjectKind.Pool, ActionKind.Drink):
            {
                double take = Math.Min(20, o.Stock);
                if (take < 1) { e.Depleted = true; break; }
                o.Stock -= take;
                e.Hydration = take;
                break;
            }
            case (ObjectKind.Vent, ActionKind.Bask):
                e.Warmth = 15;
                break;
            case (ObjectKind.Hollow, ActionKind.Rest):
                e.Rest = 15;
                e.Warmth = 3;
                break;
            case (ObjectKind.Nightshade, ActionKind.Eat):
                if (o.Stock < 1) { e.Depleted = true; break; }
                o.Stock = Math.Max(0, o.Stock - 8);
                e.Stomach = 8;
                e.Hydration = -5;
                e.Health = -15;
                break;
        }
        return e;
    }
}
