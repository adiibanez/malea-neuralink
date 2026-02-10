using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// Thread-safe persistent audit logger for wheelchair serial commands.
/// Callable from any thread including the background SendLoop.
/// Uses a ConcurrentQueue + dedicated writer thread for zero-impact logging.
/// CSV format: Timestamp,Category,Message,Command,Success
/// </summary>
public static class AuditLog
{
    public enum Category
    {
        SerialCommand,
        Override,
        Connection,
        StateChange,
        Relay,
        Macro,
        Application,
        Safety
    }

    private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
    private static Thread _writerThread;
    private static volatile bool _running;
    private static string _filePath;
    private static readonly object _initLock = new object();
    private static bool _initialized;

    /// <summary>
    /// Initializes the audit log. Call once from JoystickController.Awake().
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public static void Init()
    {
        lock (_initLock)
        {
            if (_initialized) return;

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "AuditLogs");
                Directory.CreateDirectory(dir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _filePath = Path.Combine(dir, $"audit_{timestamp}.csv");

                // Write CSV header
                File.WriteAllText(_filePath, "Timestamp,Category,Message,Command,Success\n");

                _running = true;
                _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "AuditLogWriter" };
                _writerThread.Start();

                _initialized = true;
                Log(Category.Application, "Session started");

                Debug.Log($"[AuditLog] Initialized: {_filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuditLog] Init failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Shuts down the audit log, flushing remaining entries.
    /// Call from JoystickController.OnApplicationQuit().
    /// </summary>
    public static void Shutdown()
    {
        lock (_initLock)
        {
            if (!_initialized) return;

            Log(Category.Application, "Session ended");

            _running = false;

            if (_writerThread != null && _writerThread.IsAlive)
            {
                _writerThread.Join(2000);
            }

            // Flush anything remaining
            Flush();

            _initialized = false;
            Debug.Log("[AuditLog] Shutdown complete");
        }
    }

    /// <summary>
    /// Logs an entry. Thread-safe, lock-free enqueue.
    /// </summary>
    public static void Log(Category category, string message, string command = "", bool success = true)
    {
        if (!_initialized) return;

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string safeMessage = EscapeCsv(message);
        string safeCommand = EscapeCsv(command);
        string line = $"{timestamp},{category},{safeMessage},{safeCommand},{success}";

        _queue.Enqueue(line);
    }

    /// <summary>
    /// Background writer thread — flushes queue to disk every 50ms.
    /// </summary>
    private static void WriterLoop()
    {
        while (_running)
        {
            Flush();
            Thread.Sleep(50);
        }

        // Final flush on exit
        Flush();
    }

    private static void Flush()
    {
        if (_queue.IsEmpty) return;

        try
        {
            using (var writer = new StreamWriter(_filePath, append: true))
            {
                while (_queue.TryDequeue(out string line))
                {
                    writer.WriteLine(line);
                }
            }
        }
        catch (Exception e)
        {
            // Avoid infinite recursion — don't call Log() here
            Debug.LogError($"[AuditLog] Write failed: {e.Message}");
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
