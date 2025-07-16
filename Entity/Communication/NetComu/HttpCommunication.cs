using Entity.Communication.ComComu;
using Entity.Communication.NetComu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.NetComu
{
    // HTTP通信实现
    public class HttpCommunication : NetComuBase
    {
        public HttpCommunication(string host, int port) : base(host, port, NetType.HTTP)
        {
        }

        protected override async Task<Task> ConnectAsync()
        {
            try
            {
                // HTTP是无状态协议，不需要显式连接
                SetStatus(CommunicationStatus.Connected);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                SetStatus(CommunicationStatus.Disconnected);
                throw new InvalidOperationException($"HTTP初始化失败: {ex.Message}", ex);
            }
        }

        protected override async Task<Task> DisconnectAsync()
        {
            try
            {
                // HTTP是无状态协议，不需要显式断开
                SetStatus(CommunicationStatus.Disconnected);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ConnectionFailed, $"HTTP断开失败: {ex.Message}", ex);
                throw;
            }
        }

        protected override async Task ProcessSendQueueAsync()
        {
            try
            {
                while (_sendQueue.TryDequeue(out byte[] data))
                {
                    try
                    {
                        // 这里应该解析HTTP请求，实际项目中需要更完善的实现
                        string request = Encoding.UTF8.GetString(data);
                        string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);

                        if (lines.Length < 1)
                        {
                            OnErrorOccurred(CommunicationErrorType.ProtocolError, "无效的HTTP请求");
                            continue;
                        }

                        string[] requestLine = lines[0].Split(' ');
                        if (requestLine.Length < 3)
                        {
                            OnErrorOccurred(CommunicationErrorType.ProtocolError, "无效的HTTP请求行");
                            continue;
                        }

                        string method = requestLine[0];
                        string path = requestLine[1];

                        // 构建完整URL
                        string url = $"http://{Host}:{Port}{path}";

                        using (HttpClient client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromMilliseconds(SendTimeout);

                            HttpResponseMessage response;

                            switch (method.ToUpper())
                            {
                                case "GET":
                                    response = await client.GetAsync(url);
                                    break;
                                case "POST":
                                    // 提取请求体
                                    int bodyIndex = Array.IndexOf(lines, "");
                                    string body = bodyIndex >= 0 ? string.Join("\r\n", lines.Skip(bodyIndex + 1)) : "";
                                    response = await client.PostAsync(url, new StringContent(body));
                                    break;
                                case "PUT":
                                    // 提取请求体
                                    bodyIndex = Array.IndexOf(lines, "");
                                    body = bodyIndex >= 0 ? string.Join("\r\n", lines.Skip(bodyIndex + 1)) : "";
                                    response = await client.PutAsync(url, new StringContent(body));
                                    break;
                                default:
                                    OnErrorOccurred(CommunicationErrorType.ProtocolError, $"不支持的HTTP方法: {method}");
                                    continue;
                            }

                            if (response.IsSuccessStatusCode)
                            {
                                byte[] responseData = await response.Content.ReadAsByteArrayAsync();
                                ProcessReceivedData(responseData);
                            }
                            else
                            {
                                OnErrorOccurred(CommunicationErrorType.ProtocolError,
                                    $"HTTP请求失败: {response.StatusCode} {response.ReasonPhrase}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(CommunicationErrorType.SendFailed, $"发送HTTP请求失败: {ex.Message}", ex);
                        // 不重试HTTP请求，因为可能是幂等操作
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.SendFailed, $"处理HTTP发送队列失败: {ex.Message}", ex);
            }
        }

        protected override Task ReceiveDataLoopAsync()
        {
            // HTTP是请求-响应模式，不需要单独的接收循环
            return Task.CompletedTask;
        }

        // 扩展方法：直接发送HTTP请求
        public async Task<HttpResponseMessage> SendHttpRequestAsync(string method, string path, string content = null)
        {
            try
            {
                if (Status != CommunicationStatus.Connected)
                    await OpenAsync();

                string url = $"http://{Host}:{Port}{path}";

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(SendTimeout);

                    HttpResponseMessage response = null;

                    switch (method.ToUpper())
                    {
                        case "GET":
                            response = await client.GetAsync(url);
                            break;
                        case "POST":
                            response = await client.PostAsync(url, new StringContent(content ?? ""));
                            break;
                        case "PUT":
                            response = await client.PutAsync(url, new StringContent(content ?? ""));
                            break;
                        case "DELETE":
                            response = await client.DeleteAsync(url);
                            break;
                        default:
                            throw new NotSupportedException($"不支持的HTTP方法: {method}");
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        byte[] responseData = await response.Content.ReadAsByteArrayAsync();
                        ProcessReceivedData(responseData);
                    }

                    return response;
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.SendFailed, $"发送HTTP请求失败: {ex.Message}", ex);
                throw;
            }
        }
    }

    // 使用示例
    public class HttpProtocolExample
    {
        public static async Task Main(string[] args)
        {
            // HTTP通信示例
            using (HttpCommunication httpComm = new HttpCommunication("api.example.com", 80))
            {
                httpComm.DataReceived += (sender, data) =>
                {
                    string response = Encoding.UTF8.GetString(data);
                    Console.WriteLine($"收到HTTP响应: {response}");
                };

                httpComm.ErrorOccurred += (sender, e) =>
                {
                    Console.WriteLine($"HTTP通信错误: {e.Message}");
                };

                await httpComm.OpenAsync();
                Console.WriteLine("HTTP通信已初始化");

                // 发送HTTP GET请求
                var getResponse = await httpComm.SendHttpRequestAsync("GET", "/api/data");
                Console.WriteLine($"GET请求状态: {getResponse.StatusCode}");

                // 发送HTTP POST请求
                var postResponse = await httpComm.SendHttpRequestAsync("POST", "/api/submit", "param1=value1&param2=value2");
                Console.WriteLine($"POST请求状态: {postResponse.StatusCode}");
            }
        }
    }
}
