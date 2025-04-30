using Microsoft.Extensions.ObjectPool;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public class SocketPoolPolicy : IPooledObjectPolicy<Socket>
{
    public Socket Create()
    {
        return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    public bool Return(Socket obj)
    {
        // 重置Socket状态以便重用
        obj.Shutdown(SocketShutdown.Both);
        obj.Disconnect(true);
        return true;
    }
}

public class SslStreamPoolPolicy : IPooledObjectPolicy<SslStream>
{
    private readonly X509Certificate2 _serverCert;

    public SslStreamPoolPolicy(X509Certificate2 serverCert)
    {
        _serverCert = serverCert;
    }

    public SslStream Create()
    {
        // 创建新的SslStream（需要TcpClient）
        var tcpClient = new TcpClient();
        return new SslStream(tcpClient.GetStream(), false);
    }

    public bool Return(SslStream obj)
    {
        // 重置SslStream状态以便重用
        obj.Close();
        return true;
    }
}