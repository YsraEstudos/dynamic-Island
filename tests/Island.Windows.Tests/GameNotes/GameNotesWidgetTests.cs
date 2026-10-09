using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.GameNotes;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class GameNotesWidgetTests
{
    private static readonly GameInfo Celeste = new("celeste", "Celeste", null);

    [Fact]
    public void Shows_the_game_on_screen_with_its_notes()
    {
        WpfStaTestHost.Run(_ =>
        {
            GameNotesWidget widget = CreateWidget(CreateService(Celeste, Games("celeste", 2)));

            Invoke(widget, "Render");

            Assert.Equal("Celeste", Assert.IsType<TextBlock>(widget.FindName("GameNameText")).Text);
            Assert.Equal("2 notas", Assert.IsType<TextBlock>(widget.FindName("CountText")).Text);
            Assert.Equal(2, Assert.IsType<StackPanel>(widget.FindName("NotesList")).Children.Count);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(widget.FindName("EmptyText")).Visibility);
        });
    }

    [Fact]
    public void Without_a_game_it_shows_the_empty_state()
    {
        WpfStaTestHost.Run(_ =>
        {
            GameNotesWidget widget = CreateWidget(CreateService(null, Array.Empty<GameNotesGame>()));

            Invoke(widget, "Render");

            Assert.Equal("Nenhum jogo", Assert.IsType<TextBlock>(widget.FindName("GameNameText")).Text);
            TextBlock empty = Assert.IsType<TextBlock>(widget.FindName("EmptyText"));
            Assert.Equal(Visibility.Visible, empty.Visibility);
            Assert.Equal("Abra um jogo para anotar nele", empty.Text);
        });
    }

    [Fact]
    public void Four_notes_show_two_rows_and_an_overflow_line()
    {
        WpfStaTestHost.Run(_ =>
        {
            GameNotesWidget widget = CreateWidget(CreateService(Celeste, Games("celeste", 4)));

            Invoke(widget, "Render");

            StackPanel list = Assert.IsType<StackPanel>(widget.FindName("NotesList"));
            Assert.Equal(3, list.Children.Count);
            Assert.Equal("+2 notas", Assert.IsType<TextBlock>(list.Children[2]).Text);
        });
    }

    private static GameNotesService CreateService(GameInfo? game, IReadOnlyList<GameNotesGame> games)
    {
        var service = new GameNotesService(new InMemoryGameNotesStore(games), new FakeForegroundGameTracker(game));
        service.InitializeAsync().GetAwaiter().GetResult();
        return service;
    }

    private static GameNotesWidget CreateWidget(GameNotesService service)
    {
        var settings = new IslandSettings { ReduceAnimations = true };
        var context = new ShelfContext(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            () => settings, _ => { }, gameNotes: service);
        return new GameNotesWidget(context);
    }

    private static IReadOnlyList<GameNotesGame> Games(string key, int count)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var notes = Enumerable.Range(0, count)
            .Select(i => new GameNote(Guid.NewGuid(), "nota " + i, now.AddMinutes(i), false, false))
            .ToArray();
        return new[] { new GameNotesGame(key, key, "Celeste", null, now, notes) };
    }

    private static void Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .Invoke(target, arguments);
}
