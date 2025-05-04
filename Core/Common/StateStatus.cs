using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Server.Common;
using Server.Logger;

namespace Server.Core.Common
{
    public enum ConnectionState 
    {
        Disconnecting,
        Disconnected, 
        Connecting, 
        Connected,
        Error,
        None,
    }

    public class ConnectionManager : IDisposable
    {
        private readonly StateMachine<int, ConnectionState> _stateMachine = new();
        private readonly ConcurrentDictionary<int, ConnectionClient> _clients = new();
        private readonly ILogger _logger;

        public ConnectionManager(ILogger logger)
        {
            _logger = logger;
            InitializeStateMachine();
        }

        private void InitializeStateMachine()
        {
            // 统一状态转换规则
            _stateMachine.AddTransition(ConnectionState.Disconnected, ConnectionState.Connecting);
            _stateMachine.AddTransition(ConnectionState.Connecting, ConnectionState.Connected);
            _stateMachine.AddTransition(ConnectionState.Connecting, ConnectionState.Error);
            _stateMachine.AddTransition(ConnectionState.Connected, ConnectionState.Disconnecting);
            _stateMachine.AddTransition(ConnectionState.Disconnecting, ConnectionState.Disconnected);
            _stateMachine.AddTransition(ConnectionState.Disconnecting, ConnectionState.Error);
            _stateMachine.AddTransition(ConnectionState.Error, ConnectionState.Disconnected);

            // 全局事件监听
            _stateMachine.OnAfterTransition += (id, oldState, newState) =>
            {
                _logger.LogInformation($"[Client {id}] State changed: {oldState} → {newState}");
            };
        }

        public ConnectionClient CreateClient(int clientId)
        {
            var client = new ConnectionClient(clientId, _stateMachine, _logger);
            return _clients.AddOrUpdate(clientId, client, (_, _) => client);
        }

        public bool RemoveClient(int clientId)
        {
            return _clients.TryRemove(clientId, out _);
        }

        public IEnumerable<ConnectionClient> GetAllClients()
        {
            return _clients.Values.ToArray();
        }

        public void Dispose()
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _stateMachine.Dispose();
        }
    }

    public class ConnectionClient : IDisposable
    {
        private readonly int _clientId;
        private readonly StateMachine<int, ConnectionState> _stateMachine;
        private readonly ILogger _logger;
        private bool _disposed;

        public ConnectionClient(int clientId,
                              StateMachine<int, ConnectionState> stateMachine,
                              ILogger logger)
        {
            _clientId = clientId;
            _stateMachine = stateMachine;
            _logger = logger;

            InitializeClient();
        }

        private void InitializeClient()
        {
            _stateMachine.InitializeState(_clientId, ConnectionState.Disconnected);
            _stateMachine.SetTimeout(_clientId, TimeSpan.FromSeconds(30), ConnectionState.Disconnected);
        }

        public async Task ConnectAsync()
        {
            if (_disposed) return;

            await _stateMachine.TransitionAsync(
                _clientId,
                ConnectionState.Connecting,
                transitionAction: async (id, _, _) =>
                {
                    _logger.LogTrace($"Connecting client {id}...");
                    await SimulateNetworkOperation(1000, 20);
                },
                reason: "User initiated connection"
            );

            if (CurrentState == ConnectionState.Connecting)
            {
                await CompleteConnectionAsync();
            }
        }

        private async Task CompleteConnectionAsync()
        {
            await _stateMachine.TransitionAsync(
                _clientId,
                ConnectionState.Connected,
                transitionAction: async (id, _, _) =>
                {
                    _logger.LogTrace($"Finalizing connection for client {id}...");
                    await SimulateNetworkOperation(500);
                },
                reason: "Connection established"
            );
        }

        public async Task DisconnectAsync()
        {
            if (_disposed) return;

            var success = await _stateMachine.TransitionAsync(
                _clientId,
                ConnectionState.Disconnecting,
                transitionAction: async (id, _, _) =>
                {
                    _logger.LogTrace($"Starting disconnection for client {id}...");
                    await SimulateNetworkOperation(100);
                },
                reason: "Begin disconnecting"
            );

            if (!success) return;

            await _stateMachine.TransitionAsync(
                _clientId,
                ConnectionState.Disconnected,
                transitionAction: async (id, _, _) =>
                {
                    _logger.LogTrace($"Finalizing disconnection for client {id}...");
                    await SimulateNetworkOperation(300, 10);
                },
                reason: "Complete disconnection"
            );
        }

        private async Task SimulateNetworkOperation(int delayMs, int errorChancePercent = 0)
        {
            await Task.Delay(delayMs);
            if (errorChancePercent > 0 && new Random().Next(0, 100) < errorChancePercent)
            {
                _logger.LogWarning($"Operation failed (Client {_clientId})");
            }
        }

        public ConnectionState CurrentState =>
            _stateMachine.TryGetCurrentState(_clientId, out var state) ? state : ConnectionState.None;

        public bool IsConnected => CurrentState == ConnectionState.Connected;
        public bool IsDisconnecting => CurrentState == ConnectionState.Disconnecting;
        public bool IsDisconnected => CurrentState == ConnectionState.Disconnected;

        public void PrintHistory()
        {
            _logger.LogInformation($"Connection history for client {_clientId}:");
            foreach (var entry in _stateMachine.GetStateHistory(_clientId))
            {
                _logger.LogInformation($"[{entry.Timestamp:HH:mm:ss.fff}] {entry.State} - {entry.Reason}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (!IsDisconnected)
                {
                    DisconnectAsync().GetAwaiter().GetResult();
                }
                _stateMachine.SetTimeout(_clientId, TimeSpan.Zero, ConnectionState.Disconnected);
            }
            finally
            {
                _disposed = true;
            }
        }
    }

}
