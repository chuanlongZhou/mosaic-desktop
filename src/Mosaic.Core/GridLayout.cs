using Mosaic.Core.Models;

namespace Mosaic.Core;

public static class GridLayout
{
    public static bool Overlaps(WidgetPlacement a, WidgetPlacement b) =>
        a.Column < b.Column + b.ColumnSpan && a.Column + a.ColumnSpan > b.Column &&
        a.Row < b.Row + b.RowSpan && a.Row + a.RowSpan > b.Row;

    public static List<WidgetPlacement> Adapt(IEnumerable<WidgetPlacement> source, int columns)
    {
        var placed = new List<WidgetPlacement>();
        foreach (var original in source.OrderBy(x => x.Row).ThenBy(x => x.Column))
        {
            var item = original.Copy();
            item.ColumnSpan = Math.Min(item.ColumnSpan, columns);
            FirstFree(item, placed, columns);
            placed.Add(item);
        }
        return placed;
    }

    public static List<WidgetPlacement> Move(IReadOnlyList<WidgetPlacement> source, string id, int column, int row, int columns)
    {
        var result = source.Select(x => x.Copy()).ToList();
        var moved = result.Single(x => x.Id == id);
        moved.Column = Math.Clamp(column, 0, columns - moved.ColumnSpan);
        moved.Row = Math.Clamp(row, 0, 80);
        var placed = new List<WidgetPlacement> { moved };
        foreach (var item in result.Where(x => x.Id != id).OrderBy(x => x.Row).ThenBy(x => x.Column))
        {
            if (placed.Any(x => Overlaps(x, item))) FirstFree(item, placed, columns);
            placed.Add(item);
        }
        return result;
    }

    private static void FirstFree(WidgetPlacement item, List<WidgetPlacement> placed, int columns)
    {
        for (var row = 0; row <= 100; row++)
        for (var column = 0; column <= columns - item.ColumnSpan; column++)
        {
            item.Row = row;
            item.Column = column;
            if (!placed.Any(x => Overlaps(x, item))) return;
        }
        throw new InvalidOperationException("网格空间不足。");
    }
}
