namespace Island.Core.Audio;

/// <summary>Offset arithmetic for the mixer list, which shows a few rows at a time (arrows, wheel or click).</summary>
public static class AudioListPaging
{
    public const int RowsPerPage = 3;

    public static int MaxOffset(int count, int pageSize = RowsPerPage) => Math.Max(0, count - Math.Max(1, pageSize));

    /// <summary>Keeps the offset inside the list. Use it after the list changes size.</summary>
    public static int Clamp(int offset, int count, int pageSize = RowsPerPage) =>
        Math.Clamp(offset, 0, MaxOffset(count, pageSize));

    public static int Step(int offset, int delta, int count, int pageSize = RowsPerPage) =>
        Clamp(offset + delta, count, pageSize);

    /// <summary>"1–3 de 5" for the rows on screen, or an empty string when everything fits on one page.</summary>
    public static string Label(int offset, int count, int pageSize = RowsPerPage)
    {
        if (count <= pageSize) return string.Empty;

        int first = Clamp(offset, count, pageSize) + 1;
        int last = Math.Min(count, first + pageSize - 1);
        return $"{first}–{last} de {count}";
    }
}
