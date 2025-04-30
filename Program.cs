Server.Core.ServerInstance server = new Server.Core.ServerInstance(12345, 8888, 6666);
server.Start(false);

Console.WriteLine("Press Enter to stop the server...");
Console.ReadLine();

server.Stop();