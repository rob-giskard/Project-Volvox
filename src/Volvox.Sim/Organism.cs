namespace Volvox.Sim;

public sealed class Organism
{
    public int Id { get; }
    public V2 Pos { get; set; }
    public V2 Heading { get; set; } = new(1, 0);
    /// <summary>Where it was born — the centre of its home range.</summary>
    public V2 Home { get; }

    public double Energy { get; set; }
    public double Stomach { get; set; }
    public double Hydration { get; set; }
    public double Warmth { get; set; }
    public double Rest { get; set; }
    public double Health { get; set; } = 100;
    public double Ambient { get; set; }

    public long BornTick { get; }
    public long Age { get; set; }
    public bool Alive { get; set; } = true;
    public bool Sleeping { get; set; }
    public bool Acting { get; set; }
    public string? CauseOfDeath { get; set; }
    public string LastDamage { get; set; } = "";
    public double Distance { get; set; }

    public long[] ZeroTicks { get; } = new long[4];
    public bool[] Critical { get; } = new bool[4];

    /// <summary>Sampled positions (not every tick).</summary>
    public List<V2> Trail { get; } = new();
    public int TrailDropped { get; private set; }
    private V2 _lastTrail;

    public Organism(int id, V2 pos, long bornTick, SimConfig cfg)
    {
        Id = id;
        Pos = pos;
        Home = pos;
        _lastTrail = pos;
        Trail.Add(pos);
        BornTick = bornTick;
        Energy = cfg.StartEnergy;
        Hydration = cfg.StartHydration;
        Warmth = cfg.StartWarmth;
        Rest = cfg.StartRest;
    }

    public double Get(Need n) => n switch
    {
        Need.Energy => Energy,
        Need.Hydration => Hydration,
        Need.Warmth => Warmth,
        _ => Rest,
    };

    public void Add(Need n, double d)
    {
        switch (n)
        {
            case Need.Energy: Energy += d; break;
            case Need.Hydration: Hydration += d; break;
            case Need.Warmth: Warmth += d; break;
            default: Rest += d; break;
        }
    }

    public void Clamp(double stomachCap)
    {
        Energy = Math.Clamp(Energy, 0, 100);
        Hydration = Math.Clamp(Hydration, 0, 100);
        Warmth = Math.Clamp(Warmth, 0, 100);
        Rest = Math.Clamp(Rest, 0, 100);
        Stomach = Math.Clamp(Stomach, 0, stomachCap);
        Health = Math.Clamp(Health, 0, 100);
    }

    public void SampleTrail()
    {
        if (V2.DistSq(Pos, _lastTrail) < 0.6 * 0.6) return;
        Trail.Add(Pos);
        _lastTrail = Pos;
        if (Trail.Count > 20_000)
        {
            Trail.RemoveRange(0, 2_000);
            TrailDropped += 2_000;
        }
    }
}
