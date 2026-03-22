using System;
using System.Threading;

namespace ApiClient
{
    /// <summary>
    /// Built-in circuit breaker pattern for API resilience.
    /// Prevents cascading failures by temporarily blocking requests 
    /// to a failing service.
    /// 
    /// RestSharp does NOT have this built-in — requires Polly integration.
    /// 
    /// States:
    ///   Closed   → Normal operation, requests pass through
    ///   Open     → Too many failures, requests immediately rejected
    ///   HalfOpen → After timeout, allows one probe request through
    /// </summary>
    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private int _failureCount;
        private CircuitState _state = CircuitState.Closed;
        private DateTime _openedAt;
        private readonly object _lock = new object();

        /// <summary>
        /// Creates a circuit breaker.
        /// </summary>
        /// <param name="failureThreshold">Number of consecutive failures before opening (default: 5).</param>
        /// <param name="openDuration">How long to stay open before allowing a probe (default: 30s).</param>
        public CircuitBreaker(int failureThreshold = 5, TimeSpan? openDuration = null)
        {
            _failureThreshold = failureThreshold;
            _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        }

        /// <summary>Current state of the circuit.</summary>
        public CircuitState State
        {
            get
            {
                lock (_lock)
                {
                    if (_state == CircuitState.Open && DateTime.UtcNow - _openedAt >= _openDuration)
                        _state = CircuitState.HalfOpen;
                    return _state;
                }
            }
        }

        /// <summary>Current consecutive failure count.</summary>
        public int FailureCount => _failureCount;

        /// <summary>
        /// Checks if a request is allowed through the circuit.
        /// Throws CircuitOpenException if the circuit is open.
        /// </summary>
        public void EnsureAllowed()
        {
            var state = State;
            if (state == CircuitState.Open)
                throw new CircuitOpenException(
                    $"Circuit breaker is open. Will retry after {_openDuration.TotalSeconds}s.");
        }

        /// <summary>
        /// Records a successful request.
        /// </summary>
        public void RecordSuccess()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = CircuitState.Closed;
            }
        }

        /// <summary>
        /// Records a failed request.
        /// </summary>
        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                if (_failureCount >= _failureThreshold)
                {
                    _state = CircuitState.Open;
                    _openedAt = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Resets the circuit to closed state.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = CircuitState.Closed;
            }
        }

        /// <summary>
        /// Creates a middleware that uses this circuit breaker.
        /// </summary>
        public MiddlewareFunc AsMiddleware()
        {
            return async (ctx, next, ct) =>
            {
                EnsureAllowed();
                try
                {
                    var resp = await next(ct).ConfigureAwait(false);
                    if (resp.IsServerError)
                        RecordFailure();
                    else
                        RecordSuccess();
                    return resp;
                }
                catch (Exception ex) when (!(ex is CircuitOpenException))
                {
                    RecordFailure();
                    throw;
                }
            };
        }
    }

    /// <summary>
    /// Circuit breaker states.
    /// </summary>
    public enum CircuitState
    {
        /// <summary>Normal operation — requests flow through.</summary>
        Closed,
        /// <summary>Too many failures — requests are blocked.</summary>
        Open,
        /// <summary>Probe period — allows one request to test recovery.</summary>
        HalfOpen
    }

    /// <summary>
    /// Exception thrown when the circuit breaker is in the Open state.
    /// </summary>
    public class CircuitOpenException : ApiException
    {
        public CircuitOpenException(string message) : base(message) { }
    }
}
