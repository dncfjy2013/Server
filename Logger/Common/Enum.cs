namespace Logger
{
    public enum LogLevel : byte
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    public enum LogOutputType : byte
    {
        Console = 0,
        File = 1,
        MMF = 2,
        FastSafe = 3,
    }
}
