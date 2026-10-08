using Island.Core.Models;
using Island.Windows.Input;
using System.Collections.Concurrent;

namespace Island.Windows.Tests;

public sealed class WindowsCapsLockNoticeSourceTests
{
    [Fact]
    public void StateMachine_IgnoresRepeats_AndTogglesOnKeydownEdges()
    {
        var state = new CapsLockStateMachine(initiallyOn: false);

        var firstTransition = state.Process(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));
        Assert.Equal(true, firstTransition?.IsOn);
        Assert.Null(state.Process(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false)));
        Assert.Null(state.Process(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: true, IsInjected: false)));

        var transition = state.Process(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));

        Assert.Equal(false, transition?.IsOn);
        Assert.False(state.IsOn);
    }

    [Fact]
    public void StateMachine_ReconciliationUpdatesBaselineWithoutEmitting()
    {
        var state = new CapsLockStateMachine(initiallyOn: false);

        state.Reconcile(actualIsOn: true);
        Assert.True(state.IsOn);
        state.Reconcile(actualIsOn: true);
        Assert.True(state.IsOn);
    }

    [Fact]
    public void Source_StartIsIdempotent_AndInitialStateIsSilent()
    {
        var hook = new FakeKeyboardHook();
        using var source = new WindowsCapsLockNoticeSource(hook, () => true);
        var notices = new ConcurrentQueue<Notice>();
        source.NoticeRaised += (_, notice) => notices.Enqueue(notice);

        source.Start();
        source.Start();
        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));

        Assert.True(hook.StartCount == 1);
        Assert.True(SpinWait.SpinUntil(() => notices.Count == 1, TimeSpan.FromSeconds(1)));
        var notice = Assert.Single(notices);
        Assert.Equal("Caps Lock desativado", notice.Title);
        Assert.Equal("Letras minúsculas", notice.Subtitle);
        Assert.Equal("caps-off", notice.Glyph);
        Assert.False(notice.Urgent);
    }

    [Fact]
    public void Source_StaleReaderAfterSeedStillAlternatesInjectedAndPhysicalEdges_AndDisposesIdempotently()
    {
        var hook = new FakeKeyboardHook();
        int reads = 0;
        using var source = new WindowsCapsLockNoticeSource(hook, () =>
        {
            reads++;
            return false;
        });
        var notices = new ConcurrentQueue<Notice>();
        source.NoticeRaised += (_, notice) => notices.Enqueue(notice);
        source.Start();

        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: true));
        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: true, IsInjected: true));
        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));

        Assert.True(SpinWait.SpinUntil(() => notices.Count == 2, TimeSpan.FromSeconds(1)));
        Assert.Equal(new[] { "Caps Lock ativado", "Caps Lock desativado" }, notices.Select(n => n.Title));
        Assert.Equal(1, reads);

        source.Dispose();
        source.Dispose();
        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: true, IsInjected: false));
        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));

        Assert.Equal(2, notices.Count);
        Assert.Equal(1, hook.DisposeCount);
    }

    [Fact]
    public void Source_SubscriberFailureDoesNotEscapeHookCallback()
    {
        var hook = new FakeKeyboardHook();
        using var source = new WindowsCapsLockNoticeSource(hook, () => false);
        using var firstSubscriberRan = new ManualResetEventSlim();
        using var secondSubscriberRan = new ManualResetEventSlim();
        var notices = new ConcurrentQueue<Notice>();
        int calls = 0;
        source.NoticeRaised += (_, notice) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstSubscriberRan.Set();
                throw new InvalidOperationException("subscriber failure");
            }

            notices.Enqueue(notice);
            secondSubscriberRan.Set();
        };
        source.Start();

        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));
        Assert.True(firstSubscriberRan.Wait(TimeSpan.FromSeconds(1)));

        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: true, IsInjected: false));
        hook.Emit(new KeyboardInputEvent(CapsLockConstants.VkCapital, IsKeyUp: false, IsInjected: false));

        Assert.True(secondSubscriberRan.Wait(TimeSpan.FromSeconds(1)));
        var notice = Assert.Single(notices);
        Assert.Equal("Caps Lock desativado", notice.Title);
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        private Action<KeyboardInputEvent>? _callback;

        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool Start(Action<KeyboardInputEvent> callback, Func<bool> readInitialState, Action<bool> applyInitialState)
        {
            StartCount++;
            _callback = callback;
            applyInitialState(readInitialState());
            return true;
        }

        public void Emit(KeyboardInputEvent input)
        {
            _callback?.Invoke(input);
        }

        public void Dispose()
        {
            DisposeCount++;
            _callback = null;
        }
    }

    [Fact]
    public void RealHook_StartAndDispose_IsSafeWhenDesktopHookIsUnavailable()
    {
        using var source = new WindowsCapsLockNoticeSource();

        var exception = Record.Exception(() =>
        {
            source.Start();
            source.Dispose();
        });

        Assert.Null(exception);
    }
}
