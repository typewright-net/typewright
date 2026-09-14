```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core i9-14900K 3.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                               | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------------- |----------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| ValidDocument                        | 160.47 ns |  7.793 ns | 0.427 ns |  1.00 |    0.00 | 0.0815 |      - |    1536 B |        1.00 |
| InvalidDocument_AccumulatesAllErrors | 328.43 ns | 91.619 ns | 5.022 ns |  2.05 |    0.03 | 0.1402 | 0.0005 |    2640 B |        1.72 |
| SingleNormalizedValue                |  16.89 ns |  4.173 ns | 0.229 ns |  0.11 |    0.00 | 0.0076 |      - |     144 B |        0.09 |
