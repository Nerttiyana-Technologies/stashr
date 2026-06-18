using Stashr.Core.Model;

namespace Stashr.Core.Authorization;

/// <summary>The result of an authorization check, including why it resolved (ADR-0008).</summary>
public sealed record AccessDecision
{
    public required bool Allowed { get; init; }
    public required string Path { get; init; }
    public required Capability Requested { get; init; }

    /// <summary>The rule that decided the outcome, if any.</summary>
    public PolicyRule? WinningRule { get; init; }

    /// <summary>The policy that owned the winning rule.</summary>
    public string? WinningPolicy { get; init; }

    /// <summary>Human-readable explanation for the "explain access" tool (ADR-0008).</summary>
    public required string Explanation { get; init; }
}

/// <summary>
/// Evaluates deny-by-default path-capability policies with deterministic precedence
/// (exact &gt; glob, deny wins, longest match) — ADR-0008. The same evaluator powers
/// both enforcement and the explain endpoint, so they can never drift.
/// </summary>
public interface IPolicyEvaluator
{
    AccessDecision Evaluate(IReadOnlyList<Policy> policies, string path, Capability requested);
}
