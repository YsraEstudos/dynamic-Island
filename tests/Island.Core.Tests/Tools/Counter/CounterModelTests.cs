using Island.Core.Tools.Counter;

namespace Island.Core.Tests.Tools.Counter;

public sealed class CounterModelTests
{
    private static CounterModel Model(int value = 0, int step = 1, int goal = 0, string label = "Contador") =>
        new(new CounterState(value, step, goal, label));

    [Fact]
    public void Increment_and_decrement_move_by_the_step()
    {
        var model = Model();

        model.Increment();
        model.Increment();
        model.Decrement();

        Assert.Equal(1, model.Value);
    }

    [Fact]
    public void Value_can_go_negative()
    {
        var model = Model();

        model.Decrement();

        Assert.Equal(-1, model.Value);
    }

    [Fact]
    public void Step_cycles_through_1_5_10_and_back()
    {
        var model = Model();

        var steps = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            model.CycleStep();
            steps.Add(model.Step);
        }

        Assert.Equal(new[] { 5, 10, 1, 5 }, steps);
    }

    [Fact]
    public void Increment_uses_the_current_step()
    {
        var model = Model();

        model.CycleStep();
        model.Increment();

        Assert.Equal(5, model.Value);
    }

    [Fact]
    public void Value_saturates_at_the_maximum_instead_of_overflowing()
    {
        var model = Model(value: CounterModel.MaxValue - 3, step: 10);
        int changes = 0;
        model.Changed += () => changes++;

        model.Increment();
        int changesAtLimit = changes;
        model.Increment();

        Assert.Equal(CounterModel.MaxValue, model.Value);
        Assert.Equal(1, changesAtLimit);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Value_saturates_at_the_minimum()
    {
        var model = Model(value: -CounterModel.MaxValue + 2, step: 10);

        model.Decrement();

        Assert.Equal(-CounterModel.MaxValue, model.Value);
        model.Decrement();
        Assert.Equal(-CounterModel.MaxValue, model.Value);
    }

    [Fact]
    public void Reset_zeroes_the_value_and_undo_restores_it()
    {
        var model = Model(value: 42);

        model.Reset();
        Assert.Equal(0, model.Value);
        Assert.True(model.CanUndo);

        model.Undo();

        Assert.Equal(42, model.Value);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void Reset_at_zero_leaves_nothing_to_undo()
    {
        var model = Model();

        model.Reset();

        Assert.False(model.CanUndo);
    }

    [Fact]
    public void Counting_after_a_reset_makes_it_permanent()
    {
        var model = Model(value: 42);
        model.Reset();

        model.Increment();
        model.Undo();

        Assert.Equal(1, model.Value);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void Undo_without_a_reset_does_nothing()
    {
        var model = Model(value: 7);

        model.Undo();

        Assert.Equal(7, model.Value);
    }

    [Fact]
    public void Changing_step_or_label_does_not_cancel_the_undo()
    {
        var model = Model(value: 9);
        model.Reset();

        model.CycleStep();
        model.CycleLabel();
        model.Undo();

        Assert.Equal(9, model.Value);
    }

    [Fact]
    public void Goal_is_reached_when_the_value_meets_it()
    {
        var model = Model(value: 9, goal: 10);
        Assert.False(model.GoalReached);

        model.Increment();

        Assert.True(model.GoalReached);
    }

    [Fact]
    public void Without_a_goal_the_goal_is_never_reached()
    {
        var model = Model(value: 500);

        Assert.False(model.GoalReached);
        Assert.Equal(0.0, model.GoalProgress);
    }

    [Fact]
    public void Goal_progress_is_the_share_of_the_goal_clamped_to_one()
    {
        Assert.Equal(0.25, Model(value: 5, goal: 20).GoalProgress);
        Assert.Equal(1.0, Model(value: 30, goal: 20).GoalProgress);
        Assert.Equal(0.0, Model(value: -4, goal: 20).GoalProgress);
    }

    [Fact]
    public void Goal_cycles_through_the_presets_and_back_to_no_goal()
    {
        var model = Model();

        var goals = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            model.CycleGoal();
            goals.Add(model.Goal);
        }

        Assert.Equal(new[] { 10, 20, 50, 100, 0, 10 }, goals);
    }

    [Fact]
    public void Cycling_a_goal_that_is_not_a_preset_moves_to_the_next_preset()
    {
        var model = Model(goal: 30);

        model.CycleGoal();

        Assert.Equal(50, model.Goal);
    }

    [Fact]
    public void Label_cycles_through_the_presets_and_wraps()
    {
        var model = Model();

        var labels = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            model.CycleLabel();
            labels.Add(model.Label);
        }

        Assert.Equal(new[] { "Água", "Repetições", "Partidas", "Café", "Pausas", "Contador" }, labels);
    }

    [Fact]
    public void Cycling_an_unknown_label_moves_to_the_first_preset()
    {
        var model = Model(label: "Bugs");

        model.CycleLabel();

        Assert.Equal("Contador", model.Label);
    }

    [Fact]
    public void Loaded_state_is_corrected_to_valid_values()
    {
        var model = new CounterModel(new CounterState(2_000_000, 3, -5, "   "));

        Assert.Equal(CounterModel.MaxValue, model.Value);
        Assert.Equal(1, model.Step);
        Assert.Equal(0, model.Goal);
        Assert.Equal("Contador", model.Label);
    }

    [Fact]
    public void Long_label_is_cut_to_the_maximum_length()
    {
        var model = new CounterModel(new CounterState(0, 1, 0, new string('x', 40)));

        Assert.Equal(CounterModel.MaxLabelLength, model.Label.Length);
    }

    [Fact]
    public void Null_state_starts_from_the_default()
    {
        var model = new CounterModel(state: null);

        Assert.Equal(CounterState.Default.Value, model.Value);
        Assert.Equal(CounterState.Default.Step, model.Step);
        Assert.Equal(CounterState.Default.Label, model.Label);
    }

    [Fact]
    public void Changed_is_raised_once_per_real_change()
    {
        var model = Model();
        int changes = 0;
        model.Changed += () => changes++;

        model.Increment();
        model.Reset();
        model.Reset();
        model.CycleGoal();

        Assert.Equal(3, changes);
    }

    [Fact]
    public void Changes_are_saved_through_the_store_when_the_model_is_disposed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "island-counter-model-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "counter.json");
        try
        {
            var model = new CounterModel(CounterState.Default, new CounterStore(path));

            model.Increment();
            model.CycleLabel();
            model.Dispose();

            CounterState saved = new CounterStore(path).Load();
            Assert.Equal(1, saved.Value);
            Assert.Equal("Água", saved.Label);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
