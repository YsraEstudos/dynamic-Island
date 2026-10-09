using Island.Core.GameNotes;

namespace Island.Core.Fakes;

/// <summary>Sample games for --demo. The game in front at start is <see cref="InitialGame"/>.</summary>
public static class DemoGameNotes
{
    public static GameInfo InitialGame { get; } = new("celeste", "Celeste", null);

    public static IReadOnlyList<GameNotesGame> Seed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return
        [
            new GameNotesGame(
                "celeste", "celeste", "Celeste", null, now,
                [
                    new GameNote(Guid.NewGuid(), "Pegar a fita do capítulo 3", now.AddMinutes(-5), true, false),
                    new GameNote(Guid.NewGuid(), "Lembrar do truque do dash na água", now.AddMinutes(-20), false, true),
                ]),
            new GameNotesGame(
                "eldenring", "eldenring", "ELDEN RING", null, now.AddHours(-3),
                [
                    new GameNote(Guid.NewGuid(), "Voltar ao Altar da Graça e subir o nível de Vigor", now.AddHours(-3), false, false),
                ]),
        ];
    }
}
