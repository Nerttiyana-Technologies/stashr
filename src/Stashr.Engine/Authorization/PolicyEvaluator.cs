using Stashr.Core.Authorization;
using Stashr.Core.Model;

namespace Stashr.Engine.Authorization;

/// <summary>
/// Deny-by-default path-capability evaluator with deterministic precedence (ADR-0008):
/// exact match beats glob, the most-specific match wins, an explicit <c>deny</c> at that tier
/// overrides, and with no matching rule access is denied. The same instance backs both
/// enforcement and the "explain access" endpoint, so they can never diverge.
/// </summary>
public sealed class PolicyEvaluator : IPolicyEvaluator
{
    public AccessDecision Evaluate(IReadOnlyList<Policy> policies, string path, Capability requested)
    {
        var matches = new List<(string Policy, PolicyRule Rule, int Spec)>();
        foreach (var policy in policies)
            foreach (var rule in policy.Rules)
                if (TryMatch(rule.PathPattern, path, out var spec))
                    matches.Add((policy.Name, rule, spec));

        if (matches.Count == 0)
            return new AccessDecision
            {
                Allowed = false,
                Path = path,
                Requested = requested,
                Explanation = $"denied: no policy rule matches '{path}' (deny by default).",
            };

        var maxSpec = matches.Max(m => m.Spec);

        // An explicit deny at the most-specific tier always wins.
        foreach (var m in matches)
            if (m.Spec == maxSpec && m.Rule.Capabilities.HasFlag(Capability.Deny))
                return new AccessDecision
                {
                    Allowed = false,
                    Path = path,
                    Requested = requested,
                    WinningRule = m.Rule,
                    WinningPolicy = m.Policy,
                    Explanation =
                        $"denied: rule '{m.Rule.PathPattern}' in policy '{m.Policy}' explicitly denies (most-specific match).",
                };

        // Otherwise: allowed if any matching rule grants the requested capability.
        foreach (var m in matches)
            if (!m.Rule.Capabilities.HasFlag(Capability.Deny) && m.Rule.Capabilities.HasFlag(requested))
                return new AccessDecision
                {
                    Allowed = true,
                    Path = path,
                    Requested = requested,
                    WinningRule = m.Rule,
                    WinningPolicy = m.Policy,
                    Explanation =
                        $"allowed: rule '{m.Rule.PathPattern}' in policy '{m.Policy}' grants {requested}.",
                };

        return new AccessDecision
        {
            Allowed = false,
            Path = path,
            Requested = requested,
            Explanation = $"denied: a rule matches '{path}' but none grants {requested}.",
        };
    }

    /// <summary>
    /// Matches a path against a pattern and reports specificity. Exact match is most specific
    /// (<see cref="int.MaxValue"/>); a trailing-<c>*</c> glob matches by prefix with specificity
    /// equal to the prefix length (longer prefix = more specific).
    /// </summary>
    private static bool TryMatch(string pattern, string path, out int specificity)
    {
        if (string.Equals(pattern, path, StringComparison.Ordinal))
        {
            specificity = int.MaxValue;
            return true;
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                specificity = prefix.Length;
                return true;
            }
        }

        specificity = 0;
        return false;
    }
}
