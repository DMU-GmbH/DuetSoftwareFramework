﻿using DuetAPIClient;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DuetWebServer.Authorization
{
    /// <summary>
    /// Storage class for internal HTTP sessions
    /// </summary>
    public static class Sessions
    {
        /// <summary>
        /// Anonymous ticket that is used in case no password is set
        /// </summary>
        public static readonly AuthenticationTicket AnonymousTicket = new(new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("access", Policies.ReadWrite)
        })), SessionKeyAuthenticationHandler.SchemeName);

        /// <summary>
        /// Internal session wrapper around auth tickets
        /// </summary>
        private class Session
        {
            public AuthenticationTicket Ticket { get; }

            public string Key { get => Ticket.Principal.FindFirst("key").Value; }

            public int SessionId { get => Convert.ToInt32(Ticket.Principal.FindFirst("sessionId").Value); }

            public DateTime LastQueryTime { get; set; } = DateTime.Now;

            public int NumWebSocketsConnected { get; set; }

            public int NumRunningRequests { get; set; }

            public Session(AuthenticationTicket ticket)
            {
                Ticket = ticket;
            }
        }

        /// <summary>
        /// Internal logger instance
        /// </summary>
        private static ILogger _logger;

        /// <summary>
        /// Set the log factory initially so logging can be used in this static context
        /// </summary>
        /// <param name="logFactory">Logger factory</param>
        public static void SetLogFactory(ILoggerFactory logFactory) => _logger = logFactory.CreateLogger(nameof(Sessions));

        /// <summary>
        /// List of active sessions
        /// </summary>
        private static readonly List<Session> _sessions = new();

        /// <summary>
        /// Check if the given session key provides the requested access to the given policy
        /// </summary>
        /// <param name="key">Session key</param>
        /// <param name="readWrite">If readWrite or readOnly policy is requested</param>
        /// <returns>True if access is granted</returns>
        public static bool CheckSessionKey(string key, bool readWrite)
        {
            lock (_sessions)
            {
                foreach (Session item in _sessions)
                {
                    if (item.Key == key)
                    {
                        string access = item.Ticket.Principal.FindFirstValue("access");
                        if ((readWrite && access == Policies.ReadWrite) || (!readWrite && (access == Policies.ReadOnly || access == Policies.ReadWrite)))
                        {
                            item.LastQueryTime = DateTime.Now;
                            return true;
                        }
                        return false;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Make a new session key and register it if the session ID is valid
        /// </summary>
        /// <param name="sessionId">DSF session ID</param>
        /// <param name="readWrite">Whether the client has read-write or read-only access</param>
        /// <returns>Authentication ticket</returns>
        public static string MakeSessionKey(int sessionId, bool readWrite)
        {
            lock (_sessions)
            {
                string sessionKey = Guid.NewGuid().ToString("N");
                ClaimsIdentity identity = new(new[] {
                    new Claim("access", readWrite ? Policies.ReadWrite : Policies.ReadOnly),
                    new Claim("key", sessionKey),
                    new Claim("sessionId", sessionId.ToString())
                }, nameof(SessionKeyAuthenticationHandler));
                AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), SessionKeyAuthenticationHandler.SchemeName);
                if (sessionId > 0)
                {
                    _sessions.Add(new(ticket));
                    _logger?.LogInformation("Session {0} added ({1})", sessionKey, readWrite ? "readWrite" : "readOnly");
                }
                return sessionKey;
            }
        }

        /// <summary>
        /// Get a session ID from the given key
        /// </summary>
        /// <param name="key">Key to query</param>
        /// <returns>Session ID or -1</returns>
        public static int GetSessionId(string key)
        {
            lock (_sessions)
            {
                foreach (Session item in _sessions)
                {
                    if (item.Key == key)
                    {
                        item.LastQueryTime = DateTime.Now;
                        return item.SessionId;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Get a ticket from the given key
        /// </summary>
        /// <param name="key">Key to query</param>
        /// <returns>Authentication ticket or null</returns>
        public static AuthenticationTicket GetTicket(string key)
        {
            lock (_sessions)
            {
                foreach (Session item in _sessions)
                {
                    if (item.Key == key)
                    {
                        item.LastQueryTime = DateTime.Now;
                        return item.Ticket;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Remove a session ticket returning the corresponding session ID
        /// </summary>
        /// <returns>Session ID or 0 if none was found</returns>
        public static int RemoveTicket(ClaimsPrincipal user)
        {
            lock (_sessions)
            {
                foreach (Session item in _sessions)
                {
                    if (item.Ticket.Principal == user)
                    {
                        _logger?.LogInformation("Session {0} removed", user.FindFirstValue("key"));
                        _sessions.Remove(item);
                        return item.SessionId;
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// Set whether a given socket is connected over WebSocket
        /// </summary>
        /// <param name="key">Session key</param>
        /// <param name="webSocketConnected">Whether a WebSocket is connected</param>
        public static void SetWebSocketState(string key, bool webSocketConnected)
        {
            lock (_sessions)
            {
                foreach (Session item in _sessions)
                {
                    if (item.Key == key)
                    {
                        item.LastQueryTime = DateTime.Now;
                        if (webSocketConnected)
                        {
                            item.NumWebSocketsConnected++;
                            _logger?.LogInformation("Session {0} registered a WebSocket connection", key);
                        }
                        else
                        {
                            item.NumWebSocketsConnected--;
                            _logger?.LogInformation("Session {0} unregistered a WebSocket connection", key);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Set whether a potentially long-running HTTP request has started or finished
        /// </summary>
        /// <param name="key">Session key</param>
        /// <param name="requestStarted">Whether a WebSocket is connected</param>
        public static void SetLongRunningHttpRequest(ClaimsPrincipal user, bool requestStarted)
        {
            lock (_sessions)
            {
                foreach (Session item in _sessions)
                {
                    if (item.Ticket.Principal == user)
                    {
                        item.LastQueryTime = DateTime.Now;
                        if (requestStarted)
                        {
                            item.NumRunningRequests++;
                            _logger?.LogInformation("Session {0} started a long-running request", user.FindFirstValue("key"));
                        }
                        else
                        {
                            item.NumRunningRequests--;
                            _logger?.LogInformation("Session {0} finished a long-running request", user.FindFirstValue("key"));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove sessions that are no longer active
        /// </summary>
        /// <param name="sessionTimeout">Timeout for HTTP sessions</param>
        /// <param name="socketPath">API socket path</param>
        public static void MaintainSessions(TimeSpan sessionTimeout, string socketPath)
        {
            lock (_sessions)
            {
                for (int i = _sessions.Count - 1; i >= 0; i--)
                {
                    Session item = _sessions[i];
                    if (item.NumWebSocketsConnected == 0 && item.NumRunningRequests == 0 && DateTime.Now - item.LastQueryTime > sessionTimeout)
                    {
                        // Session expired
                        _sessions.RemoveAt(i);
                        _logger?.LogInformation("Session {0} expired", item.Key);

                        // Attempt to remove it again from DCS
                        _ = Task.Run(async () => await UnregisterExpiredSession(item.SessionId, socketPath));
                    }
                }
            }
        }

        /// <summary>
        /// Unregister an expired HTTP session from the object model
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="socketPath">API socket path</param>
        /// <returns></returns>
        private static async Task UnregisterExpiredSession(int sessionId, string socketPath)
        {
            try
            {
                using CommandConnection connection = new();
                await connection.Connect(socketPath);
                await connection.RemoveUserSession(sessionId);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to unregister expired user session");
            }
        }
    }
}