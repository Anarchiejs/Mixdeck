using Mixdeck.Core.Model;
using Mixdeck.Core.Rules;
namespace Mixdeck.Core.Tests;
public sealed class RuleTests
{
    private static Rule ProfileRule(string id, string exe, int priority = 0, bool revert = true) =>
        new() { Id = id, Name = id, ProcessName = exe, ProfileId = id, Priority = priority, RevertWhenInactive = revert };
    [Fact]
    public void PriorityThenMostRecentActivationAndBaseline()
    {
        var engine = new RuleEngine();
        engine.SetBaselineProfile("base");
        engine.SetRules([ProfileRule("a", "A"), ProfileRule("b", "B"), ProfileRule("c", "C", 1)]);
        engine.Initialize([], null);
        Assert.Equal("a", engine.OnProcessStarted("A.exe").ProfileId);
        Assert.Null(engine.Evaluate().ProfileId);
        Assert.Equal("b", engine.OnProcessStarted("b").ProfileId);
        Assert.Equal("c", engine.OnProcessStarted("C").ProfileId);
        Assert.Null(engine.OnProcessExited("A").ProfileId);
        Assert.Equal("b", engine.OnProcessExited("c").ProfileId);
        Assert.Equal("base", engine.OnProcessExited("B.exe").ProfileId);
        Assert.Null(engine.Evaluate().ProfileId);
    }
    [Fact]
    public void ForegroundNormalizationInstancesAndNoRevert()
    {
        var engine = new RuleEngine();
        engine.SetBaselineProfile("base");
        engine.SetRules([ProfileRule("music", "FL64.exe"), ProfileRule("game", "game") with { Trigger = RuleTrigger.ProcessForeground, Priority = 2, RevertWhenInactive = false }]);
        engine.Initialize(["fl64", "FL64.EXE"], null);
        Assert.Equal("music", engine.Evaluate().ProfileId);
        Assert.Null(engine.OnProcessExited("FL64.exe").ProfileId);
        Assert.Equal("base", engine.OnProcessExited("fl64").ProfileId);
        Assert.Equal("game", engine.OnForegroundChanged("GAME.exe").ProfileId);
        Assert.Null(engine.OnForegroundChanged(null).ProfileId);
        Assert.Null(engine.Evaluate().ProfileId);
    }
    [Fact]
    public void MicroAndProfileWinnersAreIndependentAndRulesReload()
    {
        var engine = new RuleEngine();
        engine.SetBaselineMicMuted(false);
        engine.SetBaselineProfile("base");
        var p = ProfileRule("a", "app");
        var mic = p with { Id = "mic", Action = RuleActionKind.SetMicMuted, MicMuted = true };
        engine.SetRules([p, mic]);
        engine.Initialize([], null);
        var decision = engine.OnProcessStarted("app");
        Assert.Equal("a", decision.ProfileId);
        Assert.True(decision.MicMuted);
        Assert.Same(p, decision.ProfileCause);
        Assert.Same(mic, decision.MicCause);
        engine.SetRules([p with { Enabled = false }, mic with { Enabled = false }]);
        decision = engine.Evaluate();
        Assert.Equal("base", decision.ProfileId);
        Assert.False(decision.MicMuted);
        Assert.Null(engine.Evaluate().MicMuted);
    }
    [Fact]
    public void UnrelatedEventsDoNotRenewActivationAndReactivationDoes()
    {
        var engine = new RuleEngine();
        engine.SetRules([ProfileRule("a", "a"), ProfileRule("b", "b")]);
        engine.Initialize([], null);
        engine.OnProcessStarted("a");
        engine.OnProcessStarted("b");
        Assert.Null(engine.OnProcessStarted("other").ProfileId);
        engine.OnProcessExited("a");
        Assert.Equal("a", engine.OnProcessStarted("a").ProfileId);
    }
}
