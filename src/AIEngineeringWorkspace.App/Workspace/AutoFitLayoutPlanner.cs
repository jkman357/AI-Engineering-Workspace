namespace AIEngineeringWorkspace.Workspace;

internal readonly record struct AutoFitPaneSpec(double MinWidth, double MinHeight);
internal readonly record struct AutoFitCell(double X, double Y, double Width, double Height);
internal sealed record AutoFitLayoutPlan(
    double CanvasWidth,
    double CanvasHeight,
    int Columns,
    int Rows,
    bool RequiresScrolling,
    IReadOnlyList<AutoFitCell> Cells);

internal static class AutoFitLayoutPlanner
{
    internal static AutoFitLayoutPlan Plan(
        double viewportWidth,
        double viewportHeight,
        IReadOnlyList<AutoFitPaneSpec> panes,
        double gap,
        int maxColumns = 4)
    {
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        gap = Math.Max(0, gap);

        if (panes.Count == 0)
        {
            return new AutoFitLayoutPlan(viewportWidth, viewportHeight, 0, 0, false, Array.Empty<AutoFitCell>());
        }

        if (panes.Count == 1)
        {
            var width = Math.Max(panes[0].MinWidth, viewportWidth);
            var height = Math.Max(panes[0].MinHeight, viewportHeight);
            return new AutoFitLayoutPlan(width, height, 1, 1, width > viewportWidth || height > viewportHeight,
                new[] { new AutoFitCell(0, 0, width, height) });
        }

        var maxMinWidth = panes.Max(p => Math.Max(1, p.MinWidth));
        var maxMinHeight = panes.Max(p => Math.Max(1, p.MinHeight));
        var aspect = viewportWidth / viewportHeight;
        Candidate? best = null;

        for (var columns = 1; columns <= Math.Min(Math.Max(1, maxColumns), panes.Count); columns++)
        {
            var rows = (int)Math.Ceiling(panes.Count / (double)columns);
            var horizontalGaps = gap * (columns + 1);
            var verticalGaps = gap * (rows + 1);
            var idealCellWidth = Math.Max(1, (viewportWidth - horizontalGaps) / columns);
            var idealCellHeight = Math.Max(1, (viewportHeight - verticalGaps) / rows);
            var cellWidth = Math.Max(maxMinWidth, idealCellWidth);
            var cellHeight = Math.Max(maxMinHeight, idealCellHeight);
            var canvasWidth = Math.Max(viewportWidth, horizontalGaps + (cellWidth * columns));
            var canvasHeight = Math.Max(viewportHeight, verticalGaps + (cellHeight * rows));
            var overflowX = Math.Max(0, canvasWidth - viewportWidth) / viewportWidth;
            var overflowY = Math.Max(0, canvasHeight - viewportHeight) / viewportHeight;
            var overflowScore = overflowX + overflowY;
            var gridAspect = columns / (double)rows;
            var shapePenalty = Math.Abs(Math.Log(Math.Max(0.0001, gridAspect / aspect)));
            var candidate = new Candidate(columns, rows, cellWidth, cellHeight, canvasWidth, canvasHeight, overflowScore, shapePenalty);

            if (best is null || candidate.CompareTo(best.Value) < 0)
            {
                best = candidate;
            }
        }

        var selected = best!.Value;
        var cells = new List<AutoFitCell>(panes.Count);
        for (var index = 0; index < panes.Count; index++)
        {
            var row = index / selected.Columns;
            var column = index % selected.Columns;
            var x = gap + (column * (selected.CellWidth + gap));
            var y = gap + (row * (selected.CellHeight + gap));
            cells.Add(new AutoFitCell(x, y, selected.CellWidth, selected.CellHeight));
        }

        var requiresScrolling = selected.CanvasWidth > viewportWidth + 0.5 || selected.CanvasHeight > viewportHeight + 0.5;
        return new AutoFitLayoutPlan(selected.CanvasWidth, selected.CanvasHeight, selected.Columns, selected.Rows, requiresScrolling, cells);
    }

    private readonly record struct Candidate(
        int Columns,
        int Rows,
        double CellWidth,
        double CellHeight,
        double CanvasWidth,
        double CanvasHeight,
        double OverflowScore,
        double ShapePenalty)
    {
        internal int CompareTo(Candidate other)
        {
            var overflowCompare = OverflowScore.CompareTo(other.OverflowScore);
            if (overflowCompare != 0)
            {
                return overflowCompare;
            }

            var shapeCompare = ShapePenalty.CompareTo(other.ShapePenalty);
            if (shapeCompare != 0)
            {
                return shapeCompare;
            }

            return Rows.CompareTo(other.Rows);
        }
    }
}
