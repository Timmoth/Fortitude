using System.Text;

namespace Fortitude.Client;

/// <summary>
///     Thrown when an assertion about the traffic a mock received does not hold.
/// </summary>
public class FortitudeVerificationException : Exception
{
    /// <summary>Initializes a new instance of <see cref="FortitudeVerificationException" />.</summary>
    /// <param name="message">A description of what was expected and what actually happened.</param>
    public FortitudeVerificationException(string message) : base(message)
    {
    }
}

/// <summary>
///     Assertions about the requests a mock received.
/// </summary>
/// <remarks>
///     These exist to produce a failure message that says what went wrong. Asserting on
///     <see cref="FortitudeHandler.ReceivedRequests" /> directly gives you
///     "expected 1, actual 0"; these print the handler and the traffic it did see.
/// </remarks>
public static class FortitudeVerification
{
    /// <summary>
    ///     Asserts the handler received exactly <paramref name="expected" /> requests.
    /// </summary>
    /// <exception cref="FortitudeVerificationException">Thrown when the count differs.</exception>
    public static FortitudeHandler ShouldHaveReceived(
        this FortitudeHandler handler,
        int expected,
        Func<FortitudeRequest, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var matching = Matching(handler, predicate);

        if (matching.Count != expected)
            throw new FortitudeVerificationException(
                Describe(handler, $"Expected {Requests(expected)}, but saw {Requests(matching.Count)}.", matching));

        return handler;
    }

    /// <summary>
    ///     Asserts the handler received at least <paramref name="expected" /> requests.
    /// </summary>
    /// <exception cref="FortitudeVerificationException">Thrown when fewer were received.</exception>
    public static FortitudeHandler ShouldHaveReceivedAtLeast(
        this FortitudeHandler handler,
        int expected,
        Func<FortitudeRequest, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var matching = Matching(handler, predicate);

        if (matching.Count < expected)
            throw new FortitudeVerificationException(
                Describe(handler, $"Expected at least {Requests(expected)}, but saw {Requests(matching.Count)}.",
                    matching));

        return handler;
    }

    /// <summary>
    ///     Asserts the handler received no more than <paramref name="expected" /> requests.
    /// </summary>
    /// <exception cref="FortitudeVerificationException">Thrown when more were received.</exception>
    public static FortitudeHandler ShouldHaveReceivedAtMost(
        this FortitudeHandler handler,
        int expected,
        Func<FortitudeRequest, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var matching = Matching(handler, predicate);

        if (matching.Count > expected)
            throw new FortitudeVerificationException(
                Describe(handler, $"Expected at most {Requests(expected)}, but saw {Requests(matching.Count)}.",
                    matching));

        return handler;
    }

    /// <summary>Asserts the handler received exactly one request.</summary>
    public static FortitudeHandler ShouldHaveReceivedOnce(
        this FortitudeHandler handler,
        Func<FortitudeRequest, bool>? predicate = null) => handler.ShouldHaveReceived(1, predicate);

    /// <summary>Asserts the handler was never called.</summary>
    public static FortitudeHandler ShouldNeverHaveBeenCalled(
        this FortitudeHandler handler,
        Func<FortitudeRequest, bool>? predicate = null) => handler.ShouldHaveReceived(0, predicate);

    private static List<FortitudeRequest> Matching(FortitudeHandler handler, Func<FortitudeRequest, bool>? predicate)
    {
        return handler.ReceivedRequests.Where(r => predicate?.Invoke(r) ?? true).ToList();
    }

    private static string Requests(int count) => count == 1 ? "1 request" : $"{count} requests";

    private static string Describe(FortitudeHandler handler, string problem, IReadOnlyList<FortitudeRequest> seen)
    {
        var message = new StringBuilder();

        message.AppendLine(problem);
        message.AppendLine($"  Handler: {handler}");

        if (seen.Count == 0)
        {
            message.AppendLine("  It received nothing.");
            return message.ToString().TrimEnd();
        }

        message.AppendLine("  It received:");
        foreach (var request in seen.Take(10))
            message.AppendLine($"    {request.Method} {request.Route}{request.RawQuery}");

        if (seen.Count > 10)
            message.AppendLine($"    ... and {seen.Count - 10} more");

        return message.ToString().TrimEnd();
    }
}
