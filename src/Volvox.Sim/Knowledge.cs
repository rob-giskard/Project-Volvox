namespace Volvox.Sim;

/// <summary>"Doing this action to this kind of thing did that to me."</summary>
public sealed class Association
{
    public ObjectKind Kind { get; init; }
    public ActionKind Action { get; init; }
    public Need? Need { get; set; }
    public double MeanGain { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
    public bool Harmful { get; set; }
    public long LearnedTick { get; init; }

    /// <summary>Rises with repeated success, falls with failure. One success ≈ 0.4.</summary>
    public double Confidence => Successes / (Successes + Failures + 1.5);
    public bool Useful => Successes > 0 && !Harmful && Need != null;
}

/// <summary>"There is a thing of this kind over there." Can go stale.</summary>
public sealed class LocationMemory
{
    public int ObjectId { get; init; }
    public ObjectKind Kind { get; init; }
    public V2 Pos { get; init; }
    public long FirstSeen { get; init; }
    public long LastConfirmed { get; set; }
    public double Confidence { get; set; } = 1;
    public long EmptyUntil { get; set; }
    public string Label => $"{Names.Of(Kind)} #{ObjectId}";
}

/// <summary>
/// Coarse grid of how costly it felt to move through each cell, plus which cells have been seen.
/// Unknown cells are assumed to cost like flat open ground.
/// </summary>
public sealed class EffortGrid
{
    public int CellSize { get; }
    public int N { get; }
    public double Guess { get; }
    public double[] Cost { get; }
    public int[] Samples { get; }
    public bool[] Seen { get; }
    public int[] Visits { get; }
    private readonly double _rate;
    private double _minKnown;

    public EffortGrid(int worldSize, int cellSize, double guess, double learningRate)
    {
        CellSize = Math.Max(2, cellSize);
        N = (worldSize + CellSize - 1) / CellSize;
        Guess = guess;
        _rate = learningRate;
        _minKnown = guess;
        Cost = new double[N * N];
        Samples = new int[N * N];
        Seen = new bool[N * N];
        Visits = new int[N * N];
    }

    public int Index(V2 p)
    {
        int x = Math.Clamp((int)(p.X / CellSize), 0, N - 1);
        int y = Math.Clamp((int)(p.Y / CellSize), 0, N - 1);
        return y * N + x;
    }

    public V2 Center(int i) => new((i % N + 0.5) * CellSize, (i / N + 0.5) * CellSize);

    public double CostAt(int i) => Samples[i] > 0 ? Cost[i] : Guess;

    public void Record(V2 p, double costPerUnit)
    {
        costPerUnit = Math.Max(0.02, costPerUnit);
        int i = Index(p);
        if (Samples[i] == 0) Cost[i] = costPerUnit;
        else Cost[i] += _rate * (costPerUnit - Cost[i]);
        Samples[i]++;
        if (Cost[i] < _minKnown) _minKnown = Cost[i];
    }

    public void MarkSeen(V2 p, double radius)
    {
        int r = (int)Math.Ceiling(radius / CellSize);
        int cx = Math.Clamp((int)(p.X / CellSize), 0, N - 1), cy = Math.Clamp((int)(p.Y / CellSize), 0, N - 1);
        double r2 = radius * radius;
        for (int y = Math.Max(0, cy - r); y <= Math.Min(N - 1, cy + r); y++)
        for (int x = Math.Max(0, cx - r); x <= Math.Min(N - 1, cx + r); x++)
        {
            int i = y * N + x;
            if (!Seen[i] && V2.DistSq(Center(i), p) <= r2) Seen[i] = true;
        }
    }

    public double UnseenFraction(V2 p, double radius)
    {
        int r = (int)Math.Ceiling(radius / CellSize);
        int cx = Math.Clamp((int)(p.X / CellSize), 0, N - 1), cy = Math.Clamp((int)(p.Y / CellSize), 0, N - 1);
        int total = 0, unseen = 0;
        for (int y = Math.Max(0, cy - r); y <= Math.Min(N - 1, cy + r); y++)
        for (int x = Math.Max(0, cx - r); x <= Math.Min(N - 1, cx + r); x++)
        {
            total++;
            if (!Seen[y * N + x]) unseen++;
        }
        return total == 0 ? 0 : (double)unseen / total;
    }

    /// <summary>Effort of walking a straight line, estimated from remembered costs.</summary>
    public double LineCost(V2 a, V2 b)
    {
        double len = V2.Dist(a, b);
        if (len < 1e-6) return 0;
        int steps = Math.Max(1, (int)Math.Ceiling(len / (CellSize * 0.5)));
        double seg = len / steps, sum = 0;
        for (int k = 0; k < steps; k++)
        {
            var p = a + (b - a) * ((k + 0.5) / steps);
            sum += CostAt(Index(p)) * seg;
        }
        return sum;
    }

    public double PathCost(V2 start, IReadOnlyList<V2> path)
    {
        double sum = 0;
        var prev = start;
        foreach (var p in path) { sum += LineCost(prev, p); prev = p; }
        return sum;
    }

    /// <summary>A* over the coarse grid using remembered costs. Returns waypoints ending exactly at goal.</summary>
    public List<V2> FindPath(V2 from, V2 to)
    {
        int s = Index(from), g = Index(to);
        var result = new List<V2>();
        if (s == g) { result.Add(to); return result; }

        int count = N * N;
        var gScore = new double[count];
        var came = new int[count];
        var closed = new bool[count];
        Array.Fill(gScore, double.PositiveInfinity);
        Array.Fill(came, -1);
        double hMin = Math.Max(0.01, Math.Min(_minKnown, Guess) * 0.9);
        var goalCenter = Center(g);

        var open = new PriorityQueue<int, double>();
        gScore[s] = 0;
        open.Enqueue(s, V2.Dist(Center(s), goalCenter) * hMin);

        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == g) break;
            if (closed[cur]) continue;
            closed[cur] = true;
            int cx = cur % N, cy = cur / N;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                int ni = ny * N + nx;
                if (closed[ni]) continue;
                double step = (dx != 0 && dy != 0 ? 1.41421356 : 1.0) * CellSize;
                double tentative = gScore[cur] + step * 0.5 * (CostAt(cur) + CostAt(ni));
                if (tentative < gScore[ni])
                {
                    gScore[ni] = tentative;
                    came[ni] = cur;
                    open.Enqueue(ni, tentative + V2.Dist(Center(ni), goalCenter) * hMin);
                }
            }
        }

        if (came[g] == -1) { result.Add(to); return result; }
        var cells = new List<int>();
        for (int c = g; c != s && c != -1; c = came[c]) cells.Add(c);
        cells.Reverse();
        for (int k = 0; k < cells.Count - 1; k++) result.Add(Center(cells[k]));
        result.Add(to);
        return result;
    }
}
