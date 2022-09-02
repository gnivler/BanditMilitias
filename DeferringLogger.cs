using System;
using System.IO;

namespace BanditMilitias
{
    // simplified from https://github.com/BattletechModders/IRBTModUtils
    internal class DeferringLogger
    {
        private readonly LogWriter logWriter;
        private static DeferringLogger instance;
        internal static DeferringLogger Instance => instance ??= new DeferringLogger();

        private DeferringLogger()
        {
            logWriter = new LogWriter(new StreamWriter(SubModule.logFilename, true));
        }

        internal LogWriter? Debug => Globals.Settings.Debug ? logWriter : null;

        internal readonly struct LogWriter
        {
            private readonly StreamWriter sw;

            public LogWriter(StreamWriter sw)
            {
                this.sw = sw;
                sw.AutoFlush = true;
            }

            internal void Log(object input)
            {
                sw.WriteLine($"[{DateTime.Now.ToLongTimeString()}] {(string.IsNullOrEmpty(input?.ToString()) ? "IsNullOrEmpty" : input)}");
            }
        }
    }
}
