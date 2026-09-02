using Xunit;

// These tests build real hosts through IsoAuditWebApplicationFactory, and the
// startup-validation cases assert on the exception a failing host throws.
// WebApplicationFactory runs such an app on a deferred host: when startup fails,
// the service provider is disposed while DeferredHost.StartAsync is still reading
// services out of it. Under parallel load that disposal wins the race and
// ObjectDisposedException surfaces in place of the OptionsValidationException the
// test is asserting, so the security assertions fail for a reason unrelated to
// the behaviour they cover.
//
// Serialising the assembly removes the contention that loses that race. The
// suite runs in a few seconds, so there is nothing to gain from parallelism here.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
