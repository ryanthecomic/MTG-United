using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// TCP connection to the Forge bridge. Reads newline-delimited JSON on a background thread and hands messages to
    /// the main thread through a queue (Unity objects may only be touched on the main thread).
    /// </summary>
    public class BridgeClient
    {
        public string Host = "127.0.0.1";
        public int Port = 7777;

        readonly ConcurrentQueue<Dictionary<string, object>> inbox = new ConcurrentQueue<Dictionary<string, object>>();
        readonly object writeLock = new object();
        TcpClient tcp;
        StreamWriter writer;
        Thread readerThread;
        volatile bool connected;
        volatile bool connecting;

        public bool Connected => connected;
        public bool Connecting => connecting;
        public string LastError { get; private set; }

        public void ConnectAsync()
        {
            if (connected || connecting) return;
            connecting = true;
            var t = new Thread(() =>
            {
                try
                {
                    var c = new TcpClient();
                    c.NoDelay = true;
                    c.Connect(Host, Port);
                    var stream = c.GetStream();
                    lock (writeLock)
                    {
                        tcp = c;
                        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    }
                    connected = true;
                    LastError = null;
                    readerThread = new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "bridge-reader" };
                    readerThread.Start();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
                finally
                {
                    connecting = false;
                }
            }) { IsBackground = true, Name = "bridge-connect" };
            t.Start();
        }

        void ReadLoop(NetworkStream stream)
        {
            try
            {
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        try
                        {
                            var msg = Json.ParseObject(line);
                            if (msg != null) inbox.Enqueue(msg);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[MTGUnited] Bad message from bridge: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                connected = false;
                inbox.Enqueue(new Dictionary<string, object> { { "t", "_disconnected" } });
            }
        }

        public bool TryReceive(out Dictionary<string, object> msg) => inbox.TryDequeue(out msg);

        public void Send(Dictionary<string, object> msg)
        {
            string line = Json.Write(msg);
            lock (writeLock)
            {
                if (writer == null || !connected) return;
                try { writer.WriteLine(line); }
                catch (Exception ex) { LastError = ex.Message; }
            }
        }

        public void Send(string type, params object[] kv)
        {
            var d = new Dictionary<string, object> { { "t", type } };
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            Send(d);
        }

        public void Close()
        {
            lock (writeLock)
            {
                try { tcp?.Close(); } catch { }
                tcp = null;
                writer = null;
            }
            connected = false;
        }
    }
}
