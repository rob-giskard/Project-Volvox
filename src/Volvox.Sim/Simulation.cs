namespace Volvox.Sim;

public sealed class RunStats
{
    public Dictionary<Need, long> FirstRemedy { get; } = new();
    public int Tries { get; set; }
    public int Uses { get; set; }
    public int ObjectsNoticed { get; set; }
    public int RouteSwitches { get; set; }
    public int Poisonings { get; set; }
    public string? Cause { get; set; }
    public long Lifetime { get; set; }
}

/// <summary>
/// The whole simulation. No UI references: the desktop app renders it, the runner fast-forwards it.
/// Call Step() once per tick (one tick = one second of sim time).
/// </summary>
public sealed class Simulation
{
    public SimConfig Config { get; }
    public Rng Rng { get; }
    public World World { get; }
    public Organism Organism { get; }
    public Mind Mind { get; }
    public EventLog Log { get; }
    public RunStats Stats { get; } = new();
    public long Tick { get; private set; }
    public bool Finished => !Organism.Alive;

    public Simulation(SimConfig config, bool logging = true)
    {
        Config = config;
        Rng = new Rng((ulong)config.Seed * 2654435761UL + 1442695040888963407UL);
        Log = new EventLog { Enabled = logging };
        World = new World(config, Rng);
        Organism = new Organism(1, World.Spawn, 0, config);
        Organism.Ambient = World.AmbientAt(Organism.Pos);
        Mind = new Mind(this);

        int sources = World.Objects.Count(o => o.IsSource);
        Emit(EventCategory.ENV, "World", "",
            $"World {World.Size}×{World.Size}, {sources} sources, {World.Objects.Count - sources} other objects, seed {config.Seed}");
        Emit(EventCategory.BIO, "Born", "", $"Organism #{Organism.Id} appears at {Organism.Pos} — it knows nothing");
    }

    internal void Emit(EventCategory cat, string type, string subject, string text, V2? pos = null)
    {
        if (!Log.Enabled) return;
        Log.Add(Tick, Organism?.Age ?? 0, cat, type, Organism?.Id ?? 0, subject, pos ?? Organism?.Pos ?? default, text);
    }

    public void Step()
    {
        if (!Organism.Alive) return;
        Tick++;
        Organism.Age = Tick - Organism.BornTick;
        World.Update();
        Organism.Ambient = World.AmbientAt(Organism.Pos);
        Mind.Sense();
        Mind.Update();
        StepBiology();
    }

    /// <summary>Run headless until death or tick limit.</summary>
    public void Run(long maxTicks)
    {
        while (!Finished && Tick < maxTicks) Step();
        if (Organism.Alive) Stats.Lifetime = Organism.Age;
    }

    private void StepBiology()
    {
        var o = Organism;
        var c = Config;

        o.Energy -= c.EnergyDrain * (o.Sleeping ? 0.5 : 1);
        double digest = Math.Min(o.Stomach, c.DigestRate);
        o.Stomach -= digest;
        o.Energy += digest * c.DigestEfficiency;

        o.Hydration -= c.HydrationDrain * (1 + Math.Max(0, o.Ambient - 25) * 0.03);
        o.Warmth += (o.Ambient - c.ComfortTemperature) * c.WarmthResponse;
        if (o.Sleeping) o.Rest += c.SleepRestRegen;
        else o.Rest -= c.RestDrain;
        o.Clamp(c.StomachCapacity);

        int zeros = 0;
        bool allOk = true;
        foreach (var n in Names.Needs)
        {
            double v = o.Get(n);
            int i = (int)n;
            if (v <= 0) { zeros++; o.ZeroTicks[i]++; }
            if (v <= 30) allOk = false;

            if (v < 20 && !o.Critical[i])
            {
                o.Critical[i] = true;
                Emit(EventCategory.BIO, "Critical", "", $"{Capital(Names.Of(n))} critical ({v:0}%)");
            }
            else if (v > 45 && o.Critical[i])
            {
                o.Critical[i] = false;
                Emit(EventCategory.BIO, "Recovered", "", $"{Capital(Names.Of(n))} recovered ({v:0}%)");
            }
        }

        if (zeros > 0)
        {
            o.Health -= c.DeprivationDamage * zeros;
            o.LastDamage = "deprivation";
        }
        else if (allOk && o.Health < 100)
        {
            o.Health = Math.Min(100, o.Health + c.HealthRegen);
        }

        if (o.Health <= 0)
        {
            string cause;
            if (o.LastDamage == "poison" && zeros == 0) cause = "poisoning";
            else
            {
                var worst = Names.Needs.MaxBy(n => o.ZeroTicks[(int)n]);
                cause = Names.DeathCause(worst);
            }
            Die(cause);
        }
        else if (c.MaxAge > 0 && o.Age >= c.MaxAge)
        {
            Die("old age");
        }
    }

    private void Die(string cause)
    {
        var o = Organism;
        o.Alive = false;
        o.Sleeping = false;
        o.Acting = false;
        o.CauseOfDeath = cause;
        Stats.Cause = cause;
        Stats.Lifetime = o.Age;
        Emit(EventCategory.BIO, "Died", "", $"Organism #{o.Id} died of {cause} at age {Names.Time(o.Age)}");
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
