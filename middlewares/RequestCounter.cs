namespace middlewares;

public class RequestCounter
{
    private int _counter= 0;

    public int Increment()
    {
        return Interlocked.Increment(ref _counter);
    }

    public int GetCounter()
    {
        return Volatile.Read(ref _counter);
    }
}