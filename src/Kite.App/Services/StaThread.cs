namespace Kite.App.Services;

/// <summary>
/// runs work on a special kind of thread that some windows shell
/// objects need to work correctly.
/// </summary>
internal static class StaThread
{
    public static T Run<T>(Func<T> work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        T? result = default;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Kite shell worker",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // wait in small steps so a cancel is noticed quickly
        var waited = TimeSpan.Zero;
        var step = TimeSpan.FromMilliseconds(100);
        while (!thread.Join(step))
        {
            cancellationToken.ThrowIfCancellationRequested();
            waited += step;
            if (waited >= timeout)
            {
                throw new TimeoutException("The windows shell took too long to answer.");
            }
        }

        if (error is not null)
        {
            throw new InvalidOperationException(error.Message, error);
        }

        return result!;
    }
}
