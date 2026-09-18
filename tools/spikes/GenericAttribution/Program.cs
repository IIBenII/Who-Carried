using GenericAttribution;

if (args.Length >= 2 && args[0] == "--inspect") return InspectAssemblies.Run(args[1], args.Skip(2).ToArray());
if (!args.Contains("--baseline")) AttributionProbe.Install();
int passed = 0, failed = 0;
foreach (var test in Probe.Cases())
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); passed++; }
    catch (Exception e) { Console.WriteLine($"FAIL {test.Name}: {e.Message}"); failed++; }
}
Console.WriteLine($"{passed}/{passed + failed} spike checks passed; mode={(args.Contains("--baseline") ? "baseline" : "instrumented")}");
return failed == 0 ? 0 : 1;
