using Server.Logger.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logger._2ExpandVersion.Commmon
{
    // 日志模板配置
    public class LogTemplate
    {
        public string Name { get; set; }
        public string Template { get; set; }
        public LogLevel Level { get; set; }
        public bool IncludeException { get; set; }
        public bool IncludeCallerInfo { get; set; }
    }
}
