namespace EmailService.UnitTests.Consumers;

internal static class ConsumerTestWaiter
{
    public static async Task UntilAsync(Action assertion, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                assertion();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(100);
            }
        }

        try
        {
            assertion();
        }
        catch when (lastException is not null)
        {
            throw lastException;
        }
    }
}
