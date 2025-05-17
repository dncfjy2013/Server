using Server.Core.Certification;

Server.Core.ServerInstance server = new Server.Core.ServerInstance(1111, 2222, 3333, "http://localhost:9999/", SSLManager.LoadOrCreateCertificate());
server.Start(false);

//console.writeline("press enter to stop the server...");
Console.ReadLine();

server.Stop();