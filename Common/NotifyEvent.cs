using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common
{
    // 1. 声明委托
    public delegate void MessageRefresh(object sender, string message);

    public class NotifyEvent
    {
        public static event MessageRefresh OnMessageReceived;
        public void MessageRefreshSendMessage(string message)
        {
            // 触发事件（线程安全写法）
            OnMessageReceived?.Invoke(this, message);
        }
    }
}
