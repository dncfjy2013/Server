using Server.Common.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Constants
{
    public class ThreadName
    {
        public static readonly string InComing = "InComing".Center(9, " ");
        public static readonly string OutComing = "OutComing".Center(9, " ");
        public static readonly string Main = "Main".Center(9, " ");
    }
}
