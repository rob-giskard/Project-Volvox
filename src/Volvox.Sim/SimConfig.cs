using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Volvox.Sim;

/// <summary>
/// Every tunable number in one place. One tick = one second of simulated time.
/// The test runner can override any property by name (e.g. --set HydrationDrain=0.03).
/// </summary>
public sealed class SimConfig
{
    // ── World ────────────────────────────────────────────────
    public int Seed { get; set; } = 1;
    public int WorldSize { get; set; } = 200;
    public double SourceMinDistance { get; set; } = 25;
    public double SourceMaxDistance { get; set; } = 45;
    public int DecoyCount { get; set; } = 40;
    public double NightshadeShare { get; set; } = 0.1;
    public int ExtraHills { get; set; } = 7;

    // ── Terrain obstacles in front of each source ────────────
    public double FoodHillHeight { get; set; } = 14;
    public double WaterHillHeight { get; set; } = 10;
    public double HeatHillHeight { get; set; } = 8;
    public double MarshRadius { get; set; } = 8;
    public double ThicketRadius { get; set; } = 7;

    // ── Starting state (0..100) ──────────────────────────────
    public double StartEnergy { get; set; } = 75;
    public double StartHydration { get; set; } = 75;
    public double StartWarmth { get; set; } = 85;
    public double StartRest { get; set; } = 85;

    // ── Body (per tick) ──────────────────────────────────────
    public double EnergyDrain { get; set; } = 0.006;
    public double HydrationDrain { get; set; } = 0.013;
    public double RestDrain { get; set; } = 0.005;
    public double SleepRestRegen { get; set; } = 0.06;
    public double DigestRate { get; set; } = 0.05;
    public double DigestEfficiency { get; set; } = 0.9;
    public double StomachCapacity { get; set; } = 60;
    public double DeprivationDamage { get; set; } = 0.05;
    public double HealthRegen { get; set; } = 0.01;
    public long MaxAge { get; set; } = 0; // 0 = no old age

    // ── Temperature ──────────────────────────────────────────
    public double AmbientBase { get; set; } = 10;
    public double ComfortTemperature { get; set; } = 18;
    public double WarmthResponse { get; set; } = 0.002;
    public double HeightCooling { get; set; } = 0.25;
    public double VentWarmth { get; set; } = 28;
    public double VentRadius { get; set; } = 11;

    // ── Movement & terrain effort ────────────────────────────
    public double Speed { get; set; } = 0.5;
    public double MoveEnergyCost { get; set; } = 0.004;
    public double ClimbEnergyCost { get; set; } = 0.06;
    public double ClimbRestCost { get; set; } = 0.04;
    public double SlopeSlowdown { get; set; } = 1.5;
    public double MarshSpeed { get; set; } = 0.45;
    public double MarshWarmthDrain { get; set; } = 0.03;
    public double ThicketSpeed { get; set; } = 0.7;

    // ── Senses & mind ────────────────────────────────────────
    public double SenseRadius { get; set; } = 9;
    public double ThicketSenseRadius { get; set; } = 3;
    public int DecisionInterval { get; set; } = 10;
    public int ActionDuration { get; set; } = 20;
    public double Curiosity { get; set; } = 0.6;
    public int MemoryCapacity { get; set; } = 40;
    public double LocationConfidenceDecay { get; set; } = 0.00002;
    public int EffortCellSize { get; set; } = 5;
    public double EffortLearningRate { get; set; } = 0.3;
    public int EmptySourceRecheck { get; set; } = 1200;
    public int GiveUpTicks { get; set; } = 2500;
    public double ExploreMinDistance { get; set; } = 8;
    public double ExploreMaxDistance { get; set; } = 30;
    /// <summary>Exploration prefers staying within this distance of where it was born.</summary>
    public double HomeRange { get; set; } = 40;

    // ── Innate gradient senses (it feels these locally; it still has to learn what to use) ──
    public double HumidityRadius { get; set; } = 14;
    public double ScentRadius { get; set; } = 12;
    /// <summary>How strongly a cold organism drifts toward warmer air.</summary>
    public double Thermotaxis { get; set; } = 3;
    /// <summary>How strongly a thirsty organism drifts toward damper air.</summary>
    public double Hydrotaxis { get; set; } = 4;
    /// <summary>How strongly a hungry organism drifts toward sweet smells.</summary>
    public double Olfaction { get; set; } = 2;

    public SimConfig Clone() => (SimConfig)MemberwiseClone();

    public static IEnumerable<PropertyInfo> Parameters() =>
        typeof(SimConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite);

    /// <summary>Set a parameter by name (case-insensitive). Returns false if unknown.</summary>
    public bool TrySet(string name, string value, out string error)
    {
        error = "";
        var prop = Parameters().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (prop == null) { error = $"Unknown parameter '{name}'"; return false; }
        try
        {
            object converted = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
            prop.SetValue(this, converted);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Bad value '{value}' for {prop.Name} ({prop.PropertyType.Name}): {ex.Message}";
            return false;
        }
    }

    public static SimConfig Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonSerializer.Deserialize<SimConfig>(File.ReadAllText(path), options) ?? new SimConfig();
    }
}
