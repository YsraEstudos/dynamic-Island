using System.Globalization;

namespace Island.Core.Budgets;

public static class PercentFormatter
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static string Format(double value)
    {
        double shown = value == 0 ? 0 : value; // evita exibir "-0"
        return shown.ToString("0.#", PtBr) + "%";
    }

    /// <summary>
    /// Aceita "18,5", "18.5", "18,5%" e espaços. Rejeita vazio, NaN, infinito e negativo.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        string? normalized = Normalize(text);
        if (normalized is null) return false;
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return false;
        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0) return false;
        value = parsed;
        return true;
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (trimmed.EndsWith('%')) trimmed = trimmed[..^1].Trim();
        if (trimmed.Length == 0) return null;
        // Com vírgula, o ponto é separador de milhar (pt-BR) e a vírgula é o decimal.
        return trimmed.Contains(',') ? trimmed.Replace(".", "").Replace(',', '.') : trimmed;
    }
}
