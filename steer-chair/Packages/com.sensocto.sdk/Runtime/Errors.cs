using System;

namespace Sensocto.SDK
{
    /// <summary>
    /// Error codes for Sensocto operations.
    /// </summary>
    public enum SensoctoErrorCode
    {
        Unknown = 0,
        ConnectionFailed = 1,
        SocketError = 2,
        ChannelJoinFailed = 3,
        AuthenticationFailed = 4,
        Unauthorized = 5,
        InvalidConfig = 6,
        Timeout = 7,
        ServerError = 8,
        NetworkError = 9,
        InvalidData = 10
    }

    /// <summary>
    /// Represents an error from Sensocto operations.
    /// </summary>
    public class SensoctoError
    {
        /// <summary>
        /// The error code.
        /// </summary>
        public SensoctoErrorCode Code { get; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// The underlying exception, if any.
        /// </summary>
        public Exception Exception { get; }

        public SensoctoError(SensoctoErrorCode code, string message, Exception exception = null)
        {
            Code = code;
            Message = message;
            Exception = exception;
        }

        public override string ToString()
        {
            return $"[{Code}] {Message}";
        }
    }

    /// <summary>
    /// Exception thrown by Sensocto operations.
    /// </summary>
    public class SensoctoException : Exception
    {
        /// <summary>
        /// The error code.
        /// </summary>
        public SensoctoErrorCode Code { get; }

        public SensoctoException(SensoctoErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public SensoctoException(SensoctoErrorCode code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        public override string ToString()
        {
            return $"SensoctoException [{Code}]: {Message}";
        }
    }
}
