using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.HttpService
{
    // HTTP服务基接口
    public interface IHttpService
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
        string Host { get; }
    }
}
