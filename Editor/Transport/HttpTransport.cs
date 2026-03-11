using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEditor;
using UnityMCP.Utils;

namespace UnityMCP.Transport
{
    public class HttpTransport : IDisposable
    {
        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<HttpListenerContext> _pendingRequests = new ConcurrentQueue<HttpListenerContext>();
        private readonly ConcurrentDictionary<string, SseClient> _sseClients = new ConcurrentDictionary<string, SseClient>();
        private int _port;

        public event Action<string, Action<string>> OnMessageReceived;
        public event Action<string> OnError;
        public event Action<string> OnLog;

        public bool IsRunning => _running;
        public int Port => _port;

        public void Start(int port = 6400)
        {
            if (_running) return;

            _port = port;
            _running = true;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "UnityMCP-HttpListener"
                };
                _listenerThread.Start();

                EditorApplication.update += ProcessRequests;
                OnLog?.Invoke($"HTTP server started on port {port}");
            }
            catch (Exception ex)
            {
                _running = false;
                OnError?.Invoke($"Failed to start HTTP server: {ex.Message}");
            }
        }

        public void Stop()
        {
            _running = false;
            EditorApplication.update -= ProcessRequests;

            // Close all SSE clients
            foreach (var client in _sseClients.Values)
            {
                client.Close();
            }
            _sseClients.Clear();

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }

            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Join(1000);
            }

            OnLog?.Invoke("HTTP server stopped");
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    _pendingRequests.Enqueue(context);
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        OnError?.Invoke($"HTTP listener error: {ex.Message}");
                    }
                }
            }
        }

        private void ProcessRequests()
        {
            while (_pendingRequests.TryDequeue(out var context))
            {
                try
                {
                    HandleRequest(context);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Error handling request: {ex.Message}");
                    SendErrorResponse(context, 500, "Internal Server Error");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Add CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            // Handle preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            string path = request.Url.AbsolutePath.ToLowerInvariant();

            switch (path)
            {
                case "/":
                case "/health":
                    HandleHealthCheck(context);
                    break;

                case "/sse":
                    HandleSseConnection(context);
                    break;

                case "/message":
                case "/rpc":
                    HandleJsonRpcRequest(context);
                    break;

                case "/mcp":
                    HandleMcpEndpoint(context);
                    break;

                default:
                    SendErrorResponse(context, 404, "Not Found");
                    break;
            }
        }

        private void HandleHealthCheck(HttpListenerContext context)
        {
            var healthInfo = new Dictionary<string, object>
            {
                { "status", "ok" },
                { "server", "UnityMCP" },
                { "version", "1.0.0" },
                { "unityVersion", Application.unityVersion },
                { "projectName", Application.productName },
                { "transport", "http" },
                { "sseClients", _sseClients.Count }
            };

            SendJsonResponse(context, healthInfo);
        }

        private void HandleSseConnection(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            string clientId = Guid.NewGuid().ToString();
            var client = new SseClient(clientId, response);
            _sseClients[clientId] = client;

            // Send initial connection event
            client.SendEvent("connected", new Dictionary<string, object>
            {
                { "clientId", clientId },
                { "server", "UnityMCP" }
            });

            OnLog?.Invoke($"SSE client connected: {clientId}");
        }

        private void HandleJsonRpcRequest(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST")
            {
                SendErrorResponse(context, 405, "Method Not Allowed");
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                body = reader.ReadToEnd();
            }

            OnLog?.Invoke($"Received: {body}");

            OnMessageReceived?.Invoke(body, (responseJson) =>
            {
                SendJsonResponse(context, responseJson, isRawJson: true);
                OnLog?.Invoke($"Sent: {responseJson}");

                // Also broadcast to SSE clients
                BroadcastToSseClients("response", responseJson);
            });
        }

        private void HandleMcpEndpoint(HttpListenerContext context)
        {
            if (context.Request.HttpMethod == "GET")
            {
                // Return server capabilities
                var capabilities = new Dictionary<string, object>
                {
                    { "name", "unity-mcp" },
                    { "version", "1.0.0" },
                    { "protocolVersion", "2024-11-05" },
                    { "capabilities", new Dictionary<string, object>
                        {
                            { "tools", new Dictionary<string, object> { { "listChanged", true } } },
                            { "resources", new Dictionary<string, object> { { "subscribe", false }, { "listChanged", true } } }
                        }
                    }
                };
                SendJsonResponse(context, capabilities);
            }
            else if (context.Request.HttpMethod == "POST")
            {
                HandleJsonRpcRequest(context);
            }
            else
            {
                SendErrorResponse(context, 405, "Method Not Allowed");
            }
        }

        public void BroadcastToSseClients(string eventType, object data)
        {
            var deadClients = new List<string>();

            foreach (var kvp in _sseClients)
            {
                if (!kvp.Value.SendEvent(eventType, data))
                {
                    deadClients.Add(kvp.Key);
                }
            }

            foreach (var clientId in deadClients)
            {
                _sseClients.TryRemove(clientId, out _);
                OnLog?.Invoke($"SSE client disconnected: {clientId}");
            }
        }

        private void SendJsonResponse(HttpListenerContext context, object data, bool isRawJson = false)
        {
            var response = context.Response;
            response.ContentType = "application/json";
            response.StatusCode = 200;

            string json = isRawJson ? data.ToString() : SerializationHelper.ToJson(data);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        private void SendErrorResponse(HttpListenerContext context, int statusCode, string message)
        {
            var response = context.Response;
            response.StatusCode = statusCode;
            response.ContentType = "application/json";

            var error = new Dictionary<string, object>
            {
                { "error", message },
                { "code", statusCode }
            };

            string json = SerializationHelper.ToJson(error);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class SseClient
    {
        public string Id { get; }
        private readonly HttpListenerResponse _response;
        private readonly StreamWriter _writer;
        private bool _closed;

        public SseClient(string id, HttpListenerResponse response)
        {
            Id = id;
            _response = response;
            _writer = new StreamWriter(response.OutputStream, Encoding.UTF8) { AutoFlush = true };
        }

        public bool SendEvent(string eventType, object data)
        {
            if (_closed) return false;

            try
            {
                string json = data is string s ? s : SerializationHelper.ToJson(data);
                _writer.WriteLine($"event: {eventType}");
                _writer.WriteLine($"data: {json}");
                _writer.WriteLine();
                return true;
            }
            catch
            {
                _closed = true;
                return false;
            }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;

            try
            {
                _writer?.Close();
                _response?.Close();
            }
            catch { }
        }
    }
}
