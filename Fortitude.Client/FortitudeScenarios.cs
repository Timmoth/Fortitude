using System.Collections.Concurrent;

namespace Fortitude.Client;

/// <summary>
///     Named state machines that let handlers model a multi-step flow.
/// </summary>
/// <remarks>
///     A scenario is just a name and a current state. Handlers can require a state to match,
///     and move the scenario to another state once they have answered — which is how a
///     sequence like create, then poll pending, then poll complete is expressed without the
///     handlers knowing about each other.
/// </remarks>
public sealed class FortitudeScenarios
{
    /// <summary>The state every scenario begins in.</summary>
    public const string InitialState = "Started";

    private readonly ConcurrentDictionary<string, string> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A snapshot of every scenario that has been used, and its current state.</summary>
    public IReadOnlyDictionary<string, string> States => new Dictionary<string, string>(_states);

    /// <summary>
    ///     The current state of <paramref name="scenario" />, which is
    ///     <see cref="InitialState" /> until something moves it.
    /// </summary>
    public string GetState(string scenario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        return _states.GetValueOrDefault(scenario, InitialState);
    }

    /// <summary>Moves <paramref name="scenario" /> to <paramref name="state" />.</summary>
    public void SetState(string scenario, string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        _states[scenario] = state;
    }

    /// <summary>Returns one scenario to <see cref="InitialState" />.</summary>
    public void Reset(string scenario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        _states.TryRemove(scenario, out _);
    }

    /// <summary>Returns every scenario to <see cref="InitialState" />.</summary>
    public void ResetAll()
    {
        _states.Clear();
    }
}

/// <summary>
///     Ties a handler to a scenario: the state it requires, and the state it moves to.
/// </summary>
/// <param name="Scenarios">The state machine this binding reads and writes.</param>
/// <param name="Name">The scenario's name.</param>
/// <param name="RequiredState">
///     The state the scenario must be in for the handler to match. Null matches any state.
/// </param>
/// <param name="NextState">
///     The state to move to after answering. Null leaves the scenario where it is.
/// </param>
public sealed record ScenarioBinding(
    FortitudeScenarios Scenarios,
    string Name,
    string? RequiredState,
    string? NextState)
{
    /// <summary>Whether the scenario is currently in the state this handler requires.</summary>
    public bool IsSatisfied() =>
        RequiredState == null
        || string.Equals(Scenarios.GetState(Name), RequiredState, StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies the transition, if this binding declares one.</summary>
    public void Advance()
    {
        if (NextState != null)
            Scenarios.SetState(Name, NextState);
    }
}
