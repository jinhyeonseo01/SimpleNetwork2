# Simple Network 2

- **C# asynchronous Multi-Core server/client socket library based on IOCP - EAP SocketAsyncEventArgs supporting TCP/UDP.**
- **C# Tcp/Udp를 지원하는 IOCP - EAP SocketAsyncEventArgs, TaskPool 기반 비동기 멀티코어 서버/클라 소켓 라이브러리.**
### *2018.08.01*
* * *
## Examples

``` csharp
StandardServer server;
StandardClient client;
static void Main(string[] args)
{
    new SampleServer();
}

StandardServer server;
StandardClient client;

public SampleServer()
{
    server = new StandardServer();
    client = new StandardClient();

    //Wait Server Close Event
    
    server.close();
    client.client.Close();
}

public class StandardClient
{
    public SNClient client;
    public StandardClient()
    {
        client = new SNClient();
        client.Setting("127.0.0.1", 55700, SocketType.Tcp);
        client.Start(() =>
        {
            Console.WriteLine("Connect");
        });
    }
}


public class StandardServer : IServerToss, IServerSpaceLogic
{
    SNServer server;

    public StandardServer()
    {
        server = new SNServer();
        var spaceSystem = new SpaceSystem(64);
        server.spaceSystem = spaceSystem;
        server.DefualtSpace = spaceSystem.CreateSpace();
        server.DefualtSpace.LogicProcess = this;
        server.DefualtSpace.TossProcess = this;
        server.Setting(55700, SocketType.Tcp);
        server.Start();
    }

    public void DataToss(SNServerClientSocket NowSocket, SNBuffer Read, SNBuffer My, SNBuffer Other) {
        int packetId = Read.GetInt();
        switch (packetId)
        {
            case 0:
            {
                int index;
                My.Start(out index);
                    int size = Read.EndPoint - 4;
                    My.PutInt(0);
                    My.PutB2BVar(Read, size);
                My.End(index);
                break;
            }
            case 1:
            {
                int index;
                Other.Start(out index);
                    int size = Read.EndPoint - 4;
                    Other.PutInt(0);
                    Other.PutB2BVar(Read, size);
                Other.End(index);
                break;
            }
            case 2:
            {

                break;
            }
        }
    }

    public void CreateSpace() {
        
    }

    public void InClient(SNServerClientSocket clientIn) {
        Console.WriteLine($"Client Enter : {clientIn.socket.RemoteEndPoint}");
    }

    public void Loop(List<SNServerClientSocket> clients) {
        
    }

    public void OutClient(SNServerClientSocket clientOut) {
        Console.WriteLine($"Client Exit : ");
    }

    public void close() {
        server.Close();
    }
}
```