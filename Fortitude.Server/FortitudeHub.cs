using System.Collections.Concurrent;
using Fortitude.Client;
using Microsoft.AspNetCore.SignalR;

namespace Fortitude.Server
{
    /// <summary>
    /// SignalR Hub for Fortitude clients to submit responses and maintain connection state.
    /// </summary>
    public class FortitudeHub : Hub
    {
        private readonly PendingRequestStore _store;
        private readonly ILogger<FortitudeHub> _logger;

        /// <summary>
        /// Thread-safe dictionary of currently connected clients.
        /// Key and value are ConnectionId.
        /// </summary>
        public static ConcurrentDictionary<string, string> ConnectedClients { get; } = new();

        /// <summary>
        /// Initializes a new instance of <see cref="FortitudeHub"/>.
        /// </summary>
        /// <param name="store">The pending request store to track and complete requests.</param>
        /// <param name="logger">Logger instance for diagnostics.</param>
        public FortitudeHub(PendingRequestStore store, ILogger<FortitudeHub> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Called by clients to submit a response for a pending Fortitude request.
        /// </summary>
        /// <param name="response">The response to complete the request.</param>
        public Task SubmitResponse(FortitudeResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            try
            {
                _logger.LogInformation("Received response for RequestId {RequestId}", response.RequestId);
                _store.Complete(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete request {RequestId}", response.RequestId);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when a client connects to the hub.
        /// </summary>
        public override Task OnConnectedAsync()
        {
            ConnectedClients[Context.ConnectionId] = Context.ConnectionId;

            _logger.LogInformation("Client connected: {ConnectionId}. Total connected: {Count}",
                Context.ConnectionId, ConnectedClients.Count);

            _logger.LogDebug("Current clients: {Clients}", string.Join(", ", ConnectedClients.Keys));

            return base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a client disconnects from the hub.
        /// </summary>
        /// <param name="exception">Optional exception if the disconnect was due to an error.</param>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            ConnectedClients.TryRemove(Context.ConnectionId, out _);

            _logger.LogInformation("Client disconnected: {ConnectionId}. Total connected: {Count}",
                Context.ConnectionId, ConnectedClients.Count);

            if (exception != null)
            {
                _logger.LogWarning(exception, "Client {ConnectionId} disconnected due to error", Context.ConnectionId);
            }

            _logger.LogDebug("Current clients: {Clients}", string.Join(", ", ConnectedClients.Keys));

            return base.OnDisconnectedAsync(exception);
        }
    }
}
