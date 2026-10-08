namespace Island.Core.Configuration;

/// <summary>
/// Converts between the saved shelf (a flat id list plus a row index per id) and rows. A missing row index means
/// row 0, so settings written before rows existed load as one row.
/// </summary>
public static class ShelfLayout
{
    /// <summary>Rows in index order, each in list order. An id appears once (its first occurrence wins); empty rows are dropped.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ToRows(IReadOnlyList<string> ids, IReadOnlyList<int> rowIndexes)
    {
        var byRow = new SortedDictionary<int, List<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Count; i++)
        {
            if (!seen.Add(ids[i])) continue;

            int row = i < rowIndexes.Count ? Math.Max(0, rowIndexes[i]) : 0;
            if (!byRow.TryGetValue(row, out List<string>? list)) byRow[row] = list = new List<string>();
            list.Add(ids[i]);
        }
        return byRow.Values.Select(list => (IReadOnlyList<string>)list).ToArray();
    }

    /// <summary>A remembered row index while that row still exists; otherwise the first row (0), e.g. after the layout was edited.</summary>
    public static int ValidPage(int page, int rowCount) => page >= 0 && page < rowCount ? page : 0;

    /// <summary>The saved form of rows: ids in row order and the row index of each. Empty rows vanish; indexes are renumbered without gaps.</summary>
    public static (IReadOnlyList<string> Ids, IReadOnlyList<int> Rows) FromRows(IEnumerable<IEnumerable<string>> rows)
    {
        var ids = new List<string>();
        var indexes = new List<int>();
        int index = 0;
        foreach (IEnumerable<string> row in rows)
        {
            bool any = false;
            foreach (string id in row)
            {
                ids.Add(id);
                indexes.Add(index);
                any = true;
            }
            if (any) index++;
        }
        return (ids, indexes);
    }
}
