using Xunit;

// MassTransit consumer tests create and start an in-memory bus per test. Running
// those tests concurrently can starve the test-host thread pool on a loaded CI
// executor, causing Consumed.Any<T>() to hit its inactivity timeout even though
// the consumer is correctly registered. Keep this test assembly deterministic;
// other test projects can still run in parallel at the solution level.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
