using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Typewright.Validation.Benchmarks.ValidationBenchmarks).Assembly)
    .Run(args);
