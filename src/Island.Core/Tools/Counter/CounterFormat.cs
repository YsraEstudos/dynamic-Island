using System.Globalization;

namespace Island.Core.Tools.Counter;

/// <summary>Text the counter shows, kept out of WPF so the wording can be tested.</summary>
public static class CounterFormat
{
    /// <summary>The number itself, as plain digits so it never depends on the culture.</summary>
    public static string ValueText(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The step button label, such as "+5".</summary>
    public static string StepText(int step) => "+" + step.ToString(CultureInfo.InvariantCulture);

    /// <summary>The goal shown after the number, such as "/ 20". Empty when there is no goal.</summary>
    public static string GoalSuffix(int goal) =>
        goal > 0 ? "/ " + goal.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
