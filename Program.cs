using Server.Core.Certification;
using Server.Proxy.Common;
using Server.Test;

Server.Core.ServerInstance server = new Server.Core.ServerInstance(1111, 2222, 3333, "http://localhost:9999/", SSLManager.LoadOrCreateCertificate());
server.Start(false);

//Console.WriteLine("Press Enter to stop the server...");
Console.ReadLine();

server.Stop();
IP_ZONE.Main();