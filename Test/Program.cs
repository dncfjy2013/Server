using CoordinateSystem;
using log4net;
using log4net.Config;
using System;
using Test;

public class Program
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(Program));

    public static void Main()
    {

        XmlConfigurator.Configure(new FileInfo("log4net.config"));
        _log.Info("应用启动");

        CoordinateFullTest.RunAllTests();

    }
}