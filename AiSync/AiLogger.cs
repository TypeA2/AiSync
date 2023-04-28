using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AiSync {
    public static class AiLoggerExtensions {
        public static ILoggingBuilder AddAiLogger(this ILoggingBuilder builder) {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILoggerProvider, AiLoggerProvider>());

            return builder;
        }
    }

    [ProviderAlias("AiLogger")]
    public sealed class AiLoggerProvider : ILoggerProvider {
        private readonly ConcurrentDictionary<string, AiLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

        public ILogger CreateLogger(string categoryName) {
            return _loggers.GetOrAdd(categoryName, name => new AiLogger(name));
        }

        public void Dispose() {
            _loggers.Clear();
        }
    }

    public sealed class AiLogger : ILogger {
        private readonly string _name;

        public AiLogger(string name) {
            _name = name;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) {
            return Debugger.IsAttached && logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (!IsEnabled(logLevel)) {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            string msg = formatter(state, exception);

            if (String.IsNullOrEmpty(msg)) {
                return;
            }

            msg = $"[{DateTime.UtcNow}] [{logLevel}] [{_name}] {msg}{Environment.NewLine}";

            if (exception is not null) {
                msg += Environment.NewLine + Environment.NewLine + exception;
            }

            Debugger.Log((int)logLevel, logLevel.ToString(), msg);
        }
    }
}
