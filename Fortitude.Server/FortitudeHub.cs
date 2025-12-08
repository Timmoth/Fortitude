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
        private readonly ConnectedClientService _clients;

        /// <summary>
        /// Initializes a new instance of <see cref="FortitudeHub"/>.
        /// </summary>
        /// <param name="store">The pending request store to track and complete requests.</param>
        /// <param name="logger">Logger instance for diagnostics.</param>
        public FortitudeHub(PendingRequestStore store, ILogger<FortitudeHub> logger, ConnectedClientService clients)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clients = clients ?? throw new ArgumentNullException(nameof(clients));
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
        /// Allows a connected client to query which port it has been assigned.
        /// </summary>
        public int GetAssignedPort()
        {
            var connId = Context.ConnectionId;
            var port = _clients.GetPortForClient(connId);

            return port ?? -1; // Return -1 if not assigned
        }
        
        /// <summary>
        /// Called when a client connects to the hub.
        /// </summary>
        public override Task OnConnectedAsync()
        {
            _clients.Add(Context.ConnectionId);

            return base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a client disconnects from the hub.
        /// </summary>
        /// <param name="exception">Optional exception if the disconnect was due to an error.</param>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _clients.Remove(Context.ConnectionId);

            if (exception != null)
            {
                _logger.LogWarning(exception, "Client {ConnectionId} disconnected due to error", Context.ConnectionId);
            }
            
            return base.OnDisconnectedAsync(exception);
        }
    }
}
