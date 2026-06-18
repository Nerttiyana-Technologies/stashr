using Stashr.Core.Model;
using Stashr.Engine.Authorization;
using Xunit;

namespace Stashr.Engine.Tests;

public class PolicyEvaluatorTests
{
    private readonly PolicyEvaluator _eval = new();

    private static Policy Policy(string name, params PolicyRule[] rules) =>
        new() { Name = name, Rules = rules };

    private static PolicyRule Rule(string path, Capability caps) =>
        new() { PathPattern = path, Capabilities = caps };

    [Fact]
    public void No_matching_rule_is_denied_by_default()
    {
        var d = _eval.Evaluate(new[] { Policy("p", Rule("secret/other/*", Capability.Read)) },
            "secret/app/db", Capability.Read);
        Assert.False(d.Allowed);
    }

    [Fact]
    public void Exact_match_grants_capability()
    {
        var d = _eval.Evaluate(new[] { Policy("p", Rule("secret/app/db", Capability.Read)) },
            "secret/app/db", Capability.Read);
        Assert.True(d.Allowed);
        Assert.Equal("secret/app/db", d.WinningRule!.PathPattern);
    }

    [Fact]
    public void Glob_match_grants_capability()
    {
        var d = _eval.Evaluate(new[] { Policy("p", Rule("secret/app/*", Capability.Read | Capability.List)) },
            "secret/app/db", Capability.Read);
        Assert.True(d.Allowed);
    }

    [Fact]
    public void Capability_not_granted_is_denied()
    {
        var d = _eval.Evaluate(new[] { Policy("p", Rule("secret/app/*", Capability.Read)) },
            "secret/app/db", Capability.Update);
        Assert.False(d.Allowed);
    }

    [Fact]
    public void Explicit_deny_at_most_specific_match_wins_over_glob_allow()
    {
        var policy = Policy("p",
            Rule("secret/app/*", Capability.Read),
            Rule("secret/app/db", Capability.Deny));

        var d = _eval.Evaluate(new[] { policy }, "secret/app/db", Capability.Read);

        Assert.False(d.Allowed);
        Assert.Equal("secret/app/db", d.WinningRule!.PathPattern);
    }

    [Fact]
    public void Longest_prefix_glob_is_more_specific()
    {
        var policy = Policy("p",
            Rule("secret/*", Capability.Read),
            Rule("secret/app/*", Capability.Deny));

        var d = _eval.Evaluate(new[] { policy }, "secret/app/db", Capability.Read);

        Assert.False(d.Allowed); // the longer (more specific) deny wins
    }

    [Fact]
    public void Decision_carries_an_explanation()
    {
        var d = _eval.Evaluate(new[] { Policy("app-read", Rule("secret/app/*", Capability.Read)) },
            "secret/app/db", Capability.Read);
        Assert.Contains("app-read", d.Explanation);
    }
}
