using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Native serial port implementation for macOS using POSIX system calls.
    /// This bypasses the unsupported System.IO.Ports in Unity IL2CPP builds.
    /// </summary>
    public class NativeSerialPort : IDisposable
    {
        private int _fileDescriptor = -1;
        private bool _isOpen = false;
        private string _portName;

        public bool IsOpen => _isOpen && _fileDescriptor >= 0;
        public string PortName => _portName;

        // POSIX constants for macOS
        private const int O_RDWR = 0x0002;
        private const int O_NOCTTY = 0x20000;
        private const int O_NONBLOCK = 0x0004;

        // Baud rate constants (macOS values)
        private const int B9600 = 9600;
        private const int B19200 = 19200;
        private const int B38400 = 38400;
        private const int B57600 = 57600;
        private const int B115200 = 115200;

        // termios constants
        private const int TCSANOW = 0;
        private const int VMIN = 16;
        private const int VTIME = 17;

        // c_cflag bits
        private const uint CSIZE = 0x00000300;
        private const uint CS8 = 0x00000300;
        private const uint CREAD = 0x00000800;
        private const uint CLOCAL = 0x00008000;
        private const uint PARENB = 0x00001000;
        private const uint CSTOPB = 0x00000400;

        // c_lflag bits
        private const uint ICANON = 0x00000100;
        private const uint ECHO = 0x00000008;
        private const uint ECHOE = 0x00000002;
        private const uint ISIG = 0x00000080;

        // c_iflag bits
        private const uint IXON = 0x00000200;
        private const uint IXOFF = 0x00000400;
        private const uint IXANY = 0x00000800;
        private const uint IGNBRK = 0x00000001;
        private const uint BRKINT = 0x00000002;
        private const uint PARMRK = 0x00000008;
        private const uint ISTRIP = 0x00000020;
        private const uint INLCR = 0x00000040;
        private const uint IGNCR = 0x00000080;
        private const uint ICRNL = 0x00000100;

        // c_oflag bits
        private const uint OPOST = 0x00000001;

        // termios structure for macOS (64-bit)
        [StructLayout(LayoutKind.Sequential)]
        private struct Termios
        {
            public ulong c_iflag;   // input flags
            public ulong c_oflag;   // output flags
            public ulong c_cflag;   // control flags
            public ulong c_lflag;   // local flags
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            public byte[] c_cc;     // control characters
            public ulong c_ispeed;  // input speed
            public ulong c_ospeed;  // output speed
        }

        // Native imports
        [DllImport("libc", SetLastError = true)]
        private static extern int open(string pathname, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr write(int fd, byte[] buf, IntPtr count);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr read(int fd, byte[] buf, IntPtr count);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcgetattr(int fd, ref Termios termios);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcsetattr(int fd, int optional_actions, ref Termios termios);

        [DllImport("libc", SetLastError = true)]
        private static extern int cfsetispeed(ref Termios termios, ulong speed);

        [DllImport("libc", SetLastError = true)]
        private static extern int cfsetospeed(ref Termios termios, ulong speed);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcflush(int fd, int queue_selector);

        [DllImport("libc")]
        private static extern int fcntl(int fd, int cmd, int arg);

        private const int F_SETFL = 4;

        public NativeSerialPort()
        {
        }

        public NativeSerialPort(string portName, int baudRate)
        {
            Open(portName, baudRate);
        }

        public void Open(string portName, int baudRate = 115200)
        {
            if (_isOpen)
            {
                Close();
            }

            _portName = portName;

            // Open the serial port
            _fileDescriptor = open(portName, O_RDWR | O_NOCTTY | O_NONBLOCK);

            if (_fileDescriptor < 0)
            {
                int errno = Marshal.GetLastWin32Error();
                throw new Exception($"Failed to open serial port {portName}: errno={errno}");
            }

            // Configure the port
            if (!ConfigurePort(baudRate))
            {
                close(_fileDescriptor);
                _fileDescriptor = -1;
                throw new Exception($"Failed to configure serial port {portName}");
            }

            // Set to blocking mode for writes
            fcntl(_fileDescriptor, F_SETFL, 0);

            _isOpen = true;
            Debug.Log($"[NativeSerialPort] Opened {portName} at {baudRate} baud");
        }

        private bool ConfigurePort(int baudRate)
        {
            var options = new Termios();
            options.c_cc = new byte[20];

            // Get current options
            if (tcgetattr(_fileDescriptor, ref options) < 0)
            {
                Debug.LogWarning("[NativeSerialPort] tcgetattr failed");
                return false;
            }

            // Set baud rate
            ulong speed = (ulong)baudRate;
            cfsetispeed(ref options, speed);
            cfsetospeed(ref options, speed);

            // 8N1 mode (8 data bits, no parity, 1 stop bit)
            options.c_cflag &= ~PARENB;  // No parity
            options.c_cflag &= ~CSTOPB;  // 1 stop bit
            options.c_cflag &= ~CSIZE;   // Clear size bits
            options.c_cflag |= CS8;      // 8 data bits
            options.c_cflag |= CREAD;    // Enable receiver
            options.c_cflag |= CLOCAL;   // Ignore modem control lines

            // Raw input mode
            options.c_lflag &= ~(ICANON | ECHO | ECHOE | ISIG);

            // Raw output mode
            options.c_oflag &= ~OPOST;

            // Disable software flow control
            options.c_iflag &= ~(IXON | IXOFF | IXANY);
            options.c_iflag &= ~(IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL);

            // Set read timeout behavior
            options.c_cc[VMIN] = 0;   // Non-blocking read
            options.c_cc[VTIME] = 1;  // 0.1 second timeout

            // Apply settings
            if (tcsetattr(_fileDescriptor, TCSANOW, ref options) < 0)
            {
                Debug.LogWarning("[NativeSerialPort] tcsetattr failed");
                return false;
            }

            // Flush buffers
            tcflush(_fileDescriptor, 2); // TCIOFLUSH = 2

            return true;
        }

        public void Write(string data)
        {
            if (!_isOpen || _fileDescriptor < 0)
            {
                throw new InvalidOperationException("Serial port is not open");
            }

            byte[] bytes = Encoding.ASCII.GetBytes(data);
            Write(bytes);
        }

        public void Write(byte[] data)
        {
            if (!_isOpen || _fileDescriptor < 0)
            {
                throw new InvalidOperationException("Serial port is not open");
            }

            IntPtr written = write(_fileDescriptor, data, new IntPtr(data.Length));

            if (written.ToInt64() < 0)
            {
                int errno = Marshal.GetLastWin32Error();
                throw new Exception($"Write failed: errno={errno}");
            }
        }

        public int Read(byte[] buffer)
        {
            if (!_isOpen || _fileDescriptor < 0)
            {
                return 0;
            }

            IntPtr bytesRead = read(_fileDescriptor, buffer, new IntPtr(buffer.Length));
            return (int)bytesRead.ToInt64();
        }

        public string ReadLine()
        {
            if (!_isOpen || _fileDescriptor < 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            var buffer = new byte[1];

            while (true)
            {
                IntPtr bytesRead = read(_fileDescriptor, buffer, new IntPtr(1));

                if (bytesRead.ToInt64() <= 0)
                    break;

                char c = (char)buffer[0];
                if (c == '\n' || c == '\r')
                    break;

                sb.Append(c);
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        public void Close()
        {
            if (_fileDescriptor >= 0)
            {
                close(_fileDescriptor);
                _fileDescriptor = -1;
            }
            _isOpen = false;
            Debug.Log($"[NativeSerialPort] Closed {_portName}");
        }

        public void Dispose()
        {
            Close();
        }
    }
}
