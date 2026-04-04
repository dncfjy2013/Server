using log4net;
using MethodBoundaryAspect;
using MethodBoundaryAspect.Fody.Attributes;
using System;
using System.Reflection;
using System.Text;

[assembly: LogAspect]

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class NoLogAttribute : Attribute
{
}

public class LogAspect : OnMethodBoundaryAspect
{
    private ILog GetLogger(MethodExecutionArgs args)
    {
        return LogManager.GetLogger(args.Method?.DeclaringType ?? typeof(LogAspect));
    }

    public override void OnEntry(MethodExecutionArgs args)
    {
        try
        {
            if (IsNoLog(args.Method))
                return;

            var log = GetLogger(args);
            if (!log.IsDebugEnabled)
                return;

            var sb = new StringBuilder();
            sb.Append($"Entry：{args.Method.Name}(");
            var arguments = args.Arguments.ToArray();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(arguments[i]?.ToString() ?? "null");
            }

            sb.Append(")");
            log.Debug(sb.ToString());
        }
        catch { }
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        try
        {
            if (IsNoLog(args.Method))
                return;

            var log = GetLogger(args);
            if (!log.IsDebugEnabled)
                return;

            var msg = $"Leave：{args.Method.Name}";
            if (args.ReturnValue != null)
                msg += $" Return：{args.ReturnValue}";

            log.Debug(msg);
        }
        catch { }
    }

    public override void OnException(MethodExecutionArgs args)
    {
        try
        {
            if (IsNoLog(args.Method))
                return;

            var log = GetLogger(args);
            log.Error($"Exception：{args.Method.Name}", args.Exception);
        }
        catch { }
    }

    /// <summary>
    /// 🔥 核心：只允许【class】打印日志
    /// </summary>
    private bool IsNoLog(MethodBase method)
    {
        var declaringType = method.DeclaringType;
        if (declaringType == null)
            return true;

        // struct / interface / enum 都不打印
        if (!declaringType.IsClass)
            return true;

        if (method.Name == nameof(ToString))
        {
            return true;
        }
        if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_"))
        {
            return true;
        }
        if (method.IsDefined(typeof(NoLogAttribute), true))
            return true;

        if (declaringType.IsDefined(typeof(NoLogAttribute), true))
            return true;

        return false;
    }
}