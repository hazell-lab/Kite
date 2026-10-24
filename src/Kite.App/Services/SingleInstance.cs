using Kite.App.Interop;

namespace Kite.App.Services;

/// <summary>
/// makes sure only one copy of kite runs. a second copy asks the first one
/// to show the launcher and then exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Kite.Launcher.Instance";
    private const string ShowEventName = @"Local\Kite.Launcher.Show";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _waitRegistration;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
    }

    public bool IsFirstInstance => _ownsMutex;

    /// <summary>ask the running copy to show itself.</summary>
    public static void SignalFirstInstance()
    {
        try
        {
            // let the running copy bring its window to the front
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // the other copy is still starting or already closing
        }
        catch (UnauthorizedAccessException)
        {
            // the other copy belongs to a different user
        }
    }

    /// <summary>call the action whenever another copy asks to show the launcher.</summary>
    public void ListenForShowRequests(Action onShow)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => onShow(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _waitRegistration?.Unregister(null);
        _showEvent?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // the mutex is freed anyway when the process ends
            }
        }

        _mutex.Dispose();
    }
}
