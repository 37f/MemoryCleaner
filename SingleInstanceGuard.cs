namespace MemoryCleaner;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    internal SingleInstanceGuard(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        IsPrimaryInstance = createdNew;
    }

    internal bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 仅在互斥体已被异常释放时发生，退出流程无需再次处理。
            }
        }

        _mutex.Dispose();
    }
}
