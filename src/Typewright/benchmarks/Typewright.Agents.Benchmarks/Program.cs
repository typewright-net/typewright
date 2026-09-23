using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Typewright.Agents.Benchmarks.AgentBenchmarks).Assembly).Run(args);
