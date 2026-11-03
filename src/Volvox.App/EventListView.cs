using Volvox.Sim;

namespace Volvox.App;

/// <summary>Virtual, owner-drawn event list. Handles hundreds of thousands of rows.</summary>
internal sealed class EventListView : ListView
{
    public const int ColLifetime = 0, ColCategory = 1, ColEvent = 2, ColInteraction = 3, ColDescription = 4, ColPos = 5;

    public List<SimEvent> Rows { get; private set; } = new();
    public int SortColumn { get; set; } = -1;
    public bool SortDescending { get; set; }

    public EventListView()
    {
        View = View.Details;
        VirtualMode = true;
        OwnerDraw = true;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = false;
        HeaderStyle = ColumnHeaderStyle.Clickable;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Paper;
        ForeColor = Theme.Ink;
        Font = Theme.Mono;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;

        Columns.Add("T+ LIFETIME", 104);
        Columns.Add("CAT", 44);
        Columns.Add("EVENT", 92);
        Columns.Add("INTERACTION", 150);
        Columns.Add("DESCRIPTION", 620);
        Columns.Add("POS", 84);
    }

    public void SetRows(List<SimEvent> rows)
    {
        Rows = rows;
        VirtualListSize = rows.Count;
        Invalidate();
    }

    public void SyncSize()
    {
        if (VirtualListSize != Rows.Count) VirtualListSize = Rows.Count;
    }

    public SimEvent? Selected =>
        SelectedIndices.Count > 0 && SelectedIndices[0] < Rows.Count ? Rows[SelectedIndices[0]] : null;

    protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= Rows.Count)
        {
            e.Item = new ListViewItem(new[] { "", "", "", "", "", "" });
            return;
        }
        var ev = Rows[e.ItemIndex];
        e.Item = new ListViewItem(new[]
        {
            Names.Time(ev.Age),
            ev.Category.ToString(),
            ev.Type,
            ev.Subject,
            ev.Text,
            $"{ev.X:0},{ev.Y:0}",
        });
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var back = new SolidBrush(Theme.Panel);
        using var line = new Pen(Theme.Line, 1f);
        e.Graphics.FillRectangle(back, e.Bounds);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
        string text = e.Header?.Text ?? "";
        if (e.ColumnIndex == SortColumn) text += SortDescending ? "  v" : "  ^";
        var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Theme.MonoBold, r, Theme.InkSoft,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        // Rows are painted per sub-item.
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        int idx = e.ItemIndex;
        bool selected = SelectedIndices.Contains(idx);
        var bg = selected ? Theme.Select : idx % 2 == 0 ? Theme.Paper : Theme.PaperAlt;
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

        Color fg = Theme.Ink;
        Font font = Theme.Mono;
        if (idx >= 0 && idx < Rows.Count)
        {
            var ev = Rows[idx];
            if (e.ColumnIndex == ColCategory) { fg = Theme.Category(ev.Category); font = Theme.MonoBold; }
            else if (e.ColumnIndex == ColLifetime || e.ColumnIndex == ColPos) fg = Theme.InkSoft;
            else if (e.ColumnIndex == ColInteraction) fg = Theme.Belief;
            else if (ev.Type is "Died" or "Learned" or "Route") font = Theme.MonoBold;
        }

        var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", font, r, fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
