namespace Volvox.Sim;

public enum EventCategory { BIO, BEH, COG, ENV }

/// <summary>One structured log entry. Subject is the object or place the event is about (the "interaction").</summary>
public sealed class SimEvent
{
    public long Id { get; init; }
    public long Tick { get; init; }
    public long Age { get; init; }
    public EventCategory Category { get; init; }
    public string Type { get; init; } = "";
    public int OrganismId { get; init; }
    public string Subject { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public string Text { get; init; } = "";

    public override string ToString() =>
        $"T+{Names.Time(Tick)}  {Category}  {Type,-10} {Subject,-16} {Text}";
}

/// <summary>Bounded in-memory event history. Oldest entries are dropped in chunks past capacity.</summary>
public sealed class EventLog
{
    private readonly List<SimEvent> _events = new();
    private long _nextId = 1;

    public bool Enabled { get; set; } = true;
    public int Capacity { get; set; } = 200_000;
    public long Dropped { get; private set; }
    public IReadOnlyList<SimEvent> Events => _events;
    public long LastId => _nextId - 1;

    public void Add(long tick, long age, EventCategory cat, string type, int organismId, string subject, V2 pos, string text)
    {
        if (!Enabled) return;
        _events.Add(new SimEvent
        {
            Id = _nextId++, Tick = tick, Age = age, Category = cat, Type = type,
            OrganismId = organismId, Subject = subject, X = pos.X, Y = pos.Y, Text = text,
        });
        if (_events.Count > Capacity)
        {
            int drop = Math.Max(1, Capacity / 10);
            _events.RemoveRange(0, drop);
            Dropped += drop;
        }
    }

    /// <summary>Events with Id greater than the given one, oldest first.</summary>
    public IEnumerable<SimEvent> Since(long id)
    {
        int i = _events.Count;
        while (i > 0 && _events[i - 1].Id > id) i--;
        for (; i < _events.Count; i++) yield return _events[i];
    }
}
