namespace Volvox.Sim;

public enum IntentType { Idle, Explore, Investigate, GoUse, Sleep }

public sealed class Intent
{
    public IntentType Type { get; init; }
    public LocationMemory? Target { get; init; }
    public ActionKind Action { get; init; }
    public Need? ForNeed { get; init; }
    public V2 Goal { get; set; }
    public double Score { get; init; }

    public static readonly Intent Idle = new() { Type = IntentType.Idle };

    public string Key => Type switch
    {
        IntentType.Explore => "EXPLORE",
        IntentType.Sleep => "SLEEP",
        IntentType.Investigate => $"TRY {Names.Of(Action)} @ {Target?.Label}",
        IntentType.GoUse => $"{Names.Of(Action)} @ {Target?.Label}",
        _ => "IDLE",
    };
}

/// <summary>A scored option the mind considered. Kept for the inspector's "why".</summary>
public sealed record Candidate(IntentType Type, string Key, double Score, LocationMemory? Target, ActionKind Action, Need? ForNeed);

/// <summary>
/// Senses, remembers, decides and moves. Knows only what it has sensed or experienced.
/// Touching an object is the only moment it interacts with the world's true state.
/// </summary>
public sealed class Mind
{
    private readonly Simulation _s;
    private SimConfig C => _s.Config;
    private Organism O => _s.Organism;

    public List<Association> Associations { get; } = new();
    public List<LocationMemory> Locations { get; } = new();
    public EffortGrid Effort { get; }
    public Intent Current { get; private set; } = Intent.Idle;
    public List<V2> Path { get; private set; } = new();
    public int PathIndex { get; private set; }
    public IReadOnlyList<Candidate> LastScores => _lastScores;
    public int ActionTicksLeft { get; private set; }

    private List<Candidate> _lastScores = new();
    private long _nextDecision;
    private long _intentStart;
    private long _lastSenseLog = -10_000;
    private string _lastLoggedKey = "";
    private readonly Dictionary<int, bool> _routeIsDetour = new();
    private readonly double[] _urg = new double[4];

    public Mind(Simulation s)
    {
        _s = s;
        var c = s.Config;
        double coldDrain = Math.Max(0, (c.ComfortTemperature - c.AmbientBase) * c.WarmthResponse);
        double flatPerTick = c.EnergyDrain + c.HydrationDrain + c.RestDrain + coldDrain + c.MoveEnergyCost * c.Speed;
        Effort = new EffortGrid(s.World.Size, c.EffortCellSize, flatPerTick / c.Speed, c.EffortLearningRate);
    }

    // ── Knowledge queries ────────────────────────────────────

    public LocationMemory? FindLocation(int objectId) => Locations.FirstOrDefault(l => l.ObjectId == objectId);

    public Association? FindAssociation(ObjectKind k, ActionKind a) =>
        Associations.FirstOrDefault(x => x.Kind == k && x.Action == a);

    public bool IsHarmful(ObjectKind k) => Associations.Any(a => a.Kind == k && a.Harmful);

    private IEnumerable<ActionKind> UntriedActions(ObjectKind k)
    {
        foreach (var n in Names.Needs)
        {
            var a = Names.ActionFor(n);
            if (FindAssociation(k, a) == null) yield return a;
        }
    }

    /// <summary>What a kind of object is known to be good for, if anything.</summary>
    public string Describe(ObjectKind k)
    {
        if (IsHarmful(k)) return "harmful";
        var useful = Associations.Where(a => a.Kind == k && a.Useful).Select(a => Names.Of(a.Need!.Value)).ToList();
        if (useful.Count > 0) return string.Join("+", useful) + " source";
        bool anyTried = Associations.Any(a => a.Kind == k);
        bool allTried = !UntriedActions(k).Any();
        return allTried ? "useless" : anyTried ? "unclear" : "unknown";
    }

    // ── Senses ───────────────────────────────────────────────

    public void Sense()
    {
        var w = _s.World;
        bool inThicket = w.Terrain.TypeAt(O.Pos) == TerrainType.Thicket;
        double r = inThicket ? C.ThicketSenseRadius : C.SenseRadius;
        double r2 = r * r, thicket2 = C.ThicketSenseRadius * C.ThicketSenseRadius;

        foreach (var obj in w.Objects)
        {
            double d2 = V2.DistSq(obj.Pos, O.Pos);
            if (d2 > r2) continue;
            if (w.Terrain.TypeAt(obj.Pos) == TerrainType.Thicket && d2 > thicket2) continue;

            var loc = FindLocation(obj.Id);
            if (loc != null)
            {
                loc.Confidence = 1;
                loc.LastConfirmed = _s.Tick;
                continue;
            }
            Remember(obj);
        }

        Effort.MarkSeen(O.Pos, r);
        Effort.Visits[Effort.Index(O.Pos)]++;

        if (_s.Tick % 50 == 0) DecayMemory(50);
    }

    private void Remember(WorldObject obj)
    {
        if (Locations.Count >= C.MemoryCapacity) Forget(Locations.Where(l => l != Current.Target).MinBy(l => l.Confidence));
        var loc = new LocationMemory
        {
            ObjectId = obj.Id, Kind = obj.Kind, Pos = obj.Pos,
            FirstSeen = _s.Tick, LastConfirmed = _s.Tick,
        };
        Locations.Add(loc);
        _s.Stats.ObjectsNoticed++;
        string known = Describe(obj.Kind);
        string note = known == "unknown" ? "" : $" — recognised as {known}";
        _s.Emit(EventCategory.COG, "Noticed", loc.Label, $"Noticed {loc.Label} at {obj.Pos}{note}", obj.Pos);
    }

    private void Forget(LocationMemory? loc)
    {
        if (loc == null) return;
        Locations.Remove(loc);
        _s.Emit(EventCategory.COG, "Forgot", loc.Label, $"Forgot where {loc.Label} was", loc.Pos);
    }

    private void DecayMemory(int ticks)
    {
        for (int i = Locations.Count - 1; i >= 0; i--)
        {
            var l = Locations[i];
            l.Confidence -= C.LocationConfidenceDecay * ticks;
            if (l.Confidence < 0.1 && l != Current.Target) Forget(l);
        }
    }

    // ── Per-tick behaviour ───────────────────────────────────

    public void Update()
    {
        if (O.Acting)
        {
            ActionTicksLeft--;
            if (ActionTicksLeft <= 0) CompleteAction();
            return;
        }

        if (Current.Type == IntentType.Idle || _s.Tick >= _nextDecision) Decide();
        Execute();
    }

    private void ComputeUrgency()
    {
        foreach (var n in Names.Needs)
        {
            double v = O.Get(n);
            double u = 1 - v / 100;
            u *= u;
            if (v < 20) u += 0.4;
            _urg[(int)n] = u;
        }
    }

    private void Decide()
    {
        _nextDecision = _s.Tick + C.DecisionInterval;
        ComputeUrgency();
        double maxUrg = _urg.Max();
        Need top = Names.Needs.MaxBy(n => _urg[(int)n]);
        var cands = new List<Candidate>();

        // 1. Go to a remembered thing that is known to help a need.
        foreach (var loc in Locations)
        {
            if (loc.EmptyUntil > _s.Tick) continue;
            double dist = V2.Dist(O.Pos, loc.Pos);
            foreach (var a in Associations)
            {
                if (a.Kind != loc.Kind || !a.Useful) continue;
                var need = a.Need!.Value;
                if (O.Get(need) > 85) continue;
                double belief = (0.5 + 0.5 * a.Confidence) * loc.Confidence * Math.Min(1, a.MeanGain / 15);
                double score = 2.0 * _urg[(int)need] * belief / (1 + dist / 60);
                cands.Add(new Candidate(IntentType.GoUse, $"{Names.Of(a.Action)} @ {loc.Label}", score, loc, a.Action, need));
            }
        }

        // 2. Experiment on something not yet understood.
        foreach (var loc in Locations)
        {
            if (IsHarmful(loc.Kind)) continue;
            var untried = UntriedActions(loc.Kind).ToList();
            if (untried.Count == 0) continue;
            var action = untried.MaxBy(a => _urg[(int)Names.NeedFor(a)]);
            double dist = V2.Dist(O.Pos, loc.Pos);
            double score = C.Curiosity * (0.5 + maxUrg) / (1 + dist / 15);
            cands.Add(new Candidate(IntentType.Investigate, $"TRY {Names.Of(action)} @ {loc.Label}", score, loc, action, Names.NeedFor(action)));
        }

        // 3. Explore — stronger when the most urgent need has no known remedy.
        bool remedyKnown = cands.Any(c => c.Type == IntentType.GoUse && c.ForNeed == top);
        double explore = 0.1 + (remedyKnown ? 0.1 : 0.6) * maxUrg;
        cands.Add(new Candidate(IntentType.Explore, "EXPLORE", explore, null, ActionKind.Rest, null));

        // 4. Sleep where it stands (innate). Worse when something else is pressing.
        if (O.Rest < 45)
        {
            double others = Names.Needs.Where(n => n != Need.Rest).Max(n => _urg[(int)n]);
            double sleep = 0.9 * _urg[(int)Need.Rest] + (O.Rest < 15 ? 0.4 : 0) - 0.5 * others;
            if (O.Sleeping && O.Rest > 80) sleep = 0;
            cands.Add(new Candidate(IntentType.Sleep, "SLEEP", sleep, null, ActionKind.Rest, Need.Rest));
        }
        else if (O.Sleeping && O.Rest < 85)
        {
            // Keep sleeping until properly rested unless something beats it.
            cands.Add(new Candidate(IntentType.Sleep, "SLEEP", 0.25, null, ActionKind.Rest, Need.Rest));
        }

        // Hysteresis: finishing what it started is worth a little.
        if (Current.Type != IntentType.Idle)
        {
            for (int i = 0; i < cands.Count; i++)
                if (cands[i].Key == Current.Key) cands[i] = cands[i] with { Score = cands[i].Score + 0.15 };
        }

        cands.Sort((a, b) => b.Score.CompareTo(a.Score));
        _lastScores = cands.Take(4).ToList();
        var best = cands[0];

        if (Current.Type != IntentType.Idle && best.Key == Current.Key) return;
        SetIntent(best);
    }

    private void SetIntent(Candidate c)
    {
        Current = new Intent { Type = c.Type, Target = c.Target, Action = c.Action, ForNeed = c.ForNeed, Score = c.Score };
        Path = new List<V2>();
        PathIndex = 0;
        _intentStart = _s.Tick;
        O.Sleeping = c.Type == IntentType.Sleep;

        if (c.Key != _lastLoggedKey)
        {
            _lastLoggedKey = c.Key;
            string why = c.ForNeed != null ? $" for {Names.Of(c.ForNeed.Value)}" : "";
            _s.Emit(EventCategory.BEH, "Intent", c.Target?.Label ?? "", $"Intent → {c.Key}{why}  (score {c.Score:0.00})");
        }

        if (c.Type == IntentType.Explore)
        {
            Current.Goal = PickExploreGoal();
            PlanPath(Current.Goal, null);
        }
        else if (c.Target != null)
        {
            Current.Goal = c.Target.Pos;
            PlanPath(c.Target.Pos, c.Target);
        }
    }

    private void Execute()
    {
        switch (Current.Type)
        {
            case IntentType.Explore:
                if (PathIndex >= Path.Count)
                {
                    Current.Goal = PickExploreGoal();
                    PlanPath(Current.Goal, null);
                }
                MoveStep();
                break;

            case IntentType.Investigate:
            case IntentType.GoUse:
            {
                var t = Current.Target!;
                if (V2.Dist(O.Pos, t.Pos) <= 1.2)
                {
                    O.Acting = true;
                    ActionTicksLeft = C.ActionDuration;
                    Path = new List<V2>();
                    PathIndex = 0;
                    return;
                }
                if (_s.Tick - _intentStart > C.GiveUpTicks)
                {
                    t.Confidence *= 0.5;
                    _s.Emit(EventCategory.BEH, "GaveUp", t.Label, $"Gave up trying to reach {t.Label}", t.Pos);
                    Current = Intent.Idle;
                    return;
                }
                if (PathIndex >= Path.Count) PlanPath(t.Pos, t);
                MoveStep();
                break;
            }

            case IntentType.Sleep:
                O.Sleeping = true;
                break;
        }
    }

    private double Urgency(Need n)
    {
        double v = O.Get(n);
        double u = 1 - v / 100;
        return u * u + (v < 20 ? 0.4 : 0);
    }

    /// <summary>
    /// Choose where to wander next: unseen ground, roughly ahead, not too far from home,
    /// not through remembered hard terrain — and, innately, toward warmer, damper or sweeter air
    /// when cold, thirsty or hungry. Those gradients are felt right where it stands.
    /// </summary>
    private V2 PickExploreGoal()
    {
        var rng = _s.Rng;
        var w = _s.World;
        double size = w.Size;
        double uWarm = Urgency(Need.Warmth), uThirst = Urgency(Need.Hydration), uHunger = Urgency(Need.Energy);
        double temp0 = O.Ambient, hum0 = w.HumidityAt(O.Pos), scent0 = w.ScentAt(O.Pos);

        V2 best = O.Pos;
        double bestScore = double.NegativeInfinity;
        double bestPull = 0;
        string bestSense = "";
        for (int k = 0; k < 16; k++)
        {
            var dir = V2.FromAngle(rng.Range(0, Math.PI * 2));
            double d = rng.Range(C.ExploreMinDistance, C.ExploreMaxDistance);
            var p = O.Pos + dir * d;
            p = new V2(Math.Clamp(p.X, 3, size - 3), Math.Clamp(p.Y, 3, size - 3));

            double novelty = Effort.UnseenFraction(p, 8);
            double heading = 0.15 * V2.Dot(dir, O.Heading);
            double costPenalty = 0.3 * (Effort.CostAt(Effort.Index(p)) / Effort.Guess - 1);
            double away = 0.6 * Math.Max(0, (V2.Dist(p, O.Home) - C.HomeRange) / C.HomeRange);

            // Local gradient: what the air feels like a couple of steps in that direction.
            var probe = w.ClampToWorld(O.Pos + dir * 2);
            double warm = C.Thermotaxis * uWarm * (w.AmbientAt(probe) - temp0) / 2;
            double damp = C.Hydrotaxis * uThirst * (w.HumidityAt(probe) - hum0) * 10;
            double sweet = C.Olfaction * uHunger * (w.ScentAt(probe) - scent0) * 10;

            double score = novelty + heading - 0.2 * (d / C.ExploreMaxDistance) - costPenalty - away
                           + warm + damp + sweet + rng.Range(0, 0.05);
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
                double pull = Math.Max(warm, Math.Max(damp, sweet));
                bestPull = pull;
                bestSense = pull == warm ? "warmer air" : pull == damp ? "damp air" : "a sweet smell";
            }
        }

        if (bestPull > 0.12 && _s.Tick - _lastSenseLog > 600)
        {
            _lastSenseLog = _s.Tick;
            _s.Emit(EventCategory.BEH, "Sensed", "", $"Drawn toward {bestSense} ({Compass(best - O.Pos)})");
        }
        return best;
    }

    private static string Compass(V2 d)
    {
        // Screen coordinates: +Y is south.
        string[] names = { "E", "SE", "S", "SW", "W", "NW", "N", "NE" };
        double a = Math.Atan2(d.Y, d.X);
        int i = (int)Math.Round(a / (Math.PI / 4));
        return names[((i % 8) + 8) % 8];
    }

    private void PlanPath(V2 goal, LocationMemory? target)
    {
        Path = Effort.FindPath(O.Pos, goal);
        PathIndex = 0;
        if (target != null) CheckRoute(target, goal);
    }

    /// <summary>Notices (and logs) when the remembered effort makes it take a different route to a place.</summary>
    private void CheckRoute(LocationMemory target, V2 goal)
    {
        double straight = V2.Dist(O.Pos, goal);
        if (straight < 12) return;
        double len = 0;
        var prev = O.Pos;
        foreach (var p in Path) { len += V2.Dist(prev, p); prev = p; }
        bool detour = len > straight * 1.15;

        bool known = _routeIsDetour.TryGetValue(target.ObjectId, out bool wasDetour);
        _routeIsDetour[target.ObjectId] = detour;
        if (known && wasDetour == detour) return;
        if (!known && !detour) return;

        double pathCost = Effort.PathCost(O.Pos, Path);
        double directCost = Effort.LineCost(O.Pos, goal);
        if (known) _s.Stats.RouteSwitches++;
        string text = detour
            ? $"Route to {target.Label}: taking a detour ({len:0} units) — remembered effort {pathCost:0.0} vs {directCost:0.0} straight"
            : $"Route to {target.Label}: back to the direct path — remembered effort {pathCost:0.0}";
        _s.Emit(EventCategory.COG, "Route", target.Label, text);
    }

    private void MoveStep()
    {
        if (PathIndex >= Path.Count) return;
        var w = _s.World;
        var wp = Path[PathIndex];
        var delta = wp - O.Pos;
        double dist = delta.Length;
        if (dist < 1e-6) { PathIndex++; return; }
        var dir = delta / dist;

        var type = w.Terrain.TypeAt(O.Pos);
        double mul = type switch
        {
            TerrainType.Marsh => C.MarshSpeed,
            TerrainType.Thicket => C.ThicketSpeed,
            _ => 1.0,
        };
        double h0 = w.Terrain.HeightAt(O.Pos);
        double hAhead = w.Terrain.HeightAt(w.ClampToWorld(O.Pos + dir * 0.5));
        double grad = Math.Max(0, (hAhead - h0) / 0.5);
        mul /= 1 + C.SlopeSlowdown * grad;
        if (O.Energy < 15) mul *= 0.6;
        if (O.Rest < 15) mul *= 0.6;

        double step = Math.Min(dist, C.Speed * mul);
        var np = w.ClampToWorld(O.Pos + dir * step);
        double dh = w.Terrain.HeightAt(np) - h0;

        double eCost = C.MoveEnergyCost * step + Math.Max(0, dh) * C.ClimbEnergyCost;
        double rCost = Math.Max(0, dh) * C.ClimbRestCost;
        double wCost = type == TerrainType.Marsh ? C.MarshWarmthDrain : 0;
        O.Energy -= eCost;
        O.Rest -= rCost;
        O.Warmth -= wCost;

        // What this stretch felt like: everything drained this tick, per unit of ground covered.
        double baseDrain = C.EnergyDrain + C.HydrationDrain + C.RestDrain
                           + Math.Max(0, (C.ComfortTemperature - O.Ambient) * C.WarmthResponse);
        if (step > 0.02) Effort.Record(O.Pos, (eCost + rCost + wCost + baseDrain) / step);

        O.Heading = dir;
        O.Distance += step;
        O.Pos = np;
        if (step >= dist - 1e-9) PathIndex++;
        O.SampleTrail();
    }

    private void CompleteAction()
    {
        O.Acting = false;
        var intent = Current;
        Current = Intent.Idle;
        var target = intent.Target;
        if (target == null) return;

        var obj = _s.World.Find(target.ObjectId);
        if (obj == null || V2.Dist(obj.Pos, O.Pos) > 2.5)
        {
            target.Confidence *= 0.3;
            _s.Emit(EventCategory.COG, "Missing", target.Label, $"Nothing at the remembered spot of {target.Label}", target.Pos);
            return;
        }

        var effect = _s.World.Interact(obj, intent.Action);
        O.Stomach += effect.Stomach;
        O.Hydration += effect.Hydration;
        O.Warmth += effect.Warmth;
        O.Rest += effect.Rest;
        O.Health += effect.Health;
        if (effect.Health < 0) O.LastDamage = "poison";
        O.Clamp(C.StomachCapacity);

        if (intent.Type == IntentType.Investigate)
        {
            _s.Stats.Tries++;
            _s.Emit(EventCategory.BEH, "Tried", target.Label, $"Tried {Names.Of(intent.Action)} on {target.Label} → {effect.Describe()}", target.Pos);
        }
        else
        {
            _s.Stats.Uses++;
            _s.Emit(EventCategory.BEH, "Used", target.Label, $"{Names.Verb(intent.Action)} {target.Label}: {effect.Describe()}", target.Pos);
        }

        Learn(target, intent.Action, effect);
    }

    private void Learn(LocationMemory loc, ActionKind action, Effect effect)
    {
        var a = FindAssociation(loc.Kind, action);
        bool isNew = a == null;
        if (a == null)
        {
            a = new Association { Kind = loc.Kind, Action = action, LearnedTick = _s.Tick };
            Associations.Add(a);
        }

        var need = Names.NeedFor(action);
        double gain = effect.GainFor(need);
        string kind = Names.Of(loc.Kind);

        if (effect.Health < 0 && !a.Harmful)
        {
            a.Harmful = true;
            _s.Stats.Poisonings++;
            _s.Emit(EventCategory.COG, "Learned", loc.Label, $"Learned: {Names.Of(action)} on {kind} hurts ({effect.Health:0} health) — will avoid {kind}", loc.Pos);
        }

        if (effect.Depleted && !isNew)
        {
            loc.EmptyUntil = _s.Tick + C.EmptySourceRecheck;
            _s.Emit(EventCategory.COG, "Exhausted", loc.Label, $"{loc.Label} is used up — will check back later", loc.Pos);
            return;
        }

        if (gain > 0)
        {
            a.Successes++;
            a.Need = need;
            a.MeanGain += (gain - a.MeanGain) / a.Successes;
            if (a.Successes == 1)
            {
                _s.Emit(EventCategory.COG, "Learned", loc.Label, $"Learned: {Names.Of(action)} on {kind} → {Names.Of(need)} +{gain:0}", loc.Pos);
                if (!a.Harmful) _s.Stats.FirstRemedy.TryAdd(need, _s.Tick);
            }
        }
        else
        {
            a.Failures++;
            if (isNew)
                _s.Emit(EventCategory.COG, "Learned", loc.Label,
                    effect.Depleted
                        ? $"Learned (wrongly?): {Names.Of(action)} on {kind} does nothing — it was empty"
                        : $"Learned: {Names.Of(action)} on {kind} does nothing", loc.Pos);
        }
    }
}
