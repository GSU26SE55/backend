using Xunit;

// MassTransit saga and consumer tests create and start an in-memory bus per
// test. Running those harnesses concurrently can starve the test-host thread
// pool on a loaded CI executor, causing Consumed.Any<T>() to hit its inactivity
// timeout even though the consumer is correctly registered. Keep this test
// assembly deterministic; other test projects still run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
