using System.Diagnostics;
using System.Globalization;
using System.Text;
using Volvox.Sim;

namespace Volvox.Runner;

/// <summary>
/// Headless "speed mode": runs many seeds of the simulation as fast as the CPU allows
/// and reports how organisms lived and died. Used for tuning balance.
/// </summary>
internal static class Program
{
    private sealed record Result(int Seed, long Lifetime, bool Survived, string Cause, RunStats Stats, double Ms);

    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        int runs = 20, seed = 1, threads = Environment.ProcessorCount, traceSeed = -1;
        long maxTicks = 100_000;
        string? configPath = null, outPath = null;
        var overrides = new List<(string Key, string Value)>();
        bool quiet = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value after {a}");
            switch (a.ToLowerInvariant())
            {
                case "--runs": runs = int.Parse(Next()); break;
                case "--seed": seed = int.Parse(Next()); break;
                case "--max-ticks": maxTicks = long.Parse(Next()); break;
                case "--hours": maxTicks = (long)(double.Parse(Next()) * 3600); break;
                case "--threads": threads = Math.Max(1, int.Parse(Next())); break;
                case "--config": configPath = Next(); break;
                case "--out": outPath = Next(); break;
                case "--trace": traceSeed = int.Parse(Next()); break;
                case "--quiet": quiet = true; break;
                case "--set":
                    AddOverride(Next(), overrides);
                    break;
                case "--params":
                    PrintParams(new SimConfig());
                    return 0;
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    if (a.Contains('=')) AddOverride(a, overrides);
                    else { Console.Error.WriteLine($"Unknown argument: {a}  (try --help)"); return 2; }
                    break;
            }
        }

        SimConfig baseCfg;
        if (configPath == null && File.Exists("volvox.config.json")) configPath = "volvox.config.json";
        try { baseCfg = configPath != null ? SimConfig.Load(configPath) : new SimConfig(); }
        catch (Exception ex) { Console.Error.WriteLine($"Could not read config {configPath}: {ex.Message}"); return 2; }

        foreach (var (key, value) in overrides)
        {
            if (!baseCfg.TrySet(key, value, out string err)) { Console.Error.WriteLine(err); return 2; }
        }

        if (traceSeed >= 0) return Trace(baseCfg, traceSeed, maxTicks);

        Console.WriteLine($"VOLVOX runner · {runs} runs from seed {seed} · up to {maxTicks:N0} ticks ({maxTicks / 3600.0:0.#} sim h) · {threads} threads");
        if (configPath != null) Console.WriteLine($"config: {configPath}");
        foreach (var (key, value) in overrides) Console.WriteLine($"override: {key} = {value}");
        Console.WriteLine();

        var results = new Result[runs];
        var total = Stopwatch.StartNew();
        Parallel.For(0, runs, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var cfg = baseCfg.Clone();
            cfg.Seed = seed + i;
            var sw = Stopwatch.StartNew();
            var sim = new Simulation(cfg, logging: false);
            sim.Run(maxTicks);
            results[i] = new Result(cfg.Seed, sim.Organism.Age, sim.Organism.Alive,
                sim.Organism.Alive ? "-" : sim.Organism.CauseOfDeath ?? "?", sim.Stats, sw.Elapsed.TotalMilliseconds);
        });
        total.Stop();

        if (!quiet) PrintTable(results);
        PrintSummary(results, maxTicks, total.Elapsed);
        if (outPath != null)
        {
            WriteCsv(outPath, results);
            Console.WriteLine($"\nwrote {outPath}");
        }
        return 0;
    }

    private static void AddOverride(string kv, List<(string, string)> list)
    {
        int eq = kv.IndexOf('=');
        if (eq <= 0) throw new ArgumentException($"Expected Key=Value, got '{kv}'");
        list.Add((kv[..eq].Trim(), kv[(eq + 1)..].Trim()));
    }

    private static string T(long? ticks) => ticks is long t ? Names.Time(t) : "   -    ";

    private static void PrintTable(Result[] results)
    {
        Console.WriteLine(" seed   lifetime  status        food      water     warmth    rest    tries  uses  reroutes");
        Console.WriteLine(" ────  ─────────  ───────────  ────────  ────────  ────────  ────────  ─────  ────  ────────");
        foreach (var r in results)
        {
            var f = r.Stats.FirstRemedy;
            long? Get(Need n) => f.TryGetValue(n, out long v) ? v : null;
            string status = r.Survived ? "SURVIVED" : r.Cause;
            Console.WriteLine(
                $" {r.Seed,4}  {Names.Time(r.Lifetime),9}  {status,-11}  {T(Get(Need.Energy))}  {T(Get(Need.Hydration))}  " +
                $"{T(Get(Need.Warmth))}  {T(Get(Need.Rest))}  {r.Stats.Tries,5}  {r.Stats.Uses,4}  {r.Stats.RouteSwitches,8}");
        }
        Console.WriteLine();
    }

    private static void PrintSummary(Result[] results, long maxTicks, TimeSpan elapsed)
    {
        int n = results.Length;
        int survived = results.Count(r => r.Survived);
        var lifetimes = results.Select(r => r.Lifetime).OrderBy(x => x).ToArray();
        long median = lifetimes[n / 2];
        double totalTicks = results.Sum(r => (double)r.Lifetime);

        Console.WriteLine("SUMMARY");
        Console.WriteLine($"  survived to the limit   {survived}/{n} ({100.0 * survived / n:0}%)");
        Console.WriteLine($"  lifetime median / mean  {Names.Time(median)} / {Names.Time((long)(totalTicks / n))}");
        Console.WriteLine($"  lifetime min / max      {Names.Time(lifetimes[0])} / {Names.Time(lifetimes[^1])}");

        int foundAny = results.Count(r => r.Stats.FirstRemedy.Count > 0);
        Console.WriteLine($"  found a first remedy    {foundAny}/{n} ({100.0 * foundAny / n:0}%)   ← tuning target ~30–60% early on");

        foreach (var need in Names.Needs)
        {
            var times = results.Where(r => r.Stats.FirstRemedy.ContainsKey(need))
                .Select(r => r.Stats.FirstRemedy[need]).OrderBy(x => x).ToArray();
            string med = times.Length > 0 ? Names.Time(times[times.Length / 2]) : "-";
            Console.WriteLine($"  learned {Names.Of(need),-10}      {times.Length,3}/{n}   median at T+{med}");
        }

        var causes = results.Where(r => !r.Survived).GroupBy(r => r.Cause).OrderByDescending(g => g.Count());
        Console.WriteLine("  causes of death         " + (causes.Any()
            ? string.Join(", ", causes.Select(g => $"{g.Key} {g.Count()}"))
            : "none"));
        Console.WriteLine($"  rerouted at least once  {results.Count(r => r.Stats.RouteSwitches > 0)}/{n}");
        Console.WriteLine($"  poisoned at least once  {results.Count(r => r.Stats.Poisonings > 0)}/{n}");
        double tps = totalTicks / Math.Max(0.001, elapsed.TotalSeconds);
        Console.WriteLine($"  wall time               {elapsed.TotalSeconds:0.0}s  ({tps:N0} ticks/s overall)");
    }

    private static void WriteCsv(string path, Result[] results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("seed,lifetime_ticks,survived,cause,first_food,first_water,first_warmth,first_rest,tries,uses,objects_noticed,reroutes,poisonings,ms");
        foreach (var r in results)
        {
            var f = r.Stats.FirstRemedy;
            string G(Need n) => f.TryGetValue(n, out long v) ? v.ToString() : "";
            sb.AppendLine(string.Join(",",
                r.Seed, r.Lifetime, r.Survived ? 1 : 0, r.Cause,
                G(Need.Energy), G(Need.Hydration), G(Need.Warmth), G(Need.Rest),
                r.Stats.Tries, r.Stats.Uses, r.Stats.ObjectsNoticed, r.Stats.RouteSwitches, r.Stats.Poisonings,
                r.Ms.ToString("0")));
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Run one seed with logging on and print its full event log.</summary>
    private static int Trace(SimConfig baseCfg, int seed, long maxTicks)
    {
        var cfg = baseCfg.Clone();
        cfg.Seed = seed;
        var sim = new Simulation(cfg);
        sim.Log.Capacity = int.MaxValue;
        sim.Run(maxTicks);
        foreach (var ev in sim.Log.Events) Console.WriteLine(ev);
        Console.WriteLine();
        Console.WriteLine(sim.Organism.Alive
            ? $"Survived {Names.Time(sim.Organism.Age)}"
            : $"Died of {sim.Organism.CauseOfDeath} at {Names.Time(sim.Organism.Age)}");
        return 0;
    }

    private static void PrintParams(SimConfig cfg)
    {
        Console.WriteLine("Parameters (override with --set Name=Value or Name=Value):\n");
        foreach (var p in SimConfig.Parameters())
            Console.WriteLine($"  {p.Name,-26} {Convert.ToString(p.GetValue(cfg), CultureInfo.InvariantCulture),-10} ({p.PropertyType.Name})");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            VolvoxRunner — headless speed-mode test runs

            usage: VolvoxRunner [options] [Name=Value ...]

              --runs N          number of runs (default 20), seeds are consecutive
              --seed S          first seed (default 1)
              --max-ticks T     tick limit per run (default 100000; 1 tick = 1 sim second)
              --hours H         tick limit in sim hours instead
              --threads K       parallel runs (default: CPU count)
              --config FILE     JSON parameter file (default: volvox.config.json if present)
              --set Name=Value  override one parameter (repeatable; bare Name=Value also works)
              --out FILE.csv    write per-run results as CSV
              --trace SEED      run one seed and print its full event log
              --params          list every parameter with its default
              --quiet           summary only

            examples:
              VolvoxRunner --runs 100 --hours 24
              VolvoxRunner --runs 50 HydrationDrain=0.03 SourceMaxDistance=60 --out thirsty.csv
              VolvoxRunner --trace 7 --hours 2 > seed7.log
            """);
    }
}
