// Throwaway feasibility probe. No reference from the shipping mod or solution.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GenericAttribution;

internal abstract class Effect
{
    public string Name = "";
    public ulong? Player;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public abstract Task Execute();
}

internal sealed class DirectEffect : Effect
{
    public Func<Task> Body = () => Task.CompletedTask;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override Task Execute() => Body();
}

// Models a mod's inherited compatibility bridge, without naming that mod.
internal abstract class BridgeEffect : Effect
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public sealed override Task Execute() => LegacyHook();
    protected abstract Task LegacyHook();
}

internal sealed class BridgedEffect : BridgeEffect
{
    public Func<Task> Body = () => Task.CompletedTask;
    protected override Task LegacyHook() => Body();
}

internal sealed record Choice(Effect? Source = null, ulong? Dealer = null);
internal sealed record Hit(int Amount, Effect? Source, ulong? Player);

internal static class Damage
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<IReadOnlyList<Hit>> Deal(Choice choice, int amount, List<Hit> hits,
                                  Task? pause = null, Effect? listener = null)
    {
        if (pause != null) await pause;
        if (listener != null) await listener.Execute();
        Effect? source = choice.Source ?? AttributionProbe.DamageSource;
        hits.Add(new Hit(amount, source, choice.Dealer ?? source?.Player));
        return hits;
    }
}

internal static class Probe
{
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Equal<T>(T want, T actual, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(want, actual))
            throw new InvalidOperationException($"{why}: expected {want}, got {actual}");
    }
    private static void Source(Effect? want, Hit hit, int amount)
    {
        if (!ReferenceEquals(want, hit.Source)) throw new InvalidOperationException($"source: expected {want?.Name ?? "unknown"}, got {hit.Source?.Name ?? "unknown"}");
        Equal(amount, hit.Amount, "damage amount");
        Equal(want?.Player, hit.Player, "player ownership");
    }

    public static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("metadata slot discovery agrees with runtime reflection", () =>
        {
            MethodInfo[] runtime = HookDiscovery.Find(typeof(Effect), typeof(Effect).Assembly.GetTypes());
            MethodInfo[] metadata = HookDiscovery.Find(typeof(Effect), typeof(Effect).Assembly.GetTypes(), metadataOnly: true);
            Equal(true, runtime.ToHashSet().SetEquals(metadata), "slot discovery");
            Equal(2, runtime.Length, "one direct declaration and one inherited bridge");
            return Task.CompletedTask;
        });
        yield return ("fresh context without dealer preserves live effect", async () =>
        {
            var hits = new List<Hit>();
            var effect = new DirectEffect { Name = "A", Player = 1 };
            effect.Body = () => Damage.Deal(new Choice(), 8, hits);
            await effect.Execute();
            Source(effect, hits.Single(), 8);
        });
        yield return ("source survives real asynchronous suspension", async () =>
        {
            var hits = new List<Hit>(); var gate = Gate();
            var effect = new DirectEffect { Name = "A", Player = 1 };
            effect.Body = async () => { await gate.Task; await Damage.Deal(new Choice(), 9, hits); };
            Task task = effect.Execute();
            Equal(false, task.IsCompleted, "probe must suspend");
            Equal<Effect?>(null, AttributionProbe.EffectSource, "caller context restored before completion");
            gate.SetResult(); await task;
            Source(effect, hits.Single(), 9);
        });
        yield return ("inherited compatibility bridge preserves derived instance", async () =>
        {
            var hits = new List<Hit>(); var effect = new BridgedEffect { Name = "bridge", Player = 2 };
            effect.Body = async () => { await Task.Yield(); await Damage.Deal(new Choice(), 7, hits); };
            await effect.Execute(); Source(effect, hits.Single(), 7);
        });
        yield return ("overlapping same-type effects keep separate owners", async () =>
        {
            var hitsA = new List<Hit>(); var hitsB = new List<Hit>(); var a = Gate(); var b = Gate();
            var first = new DirectEffect { Name = "same type A", Player = 1 };
            var second = new DirectEffect { Name = "same type B", Player = 2 };
            first.Body = async () => { await a.Task; await Damage.Deal(new Choice(), 8, hitsA); };
            second.Body = async () => { await b.Task; await Damage.Deal(new Choice(), 9, hitsB); };
            Task ta = first.Execute(); Task tb = second.Execute();
            b.SetResult(); await tb; a.SetResult(); await ta;
            Source(first, hitsA.Single(), 8); Source(second, hitsB.Single(), 9);
        });
        yield return ("nested reaction cannot steal outer damage", async () =>
        {
            var hits = new List<Hit>();
            var outer = new DirectEffect { Name = "outer", Player = 1 };
            var reaction = new DirectEffect { Name = "reaction", Player = 2 };
            reaction.Body = async () => { await Task.Yield(); await Damage.Deal(new Choice(), 3, hits); };
            outer.Body = () => Damage.Deal(new Choice(), 8, hits, listener: reaction);
            await outer.Execute();
            Source(reaction, hits[0], 3); Source(outer, hits[1], 8);
        });
        yield return ("explicit context and dealer retain precedence", async () =>
        {
            var hits = new List<Hit>(); var explicitEffect = new DirectEffect { Name = "card", Player = 1 };
            var ambient = new DirectEffect { Name = "ambient", Player = 2 };
            ambient.Body = () => Damage.Deal(new Choice(explicitEffect, 3), 8, hits);
            await ambient.Execute();
            Equal(explicitEffect, hits.Single().Source, "explicit source"); Equal<ulong?>(3, hits.Single().Player, "explicit dealer");
        });
        yield return ("enemy effect stays named without player credit", async () =>
        {
            var hits = new List<Hit>(); var effect = new DirectEffect { Name = "enemy" };
            effect.Body = () => Damage.Deal(new Choice(), 5, hits);
            await effect.Execute(); Source(effect, hits.Single(), 5);
        });
        yield return ("unobserved damage remains unknown", async () =>
        {
            var hits = new List<Hit>(); await Damage.Deal(new Choice(), 5, hits); Source(null, hits.Single(), 5);
        });
        yield return ("faulted effect does not contaminate next damage", async () =>
        {
            var hits = new List<Hit>(); var error = new InvalidOperationException("sentinel");
            var effect = new DirectEffect { Name = "fault", Player = 1 };
            effect.Body = async () => { await Task.Yield(); throw error; };
            Exception? observed = null; try { await effect.Execute(); } catch (Exception e) { observed = e; }
            Equal(error, observed, "original exception identity");
            await Damage.Deal(new Choice(), 5, hits); Source(null, hits.Single(), 5);
        });
        yield return ("synchronous throw restores caller", async () =>
        {
            var hits = new List<Hit>(); var error = new InvalidOperationException("sentinel");
            var effect = new DirectEffect { Name = "sync fault", Player = 1, Body = () => throw error };
            Exception? observed = null; try { await effect.Execute(); } catch (Exception e) { observed = e; }
            Equal(error, observed, "original synchronous exception");
            await Damage.Deal(new Choice(), 5, hits); Source(null, hits.Single(), 5);
        });
        yield return ("completed effect does not credit detached continuation", async () =>
        {
            var hits = new List<Hit>(); var gate = Gate(); Task? detached = null;
            var effect = new DirectEffect { Name = "detached", Player = 1 };
            effect.Body = () => { detached = Task.Run(async () => { await gate.Task; await Damage.Deal(new Choice(), 8, hits); }); return Task.CompletedTask; };
            await effect.Execute(); gate.SetResult(); await detached!; Source(null, hits.Single(), 8);
        });
        yield return ("completed nested effect does not fall back to active parent", async () =>
        {
            var hits = new List<Hit>(); var gate = Gate(); Task? detached = null;
            var child = new DirectEffect { Name = "child", Player = 2 };
            child.Body = () => { detached = Task.Run(async () => { await gate.Task; await Damage.Deal(new Choice(), 8, hits); }); return Task.CompletedTask; };
            var parent = new DirectEffect { Name = "parent", Player = 1 };
            parent.Body = async () => { await child.Execute(); gate.SetResult(); await detached!; };
            await parent.Execute(); Source(null, hits.Single(), 8);
        });
        yield return ("combat generation invalidates old suspended work", async () =>
        {
            var hits = new List<Hit>(); var gate = Gate(); var effect = new DirectEffect { Name = "old combat", Player = 1 };
            effect.Body = async () => { await gate.Task; await Damage.Deal(new Choice(), 8, hits); };
            Task task = effect.Execute(); AttributionProbe.NewCombat(); gate.SetResult(); await task; Source(null, hits.Single(), 8);
        });
        yield return ("damage operation captures source before its own await", async () =>
        {
            var hits = new List<Hit>(); var gate = Gate(); var effect = new DirectEffect { Name = "A", Player = 1 };
            effect.Body = () => Damage.Deal(new Choice(), 8, hits, pause: gate.Task);
            Task task = effect.Execute(); gate.SetResult(); await task; Source(effect, hits.Single(), 8);
        });
        yield return ("patch preserves returned task identity", async () =>
        {
            var gate = Gate(); var effect = new DirectEffect { Name = "task identity", Body = () => gate.Task };
            Task actual = effect.Execute(); Equal(gate.Task, actual, "Task must not be replaced"); gate.SetResult(); await actual;
        });
        yield return ("cancellation retains token and clears attribution", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var hits = new List<Hit>();
            var effect = new DirectEffect { Name = "cancelled", Player = 1 };
            effect.Body = async () => await Task.Delay(Timeout.Infinite, cancellation.Token);
            Task task = effect.Execute(); cancellation.Cancel();
            CancellationToken observed = default;
            try { await task; } catch (OperationCanceledException e) { observed = e.CancellationToken; }
            Equal(cancellation.Token, observed, "cancellation token"); Equal(true, task.IsCanceled, "cancelled Task state");
            await Damage.Deal(new Choice(), 8, hits); Source(null, hits.Single(), 8);
        });
        yield return ("suppressed execution-context flow stays unknown", async () =>
        {
            var hits = new List<Hit>(); var effect = new DirectEffect { Name = "no context", Player = 1 };
            effect.Body = () =>
            {
                using (ExecutionContext.SuppressFlow()) return Task.Run(() => Damage.Deal(new Choice(), 8, hits));
            };
            await effect.Execute(); Source(null, hits.Single(), 8);
        });
    }
}
