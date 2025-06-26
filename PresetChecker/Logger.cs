using Serilog;
using Serilog.Events;
using System;

namespace PresetChecker
{
    internal class Logger
    {
        private Serilog.Core.Logger _log;
        public Logger(string filename)
        {
            _log = new LoggerConfiguration()
                .WriteTo.File(filename)
                .CreateLogger();
        }

        public void Write(string info)
        {
            _log.Write(Serilog.Events.LogEventLevel.Information, info);
            Console.WriteLine(info);
        }

        public void Write<T>(string info, T value)
        {
            _log.Write(Serilog.Events.LogEventLevel.Information, info, value);
            Console.WriteLine(info, value);
        }

        public void Write<T0, T1>(string info, T0 value0, T1 value1)
        {
            _log.Write(Serilog.Events.LogEventLevel.Information, info, value0, value1);
            Console.WriteLine(info, value0, value1);
        }

        public void Write<T0, T1, T2>(string info, T0 value0, T1 value1, T2 value2)
        {
            _log.Write(Serilog.Events.LogEventLevel.Information, info, value0, value1, value2);
            Console.WriteLine(info, value0, value1, value2);
        }
    }
}
