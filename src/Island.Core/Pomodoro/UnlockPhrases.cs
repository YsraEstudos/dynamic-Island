using System.Globalization;
using System.Text;

namespace Island.Core.Pomodoro;

/// <summary>
/// Phrases the user must type to end an angry focus session early. Accents are ignored so a US keyboard can always
/// type a phrase, and a whole phrase arriving in one step (a paste) is rejected by <see cref="IsPlausibleKeystroke"/>.
/// </summary>
public static class UnlockPhrases
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "Eu estou escolhendo desistir do meu foco agora, mesmo sabendo que depois vou me arrepender de ter jogado fora esse tempo na internet.",
        "Eu sei muito bem que este bloqueio existe para me proteger de mim mesmo, e mesmo assim estou procurando uma brecha para fugir do que combinei comigo.",
        "Ninguém me obrigou a começar este ciclo. Eu mesmo escolhi, e agora estou repetindo a mesma promessa vazia que sempre faço quando a vontade de parar aparece.",
        "Cada minuto que eu largo agora vira um pedaço a menos do trabalho que eu queria ter entregado hoje. Eu vejo isso com clareza e, ainda assim, empurro a tela para o lado.",
        "Eu poderia ficar quieto e terminar o que comecei, mas prefiro a sensação rápida de rolar a tela sem parar. Amanhã vou lembrar desta escolha e não vou gostar nem um pouco dela.",
        "Estou quebrando uma promessa que eu mesmo fiz quando sentei aqui. Quem vai pagar o preço não é a internet, sou eu, e estou aceitando isso sem nem tentar aguentar um pouco mais.",
        "Eu conheço bem esse padrão: começo firme, sinto a vontade de fugir e logo me convenço de que mereço uma pausa falsa. Desta vez eu escolho a vergonha de admitir que desisti.",
        "Não existe motivo bom para abandonar o foco neste momento, só a preguiça vestida de cansaço. Eu sei disso e ainda assim vou ceder, então que eu carregue a lembrança deste clique.",
    };

    public static string Pick(Random? rng = null) => All[(rng ?? Random.Shared).Next(All.Count)];

    /// <summary>True when <paramref name="typed"/> equals <paramref name="expected"/> ignoring accents. Case and spacing must match exactly.</summary>
    public static bool IsMatch(string expected, string? typed) =>
        typed is not null && string.Equals(Fold(expected), Fold(typed), StringComparison.Ordinal);

    /// <summary>Length of the common prefix under the same comparison as <see cref="IsMatch"/>; 0 when nothing was typed.</summary>
    public static int CommonPrefixLength(string expected, string? typed)
    {
        if (typed is null) return 0;

        var a = Fold(expected);
        var b = Fold(typed);
        var limit = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }

    /// <summary>False when the input grew by more than one character in one step, which means a paste or an automation tool.</summary>
    public static bool IsPlausibleKeystroke(string before, string after) => after.Length - before.Length <= 1;

    // Decomposes accented letters and drops the combining marks, so "ã" compares as "a" on both sides.
    private static string Fold(string text)
    {
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // Malformed UTF-16 cannot match any phrase; compare it as typed.
            decomposed = text;
        }

        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }

        return builder.ToString();
    }
}
