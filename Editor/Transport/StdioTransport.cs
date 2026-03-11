using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEditor;
using UnityMCP.Utils;

namespace UnityMCP.Transport
{
    public class StdioTransport : IDisposable
    {
        private Thread _readThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outgoingMessages = new ConcurrentQueue<string>();
        private TextReader _input;
        private TextWriter _output;
        private readonly object _writeLock = new object();

        public event Action<string> OnMessageReceived;
        public event Action<string> OnError;

        public bool IsRunning => _running;

        public void Start()
        {
            if (_running) return;

            _running = true;
            _input = Console.In;
            _output = Console.Out;

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "UnityMCP-StdioRead"
            };
            _readThread.Start();

            EditorApplication.update += ProcessMessages;
        }

        public void Stop()
        {
            _running = false;
            EditorApplication.update -= ProcessMessages;

            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(1000);
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (_running)
                {
                    string line = _input.ReadLine();
                    if (line == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _incomingMessages.Enqueue(line);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    _incomingMessages.Enqueue($"{{\"error\": \"{ex.Message}\"}}");
                }
            }
        }

        private void ProcessMessages()
        {
            // Process incoming messages on main thread
            while (_incomingMessages.TryDequeue(out string message))
            {
                try
                {
                    OnMessageReceived?.Invoke(message);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Error processing message: {ex.Message}");
                }
            }

            // Send outgoing messages
            while (_outgoingMessages.TryDequeue(out string response))
            {
                WriteMessage(response);
            }
        }

        public void SendMessage(string message)
        {
            _outgoingMessages.Enqueue(message);
        }

        public void SendMessageImmediate(string message)
        {
            WriteMessage(message);
        }

        private void WriteMessage(string message)
        {
            lock (_writeLock)
            {
                try
                {
                    _output.WriteLine(message);
                    _output.Flush();
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Error writing message: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class JsonRpcMessage
    {
        public string jsonrpc = "2.0";
        public object id;
        public string method;
        public Dictionary<string, object> @params;
        public object result;
        public JsonRpcError error;

        public static JsonRpcMessage Parse(string json)
        {
            var msg = new JsonRpcMessage();
            var dict = SerializationHelper.ParseJson(json);

            if (dict.TryGetValue("jsonrpc", out object jsonrpc))
                msg.jsonrpc = jsonrpc?.ToString();

            if (dict.TryGetValue("id", out object id))
                msg.id = id;

            if (dict.TryGetValue("method", out object method))
                msg.method = method?.ToString();

            if (dict.TryGetValue("params", out object p) && p is Dictionary<string, object> pDict)
                msg.@params = pDict;

            if (dict.TryGetValue("result", out object result))
                msg.result = result;

            if (dict.TryGetValue("error", out object err) && err is Dictionary<string, object> errDict)
            {
                msg.error = new JsonRpcError();
                if (errDict.TryGetValue("code", out object code))
                    msg.error.code = Convert.ToInt32(code);
                if (errDict.TryGetValue("message", out object errMsg))
                    msg.error.message = errMsg?.ToString();
                if (errDict.TryGetValue("data", out object data))
                    msg.error.data = data;
            }

            return msg;
        }

        public string ToJson()
        {
            var dict = new Dictionary<string, object> { { "jsonrpc", jsonrpc } };

            if (id != null)
                dict["id"] = id;

            if (!string.IsNullOrEmpty(method))
                dict["method"] = method;

            if (@params != null)
                dict["params"] = @params;

            if (result != null)
                dict["result"] = result;

            if (error != null)
            {
                dict["error"] = new Dictionary<string, object>
                {
                    { "code", error.code },
                    { "message", error.message },
                    { "data", error.data }
                };
            }

            return SerializationHelper.ToJson(dict);
        }

        public static JsonRpcMessage CreateResponse(object id, object result)
        {
            return new JsonRpcMessage { id = id, result = result };
        }

        public static JsonRpcMessage CreateError(object id, int code, string message, object data = null)
        {
            return new JsonRpcMessage
            {
                id = id,
                error = new JsonRpcError { code = code, message = message, data = data }
            };
        }

        public static JsonRpcMessage CreateNotification(string method, Dictionary<string, object> @params = null)
        {
            return new JsonRpcMessage { method = method, @params = @params };
        }
    }

    public class JsonRpcError
    {
        public int code;
        public string message;
        public object data;

        // Standard JSON-RPC error codes
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;

        // Custom error codes
        public const int UnityRuntimeError = -32000;
        public const int SecurityError = -32001;
        public const int NotFoundError = -32002;
    }
}
