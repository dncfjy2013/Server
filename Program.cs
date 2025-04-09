Server.Server server = new Server.Server(12345, 8888);
server.Start(false);

Console.WriteLine("Press Enter to stop the server...");
Console.ReadLine();

server.Stop();