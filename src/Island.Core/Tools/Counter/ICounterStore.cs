namespace Island.Core.Tools.Counter;

public interface ICounterStore : IDisposable
{
    CounterState Load();
    void Save(CounterState state);
    void Flush();
}
