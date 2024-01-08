using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SimpleNetwork2;

public class SampleServer
{
    StandardServer server;
    StandardClient client;
    static void Main(string[] args)
    {
        new SampleServer();
    }
    public SampleServer()
    {
        server = new StandardServer();
        client = new StandardClient();
        client.client.Close();
        while (!Console.ReadLine().Trim().ToLower().Equals("e"))
        {
            
        }
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

        public void DataToss(SNServerClientSocket NowSocket, SNBuffer Read, SNBuffer My, SNBuffer Other)
        {
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

        public void CreateSpace()
        {
            
        }

        public void InClient(SNServerClientSocket clientIn)
        {
            Console.WriteLine($"Client Enter : {clientIn.socket.RemoteEndPoint}");
        }

        public void Loop(List<SNServerClientSocket> clients)
        {
            
        }

        public void OutClient(SNServerClientSocket clientOut)
        {
            Console.WriteLine($"Client Exit : ");
        }

        public void close()
        {
            server.Close();
        }
    }
}
