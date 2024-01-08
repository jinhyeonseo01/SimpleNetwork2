using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using SimpleNetwork2.NetPackage;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace SimpleNetwork2
{
    public abstract class SNSocketBase
    {
        public Socket socket;
        public IPEndPoint endPoint;
        public SNSocketSetting state;

        internal SocketAsyncEventArgs AcceptArgs;
        internal SocketAsyncEventArgs ConnectArgs;
        internal SocketAsyncEventArgs DisconnectArgs;
        internal SocketAsyncEventArgs ReceiveArgs;
        internal SocketAsyncEventArgs SendArgs;

        public virtual void Accept(SocketAsyncEventArgs args) { }
        public virtual void Connect(SocketAsyncEventArgs args) { }
        public virtual void Disconnect(SocketAsyncEventArgs args) { }
        public virtual void Receive(SocketAsyncEventArgs args) { }
        public virtual void Send(SocketAsyncEventArgs args) { }

        public virtual void AcceptFail(SocketAsyncEventArgs args) { }
        public virtual void ConnectFail(SocketAsyncEventArgs args) { }
        public virtual void DisconnectFail(SocketAsyncEventArgs args) { }
        public virtual void ReceiveFail(SocketAsyncEventArgs args) { }
        public virtual void SendFail(SocketAsyncEventArgs args) { }
        public void IO_Completed(object socket, SocketAsyncEventArgs args)
        {
            if (args.SocketError == SocketError.Success)
            {
                switch (args.LastOperation)
                {
                    case SocketAsyncOperation.Accept:
                    {
                        Accept(args);
                        break;
                    }
                    case SocketAsyncOperation.Connect:
                    {
                        Connect(args);
                        break;
                    }
                    case SocketAsyncOperation.Disconnect:
                    {
                        Disconnect(args);
                        break;
                    }
                    case SocketAsyncOperation.Receive:
                    {
                        Receive(args);
                        break;
                    }
                    case SocketAsyncOperation.Send:
                    {
                        Send(args);
                        break;
                    }
                }
            }
            else
            {
                switch (args.LastOperation)
                {
                    case SocketAsyncOperation.Accept:
                    {
                        AcceptFail(args);
                        break;
                    }
                    case SocketAsyncOperation.Connect:
                    {
                        ConnectFail(args);
                        break;
                    }
                    case SocketAsyncOperation.Disconnect:
                    {
                        DisconnectFail(args);
                        break;
                    }
                    case SocketAsyncOperation.Receive:
                    {
                        ReceiveFail(args);
                        break;
                    }
                    case SocketAsyncOperation.Send:
                    {
                        SendFail(args);
                        break;
                    }
                }
            }
        }

        public SNSocketBase()
        {

        }
        public void AsyncEventConnect(out SocketAsyncEventArgs args)
        {
            args = SNPools.Pool_SocketArgs.Get();
            args.Completed += IO_Completed;
        }
        public void AsyncEventDisconnect(ref SocketAsyncEventArgs args)
        {
            if (args != null)
            {
                args.Completed -= IO_Completed;
                SNPools.Pool_SocketArgs.Put(args);
                args = null;
            }
        }
        public void ReceiveAsyncProcess(Socket socket, SocketAsyncEventArgs args)
        {
            if (socket.Connected)
            {
                byte[] temp_byte = SNPools.Pool_Byte.Get();
                args.SetBuffer(temp_byte, 0, temp_byte.Length);

                if (!socket.ReceiveAsync(args))
                    Receive(args);
            }
        }

        public void SendAsyncProcess(Socket socket, SocketAsyncEventArgs args, byte[] bytes, int offset, int length)
        {// 수정 필요 Args를 각자 다른 녀석으로 할당해줘야함
            args.SetBuffer(bytes, offset, length);

            switch (state.type)
            {
                case SocketType.Tcp:
                {
                    if (!socket.SendAsync(args))
                        Send(args);
                    break;
                }
                case SocketType.Udp:
                {
                    if (!socket.SendToAsync(args))
                        Send(args);
                    break;
                }
            }
        }
        public void SendProcess(SNBuffer buffer)
        {
            byte[] bytes = buffer.Array;
            int offset = buffer.Position;
            int length = buffer.EndPoint;
            switch (state.type)
            {
                case SocketType.Tcp:
                {
                    socket.Send(bytes, offset, length, SocketFlags.None);
                    break;
                }
                case SocketType.Udp:
                {
                    socket.SendTo(bytes, offset, length, SocketFlags.None, endPoint);
                    break;
                }
            }
        }

        public void Close()
        {
            AsyncEventDisconnect(ref AcceptArgs);
            AsyncEventDisconnect(ref ReceiveArgs);
            AsyncEventDisconnect(ref SendArgs);
            AsyncEventDisconnect(ref ConnectArgs);
            AsyncEventDisconnect(ref DisconnectArgs);

            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }
    }

    public class SNClient
    {
        public SNBuffer SendBuffer;
        public Dictionary<int, Action<SNBuffer>> ReadDic;
        public Queue<SNBuffer> ReceiveDatas;
        internal Action ConnectFunction = () => { };
        SNClientSocket socketBase;

        public bool IsHost
        {
            get;
            internal set;
        }

        public bool IsConnect
        {
            get;
            internal set;
        }
        public bool IsPause = false;
        public bool ISSendAble
        {
            get
            {
                return IsConnect & (!IsPause);
            }
        }
        public SNSocketSetting SocketSetting
        {
            get
            {
                return socketBase.state;
            }
        }

        public SNClient()
        {
            Reset(new SNClientSocket());
        }
        public SNClient(SNClientSocket clientSocket)
        {
            Reset(clientSocket);
        }

        public void Reset(SNClientSocket socket)
        {
            socketBase = socket;
            socketBase.MyClient = this;
            if (socketBase.state == null) socketBase.state = new SNSocketSetting();
            
            SNArray<byte> TempArray;
            //(TempArray = SNPools.Pool_Array.Get()).Setting(SNPools.Pool_Byte.Get());// 원본
            (TempArray = SNPools.Pool_Array.Get()).Setting(new byte[262144]);// 수정요망
            (SendBuffer = SNPools.Pool_Buffer.Get()).Setting(TempArray);
            ReadDic = new Dictionary<int, Action<SNBuffer>>(65535);
            ReceiveDatas = new Queue<SNBuffer>(65535);
        }

        public void Setting(string ip, int port, SocketType type)
        {
            socketBase.state.SettingLogic(ip, port, type);
        }
        public void Start()
        {
            Start(() => { });
        }
        public void Start(Action ConnectFunction)
        {
            this.ConnectFunction = ConnectFunction;
            switch (SocketSetting.type)
            {
                case SocketType.Tcp:
                {
                    socketBase.socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);

                    //IPAddress bnetServerIP = Dns.GetHostAddresses(socketBase.state.ip)[0];
                    //IPEndPoint bnetServerEP = new IPEndPoint(bnetServerIP, socketBase.state.port);

                    socketBase.endPoint = new IPEndPoint(IPAddress.Parse(socketBase.state.ip), socketBase.state.port);
                    //socketBase.endPoint = bnetServerEP;
                    socketBase.ConnectArgs.RemoteEndPoint = socketBase.endPoint;
                    socketBase.socket.NoDelay = true;
                    if (!socketBase.socket.ConnectAsync(socketBase.ConnectArgs))
                        socketBase.Connect(socketBase.ConnectArgs);
                    break;
                }
                case SocketType.Udp:
                {
                    socketBase.socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, ProtocolType.Udp);
                    socketBase.endPoint = new IPEndPoint(IPAddress.Parse(SocketSetting.ip), SocketSetting.port);
                    socketBase.ReceiveArgs.RemoteEndPoint = socketBase.endPoint;
                    socketBase.SendArgs.RemoteEndPoint = socketBase.endPoint;
                    socketBase.socket.Bind(new IPEndPoint(IPAddress.Any, SocketSetting.port));
                    socketBase.socket.EnableBroadcast = true;
                    socketBase.socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
                    socketBase.Connect(socketBase.ConnectArgs);

                    break;
                }
            }

        }

        public void Close()
        {
            socketBase.Clear();
        }

        public void Write(int PacketIndex, Action<SNBuffer> action, bool fail = false)
        {
            if (ISSendAble)
            {
                int index;
                SendBuffer.Start(out index);
                SendBuffer.PutInt(PacketIndex);
                action(SendBuffer);
                SendBuffer.End(index);
            }
        }

        public void AddRead(int index, Action<SNBuffer> action)
        {
            if (!ReadDic.ContainsKey(index))
            {
                ReadDic.Add(index, action);
            }
        }
        public void Send()
        {
            if (ISSendAble)
            {
                SendBuffer.PosClear();
                if (SendBuffer.EndPoint != 0)
                {
                    try
                    {
                        socketBase.SendProcess(SendBuffer);
                    }
                    finally
                    {

                    }
                }
                SendBuffer.PointClear();
            }
        }

        public void Read()
        {
            SNBuffer buffer = null;
            int index;
            while ((buffer = socketBase.BufferRelay.Get()) != null)
            {
                if (ReadDic.ContainsKey(index = buffer.GetInt()))
                {
                    ReadDic[index](buffer);
                }
                SNPools.Pool_Buffer.Put(buffer);
            }
        }
    }

    public class SNClientSocket : SNSocketBase
    {
        public SNPacketReader Reader;
        public SNBuffer ReceiveBuffer;
        public SNBuffer SendBuffer;
        public Queue<SNBuffer> BufferQueue;
        public Relay<SNBuffer> BufferRelay;
        public SNClient MyClient;

        public SNClientSocket()
        {
            AsyncEventConnect(out ReceiveArgs);
            AsyncEventConnect(out SendArgs);
            AsyncEventConnect(out ConnectArgs);
            AsyncEventConnect(out DisconnectArgs);

            Reader = new SNPacketReader();
            BufferQueue = new Queue<SNBuffer>(1024);
            BufferRelay = new Relay<SNBuffer>(65535);
            SNArray<byte> TempArray;
            byte[] TempByte;

            TempByte = SNPools.Pool_Byte.Get();
            ReceiveArgs.SetBuffer(TempByte, 0, TempByte.Length);
            TempByte = SNPools.Pool_Byte.Get();
            SendArgs.SetBuffer(TempByte, 0, TempByte.Length);

            (TempArray = SNPools.Pool_Array.Get()).Setting(SNPools.Pool_Byte.Get());
            (ReceiveBuffer = SNPools.Pool_Buffer.Get()).Setting(TempArray);
            (TempArray = SNPools.Pool_Array.Get()).Setting(SNPools.Pool_Byte.Get());
            (SendBuffer = SNPools.Pool_Buffer.Get()).Setting(TempArray);
        }

        public override void Receive(SocketAsyncEventArgs args)
        {
            if (args.BytesTransferred > 0)
            {
                SNArray<byte> ReadArray;
                (ReadArray = SNPools.Pool_Array.Get()).Setting(args.Buffer, args.BytesTransferred);
                Reader.Read(ReadArray, BufferQueue);

                while (BufferQueue.Count != 0)
                {
                    SNBuffer buffer = BufferQueue.Dequeue();
                    BufferRelay.Put(buffer);
                }

                ReceiveAsyncProcess(socket, ReceiveArgs);
            }
            else
            {
                ReceiveFail(args);
            }
        }
        public override void ReceiveFail(SocketAsyncEventArgs args)
        {
            if (socket.Connected)
            {
                //Debug.Log("Not Problem, Next Receive Start : " + socket.LocalEndPoint);
                ReceiveAsyncProcess(socket, ReceiveArgs);
            }
            else
            {
                if (!socket.DisconnectAsync(DisconnectArgs))
                    Disconnect(DisconnectArgs);
            }
            MyClient.IsConnect = socket.Connected;
        }
        public override void Send(SocketAsyncEventArgs args)
        {
            base.Send(args);
        }
        public override void Connect(SocketAsyncEventArgs args)
        {
            MyClient.IsConnect = socket.Connected;
            MyClient.ConnectFunction();
            ReceiveAsyncProcess(socket, ReceiveArgs);
        }
        public override void Disconnect(SocketAsyncEventArgs args)
        {
            Clear();
        }

        public void Clear()
        {
            Close();
            MyClient.IsConnect = socket.Connected;
            if (ReceiveArgs != null)
            {
                if (ReceiveArgs.Buffer != null) SNPools.Pool_Byte.Put(ReceiveArgs.Buffer);
                ReceiveArgs.SetBuffer(null, 0, 0);
            }
            if (SendArgs != null)
            {
                if (SendArgs.Buffer != null) SNPools.Pool_Byte.Put(SendArgs.Buffer);
                SendArgs.SetBuffer(null, 0, 0);
            }
        }
    }

    public class Relay<T> where T : class
    {
        Queue<T> queue;
        object LockKey;

        public Relay(int size)
        {
            LockKey = new object();
            queue = new Queue<T>(size);
        }
        public T Get()
        {
            T t = null;
            lock (LockKey)
            {
                if(queue.Count != 0)
                    t = queue.Dequeue();
            }
            return t;
        }

        public void Put(T t)
        {
            lock (LockKey)
            {
                queue.Enqueue(t);
            }
        }
        public void Clear()
        {
            lock (LockKey)
            {
                queue.Clear();
            }
        }
    }

    public class SpaceSystem
    {
        IDGetSetter<SNSpace> GetSetter;
        public float LoopSecond = 1f;
        public bool LoopONOFF = true;
        public bool Open = false;
        public SpaceSystem(int size)
        {
            GetSetter = new IDGetSetter<SNSpace>(size);
            LoopOpen();
        }

        public SNSpace CreateSpace<T>(T element, string Code) where T : class, IServerToss, IServerSpaceLogic
        {
            SNSpace space = new SNSpace(this, Code);
            GetSetter.Add(space);

            if (element != null)
            {
                space.TossProcess = element;
                space.LogicProcess = element;
            }

            return space;
        }
        public SNSpace CreateSpace()
        {
            return CreateSpace<SNServer>(null, Const.GetCode());
        }
        public SNSpace CreateSpace<T>(T element) where T : class, IServerToss, IServerSpaceLogic 
        {
            return CreateSpace(element, Const.GetCode());
        }
        public void GetSpaceList(List<SNSpace> list)
        {
            GetSetter.GetList(list);
        }
        public void LoopOpen()
        {
            if (!Open)
            {
                Open = true;

                Task.Factory.StartNew(async () =>
                {
                    List<SNSpace> SpaceList = new List<SNSpace>();
                    List<SNServerClientSocket> ClientList = new List<SNServerClientSocket>();
                    while (Open)//수정
                    {
                        if (LoopONOFF)
                        {
                            GetSetter.GetList(SpaceList);
                            for (int i = 0; i < SpaceList.Count; i++)
                            {
                                SpaceList[i].GetLists(ClientList);
                                SpaceList[i].LogicProcess.Loop(ClientList);
                            }
                        }
                        await Task.Delay((int)(LoopSecond * 1000));
                    }
                });
            }
        }

        public void LoopClose()
        {
            Open = false;
        }
    }

    public class SNSpace : IID
    {
        public SpaceSystem Parent;
        public IServerToss TossProcess = null;
        public IServerSpaceLogic LogicProcess = null;
        public string MyID { get; set; }
        
        object LockKey;
        List<SNServerClientSocket> Lists;
        public int Count
        {
            get
            {
                int size = -1;
                lock (LockKey)
                {
                    size = Lists.Count;
                }
                return size;
            }
        }

        public SNSpace(SpaceSystem spaceSystem)
        {
            Setting(spaceSystem, Const.GetCode());
        }
        public SNSpace(SpaceSystem spaceSystem, string ID)
        {
            Setting(spaceSystem, ID);
        }

        void Setting(SpaceSystem spaceSystem, string ID)
        {
            Lists = new List<SNServerClientSocket>(1024); // 방 인원수
            LockKey = new object();
            MyID = ID;
            Parent = spaceSystem;
        }

        public void GetLists(List<SNServerClientSocket> lists)
        {
            lock (LockKey)
            {

                lists.Clear();
                for (int i = 0; i < Lists.Count; i++)
                {
                    lists.Add(Lists[i]);
                }
            }
        }

        public void Add(SNServerClientSocket element)
        {
            lock (LockKey)
            {
                Lists.Add(element);
            }
            element.MySpace = this;
            if(TossProcess != null) element.TossProcess.Add(TossProcess);
            LogicProcess?.InClient(element);
        }
        public void Remove(SNServerClientSocket element)
        {
            lock (LockKey)
            {
                if (Lists.Remove(element))
                {
                    element.MySpace = null;
                }
            }
            if (TossProcess != null) element.TossProcess.Remove(TossProcess);
            LogicProcess?.OutClient(element);
        }


        public void Move(SNServerClientSocket element, SNSpace form)
        {
            if (Lists.Contains(element))
            {
                Lists.Remove(element);
            }
            form.Add(element);
        }
    }

    public interface IID
    {
        string MyID { get; set; }
    }

    public class IDGetSetter<T> where T : class, IID
    {
        public Dictionary<string, T> dic = null;
        //public List<T> list = null;
        object LockKey = null;

        public IDGetSetter(int size)
        {
            dic = new Dictionary<string, T>(size);
            LockKey = new object();
        }

        public bool Add(T element)
        {
            bool result_TF = true;
            lock (LockKey)
            {
                if ((result_TF = !dic.ContainsKey(element.MyID)))
                    dic.Add(element.MyID, element);
            }
            return result_TF;
        }
        public T Get(string ID)
        {
            T result = null;

            lock (LockKey)
            {
                dic.TryGetValue(ID, out result);
            }

            return result;
        }
        public bool Remove(T element)
        {
            bool result_TF = false;
            lock (LockKey)
            {
                result_TF = dic.Remove(element.MyID);
            }
            return result_TF;
        }

        public void GetList(List<T> lists)
        {
            lists.Clear();
            lock (LockKey)
            {
                foreach (var now in dic)
                {
                    lists.Add(now.Value);
                }
            }
        }
    }

    public class SNServer : IServerToss, IServerSpaceLogic
    {
        SNServerSocket socketBase;
        public SNSocketSetting SocketSetting
        {
            get
            {
                return socketBase.state;
            }
        }

        public SpaceSystem spaceSystem = null;//스페이스 시스템분리가능성

        public SNSpace DefualtSpace;
        public IServerToss GlobalTossProcess = null;

        public SNServer()
        {

        }

        public void Start()
        {
            spaceSystem = spaceSystem ?? new SpaceSystem(4096);
            DefualtSpace = DefualtSpace ?? spaceSystem.CreateSpace(this);

            socketBase.MyServer = this;
            switch (SocketSetting.type)
            {
                case SocketType.Tcp:
                {
                    socketBase.socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
                    socketBase.socket.Bind(socketBase.endPoint = new IPEndPoint(IPAddress.Any, socketBase.state.port));
                    socketBase.socket.Listen(10);
                    if (!socketBase.socket.AcceptAsync(socketBase.AcceptArgs))
                        socketBase.Accept(socketBase.AcceptArgs);
                    break;
                }
                case SocketType.Udp:
                {
                    socketBase.socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, ProtocolType.Udp);
                    socketBase.socket.Bind(socketBase.endPoint = new IPEndPoint(IPAddress.Any, socketBase.state.port));
                    //리시브
                    break;
                }
            }
        }
        public void Setting(int port, SocketType type)
        {
            if (socketBase == null)
                socketBase = new SNServerSocket()
                {
                    state = new SNSocketSetting(),
                    MyServer = this
                };

            socketBase.state.SettingLogic(port, type);
        }

        public void Close()
        {
            if (spaceSystem != null)
            {
                spaceSystem.LoopClose();
            }
            List<SNSpace> SpaceList = new List<SNSpace>();
            List<SNServerClientSocket> ClientList = new List<SNServerClientSocket>();
            //Debug.Log("Server Close");
            spaceSystem.GetSpaceList(SpaceList);
            for (int i = 0; i < SpaceList.Count; i++)
            {
                SpaceList[i].GetLists(ClientList);
                for (int j = 0; j < ClientList.Count; j++)
                {
                    ClientList[i].Clear();
                }
            }
            //socketBase.socket.Shutdown(SocketShutdown.Both);
            socketBase.socket.Close();
        }

        public void DataToss(SNServerClientSocket NowSocket, SNBuffer Read, SNBuffer My, SNBuffer Other)
        {
            
        }

        public void CreateSpace()
        {

        }

        public void InClient(SNServerClientSocket Client)
        {

        }

        public void OutClient(SNServerClientSocket Out)
        {

        }

        public void Loop(List<SNServerClientSocket> Lists)
        {

        }
    }

    public class SNServerSocket : SNSocketBase
    {
        public SNServer MyServer;

        public SNServerSocket()
        {
            AsyncEventConnect(out AcceptArgs);
        }

        public override void Accept(SocketAsyncEventArgs args)
        {
            Socket client_socket = args.AcceptSocket;
            args.AcceptSocket = null;
            SNServerClientSocket client = new SNServerClientSocket();
            client.socket = client_socket;
            if(MyServer.GlobalTossProcess != null) client.TossProcess.Add(MyServer.GlobalTossProcess);
            MyServer.DefualtSpace.Add(client);
            client.ReceiveAsyncProcess(client.socket, client.ReceiveArgs);
            if (!socket.AcceptAsync(args))
                Accept(args);
        }
        public override void Receive(SocketAsyncEventArgs args)
        {

        }
        public void Clear()
        {
            ReceiveArgs.SetBuffer(null, 0, 0);
            SendArgs.SetBuffer(null, 0, 0);

            AsyncEventDisconnect(ref AcceptArgs);
        }
    }

    public class SNServerClientSocket : SNSocketBase
    {
        public SNPacketReader Reader;
        public SNBuffer MyBuffer;
        public SNBuffer OtherBuffer;
        public Queue<SNBuffer> BufferQueue;
        public SNSpace MySpace;
        public List<IServerToss> TossProcess;

        List<SNServerClientSocket> MySpaceList;

        public SNServerClientSocket()
        {
            state = new SNSocketSetting();
            Reader = new SNPacketReader();
            MySpaceList = new List<SNServerClientSocket>(1024);
            TossProcess = new List<IServerToss>(1024);
            BufferQueue = new Queue<SNBuffer>(1024);
            AsyncEventConnect(out ReceiveArgs);
            AsyncEventConnect(out SendArgs);
            AsyncEventConnect(out DisconnectArgs);
            SNArray<byte> TempArray;
            byte[] TempByte;
            TempArray = SNPools.Pool_Array.Get();
            TempArray.Setting(SNPools.Pool_Byte.Get());
            (MyBuffer = SNPools.Pool_Buffer.Get()).Setting(TempArray);

            TempArray = SNPools.Pool_Array.Get();
            TempArray.Setting(SNPools.Pool_Byte.Get());
            (OtherBuffer = SNPools.Pool_Buffer.Get()).Setting(TempArray);

            TempByte = SNPools.Pool_Byte.Get();
            SendArgs.SetBuffer(TempByte, 0, TempByte.Length);
        }

        ~SNServerClientSocket()
        {
            //Debug.Log("SNServerClientSocket : 메모리 제거");
        }

        public override void Receive(SocketAsyncEventArgs args)
        {
            if (args.BytesTransferred > 0)
            {
                SNArray<byte> ReadArray;
                (ReadArray = SNPools.Pool_Array.Get()).Setting(args.Buffer, args.BytesTransferred);
                Reader.Read(ReadArray, BufferQueue);
                MyBuffer.PointClear();
                OtherBuffer.PointClear();
                while (BufferQueue.Count != 0)
                {
                    SNBuffer buffer = BufferQueue.Dequeue();
                    buffer.PosClear();
                    int Count = TossProcess.Count;
                    try
                    {
                        for (int i = 0; i < Count; i++)
                        {
                            TossProcess[i].DataToss(this, buffer, MyBuffer, OtherBuffer);
                        }
                    }
                    catch (Exception e)
                    {
                        //Debug.LogError(e);
                    }
                    SNPools.Pool_Buffer.Put(buffer);
                }
                MyBuffer.PosClear();
                OtherBuffer.PosClear();
                if (MyBuffer.EndPoint != 0)
                {
                    try
                    {
                        this.SendProcess(MyBuffer);
                    }
                    finally { }
                }
                if (OtherBuffer.EndPoint != 0)
                {
                    MySpace.GetLists(MySpaceList);
                    int i = MySpaceList.Count - 1;
                    while (i >= 0)
                    {
                        try
                        {
                            for (; i >= 0; i--)
                            {
                                if (MySpaceList[i] != this)
                                    MySpaceList[i].SendProcess(OtherBuffer);
                            }
                        }
                        catch (Exception)
                        {
                            i--;
                        }
                    }
                    MySpaceList.Clear();
                }
                ReceiveAsyncProcess(socket, args);
            }
            else
            {
                Disconnect(DisconnectArgs);
            }
        }

        public override void ReceiveFail(SocketAsyncEventArgs args)
        {
            if (socket.Connected)
            {
                if (!socket.ReceiveAsync(ReceiveArgs))
                    Receive(ReceiveArgs);
            }
            else
            {
                if (!socket.DisconnectAsync(DisconnectArgs))
                    Disconnect(DisconnectArgs);
            }
        }

        public override void Send(SocketAsyncEventArgs args)
        {
            
        }

        public override void Disconnect(SocketAsyncEventArgs args)
        {
            Clear();
        }

        public void SpaceChange(SNSpace from)
        {
            MySpace.Move(this, from);
        }

        public void Clear()
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
            socket.Close();
            if (MySpace != null)
                MySpace.Remove(this);
            if (MyBuffer != null) SNPools.Pool_Buffer.Put(MyBuffer);
            if (OtherBuffer != null) SNPools.Pool_Buffer.Put(OtherBuffer);
            ReceiveArgs.SetBuffer(null, 0, 0);
            SendArgs.SetBuffer(null, 0, 0);
            AsyncEventDisconnect(ref ReceiveArgs);
            AsyncEventDisconnect(ref SendArgs);
            AsyncEventDisconnect(ref DisconnectArgs);
        }
    }
    public class SNSocketSetting
    {
        public SocketType type;
        public string ip;
        public int port;

        public virtual void SettingLogic(string ip, int port, SocketType type)
        {
            this.ip = ip;
            this.port = port;
            this.type = type;
        }
        public virtual void SettingLogic(string ip, int port)
        {
            this.ip = ip;
            this.port = port;
        }
        public virtual void SettingLogic(int port, SocketType type)
        {
            this.port = port;
            this.type = type;
        }
    }


    public static class SNPools
    {
        static SNPool<byte[]> _pool_byte;
        static SNPool<SNArray<byte>> _pool_array;
        static SNPool<SNBuffer> _pool_buffer;
        static SNPool<SocketAsyncEventArgs> _pool_socketArgs;

        public static SNPool<byte[]> Pool_Byte
        {
            get
            {
                if (_pool_byte == null)
                {
                    _pool_byte = new SNPool<byte[]>(65536)
                    {
                        NewSetting = () =>
                        {
                            return new byte[8192];
                        },
                    };
                }
                return _pool_byte;
            }
        }
        public static SNPool<SNArray<byte>> Pool_Array
        {
            get
            {
                if (_pool_byte == null)
                {
                    _pool_array = new SNPool<SNArray<byte>>(65536)
                    {
                        NewSetting = () =>
                        {
                            return new SNArray<byte>();
                        },
                        PutSetting = (SNArray<byte> t) =>
                        {
                            if (t.array != null)
                                Pool_Byte.Put(t.array);
                            t.Clear();
                        },
                    };
                }
                return _pool_array;
            }
        }
        public static SNPool<SNBuffer> Pool_Buffer
        {
            get
            {
                if (_pool_buffer == null)
                {
                    _pool_buffer = new SNPool<SNBuffer>(65536)
                    {
                        NewSetting = () =>
                        {
                            return new SNBuffer();
                        },
                        PutSetting = (SNBuffer t) =>
                        {
                            if (t.Array != null)
                                Pool_Array.Put(t.Array);
                            t.Clear();
                        },
                    };
                }
                return _pool_buffer;
            }
        }
        public static SNPool<SocketAsyncEventArgs> Pool_SocketArgs
        {
            get
            {
                if (_pool_socketArgs == null)
                {
                    _pool_socketArgs = new SNPool<SocketAsyncEventArgs>(2048)
                    {
                        NewSetting = () =>
                        {
                            return new SocketAsyncEventArgs();
                        },
                        PutSetting = (SocketAsyncEventArgs t) =>
                        {
                            //t.Completed.
                        },
                    };
                }
                return _pool_socketArgs;
            }
        }
    }

    public class SNArray<T>
    {
        public T[] array;
        public int EndPoint = 0;
        public int LengthPoint = 0;

        public int EtoLSize
        {
            get
            {
                return LengthPoint - EndPoint;
            }
        }

        public SNArray()
        {
            Clear();
        }

        public T this[int i]
        {
            get { return array[i]; }
            set
            {
                if (i >= EndPoint)
                    EndPoint = i + 1;
                array[i] = value;
            }
        }

        public void Setting(T[] array)
        {
            Setting(array, 0);
        }
        public void Setting(T[] array, int EndPoint)
        {
            this.array = array;
            this.EndPoint = EndPoint;
            this.LengthPoint = array.Length;
        }

        public void CopyData(T[] data, int offset, int length)
        {
            EndPoint = 0;

            for (int i = 0; i < length; i++)
            {
                this[i - offset] = data[offset + i];
            }
        }
        public void CopyArray(SNArray<T> data)
        {
            EndPoint = 0;
            int length = data.EndPoint;
            for (int i = 0; i < length; i++)
            {
                this[i] = data[i];
            }
        }

        public void PointClear()
        {
            EndPoint = 0;
        }

        public void Clear()
        {
            array = null;
            EndPoint = 0;
            LengthPoint = 0;
        }

        public static implicit operator T[](SNArray<T> array)
        {
            return array.array;
        }
    }

    public class SNPacketReader
    {
        int HeaderSize;
        public SNBufferVar ReadBuffer;
        public SNArray<byte> HeaderArray;
        public SNBuffer HeaderBuffer;

        public const short CheckCode = 0x1234;

        int Pattern = 1;
        int BodyLength = 0;
        bool LoopAble = true;

        public SNPacketReader()
        {
            HeaderSize = 4;
            ReadBuffer = new SNBufferVar(65536);
            (HeaderArray = SNPools.Pool_Array.Get()).Setting(SNPools.Pool_Byte.Get());
            (HeaderBuffer = SNPools.Pool_Buffer.Get()).Setting(HeaderArray);
        }

        public void Read(SNArray<byte> array, Queue<SNBuffer> queue)
        {
            ReadBuffer.PutArray(array);//이부분이 수정될 예정
            //Debug.Log("-- 루프 --");
            LoopAble = true;

            while (LoopAble)
            {
                switch (Pattern)
                {
                    case 1 << 0: // Header Check
                    {
                        if (ReadBuffer.IsGetAble(2))
                        {
                            HeaderBuffer.PosClear();
                            ReadBuffer.GetArray(HeaderArray, 0, 2);
                            Pattern = (CheckCode == HeaderBuffer.GetShort()) ? (Pattern << 1) : (1 << 3);
                        }
                        else
                        {
                            LoopAble = false;
                        }
                        break;
                    }
                    case 1 << 1: // Header Size
                    {
                        if (ReadBuffer.IsGetAble(HeaderSize))
                        {
                            HeaderBuffer.PosClear();
                            ReadBuffer.GetArray(HeaderArray, 0, HeaderSize);
                            BodyLength = HeaderBuffer.GetInt();
                            Pattern <<= 1;
                        }
                        else
                        {
                            LoopAble = false;
                        }
                        break;
                    }
                    case 1 << 2: // Body
                    {
                        if (ReadBuffer.IsGetAble(BodyLength))
                        {
                            SNBuffer BodyBuffer = SNPools.Pool_Buffer.Get();
                            SNArray<byte> BodyArray = SNPools.Pool_Array.Get();
                            BodyArray.Setting(SNPools.Pool_Byte.Get());
                            BodyBuffer.Setting(BodyArray);
                            BodyBuffer.Position = 0;
                            ReadBuffer.GetArray(BodyArray, 0, BodyLength);

                            queue.Enqueue(BodyBuffer);

                            Pattern = 1;
                        }
                        else
                        {
                            LoopAble = false;
                        }
                        break;
                    }
                    case 1 << 3: // Error
                    {
                        Console.WriteLine("Error");
                        while (true)
                        {
                            if (ReadBuffer.IsGetAble(HeaderSize))
                            {
                                //Debug.Log(ReadBuffer.Remain());
                                ReadBuffer.GetArray(HeaderArray, 0, HeaderSize);
                                HeaderBuffer.Position = 0;
                                short Data = HeaderBuffer.GetShort();
                                if (Data == CheckCode)
                                {
                                    Pattern = 1;
                                    ReadBuffer.Position -= 2;
                                    break;
                                }
                                else
                                {
                                    ReadBuffer.Position--;
                                }
                            }
                            else
                            {
                                LoopAble = false;
                            }
                        }
                        break;
                    }
                }
            }
        }
    }

    public class SNBuffer
    {
        SNDataChange change;
        public int Position = 0;
        public int EndPoint
        {
            get
            {
                return Array.EndPoint;
            }
            set
            {
                Array.EndPoint = value;
            }
        }
        public SNArray<byte> Array;

        public SNBuffer()
        {
            change = new SNDataChange();

        }

        public void Setting(SNArray<byte> Array)
        {
            this.Array = Array;
        }

        public void Clear()
        {
            Array = null;
            Position = 0;
        }
        public void PointClear()
        {
            EndPoint = 0;
            Position = 0;
        }
        public void PosClear()
        {
            Position = 0;
        }

        public void Start(out int index)
        {
            PutShort(0x1234);
            index = Position;
            PutInt(0);
        }
        public void End(int index)
        {
            int NowPoint = Position;
            int Size = NowPoint - (index + 4);
            Position = index;
            PutInt(Size);
            Position = NowPoint;
        }

        public void PutLong(long l)
        {
            change.PutLong(l, Array, Position);
            Position += 8;
        }
        public void PutInt(int i)
        {
            change.PutInt(i, Array, Position);
            Position += 4;
        }
        public void PutShort(short s)
        {
            change.PutShort(s, Array, Position);
            Position += 2;
        }
        public void PutByte(byte b)
        {
            change.PutByte(b, Array, Position);
            Position ++;
        }
        public void PutFloat(float f)
        {
            change.PutFloat(f, Array, Position);
            Position += 4;
        }
        public void PutDouble(double d)
        {
            change.PutDouble(d, Array, Position);
            Position += 8;
        }
        public void PutString(string s)
        {
            int size;
            change.PutString(s, Array, Position, out size);
            Position += size;
        }
        public void PutBool(bool b)
        {
            PutByte((byte)(b ? 1 : 0));
        }

        public void PutArray(SNArray<byte> data, int offset, int size)
        {
            int length;
            change.MoveArray(data, offset, size, Array, Position, out length);
            Position += length;
        }

        public void PutBuffer(SNBuffer data, int size)
        {
            int length;
            change.MoveArray(data.Array, data.Position, size, Array, Position, out length);
            Position += length;
            data.Position += length;
        }

        public long GetLong()
        {
            long l = change.GetLong(Array, Position);
            Position += 8;
            return l;
        }
        public int GetInt()
        {
            int i = change.GetInt(Array, Position);
            Position += 4;
            return i;
        }
        public short GetShort()
        {
            short s = change.GetShort(Array, Position);
            Position += 2;
            return s;
        }
        public byte GetByte()
        {
            byte b = change.Getbyte(Array, Position);
            Position ++;
            return b;
        }
        public double GetDouble()
        {
            double d = change.GetDouble(Array, Position);
            Position += 8;
            return d;
        }
        public float GetFloat()
        {
            float f = change.GetFloat(Array, Position);
            Position += 4;
            return f;
        }
        public string GetString()
        {
            int size = 0;
            string s = change.GetString(Array, Position, out size);
            Position += size;
            return s;
        }

        public SNArray<byte> GetArray(int size)
        {
            SNArray<byte> array = SNPools.Pool_Array.Get();
            array.Setting(SNPools.Pool_Byte.Get());
            int length;
            change.MoveArray(Array, Position, size, array, 0, out length);
            Position += length;

            return array;
        }

        public bool GetBool()
        {
            return GetByte() == 1 ? true : false;
        }

        public void PutB2BByte(SNBuffer buffer)
        { change.PutB2B(this, buffer, 1); }
        public void PutB2BShort(SNBuffer buffer)
        { change.PutB2B(this, buffer, 2); }
        public void PutB2BInt(SNBuffer buffer)
        { change.PutB2B(this, buffer, 4); }
        public void PutB2BLong(SNBuffer buffer)
        { change.PutB2B(this, buffer, 8); }
        public void PutB2BFloat(SNBuffer buffer)
        { change.PutB2B(this, buffer, 4); }
        public void PutB2BDouble(SNBuffer buffer)
        { change.PutB2B(this, buffer, 8); }
        public void PutB2BVar(SNBuffer buffer, int size)
        { change.PutB2B(this, buffer, size); }
        public void PutB2BBool(SNBuffer buffer)
        { change.PutB2B(this, buffer, 1); }
        public void PutB2BString(SNBuffer buffer)
        { change.PutB2BString(this, buffer, 0x00); }
    }

    public class SNBufferVar
    {
        public object LockKey;
        public SNArray<byte>[] Arrays;

        public int Position = 0;
        public int ArraysCount = 0;
        public int LastIndex
        {
            get
            {
                return ArraysCount - 1;
            }
        }

        SNDataChange change;

        public int Size
        {
            get
            {
                int size = 0;
                int length = ArraysCount;
                for (int i = 0; i < length; i++)
                {
                    size += Arrays[i].EndPoint;
                }
                return size;
            }
        }

        public SNBufferVar(int size)
        {
            Arrays = new SNArray<byte>[size];
            LockKey = new object();
            change = new SNDataChange();
        }

        public byte Get(int Pos)
        {
            int length = ArraysCount;
            for (int i = 0; i < length; i++)
            {
                if (Pos < Arrays[i].EndPoint)
                    return Arrays[i][Pos];
                Pos -= Arrays[i].EndPoint;
            }
            return 0;
        }
        public int Remain()
        {
            return Remain(Position);
        }
        public int Remain(int Pos)
        {
            return Size - Pos;
        }
        public bool IsGetAble(int Length)
        {
            return IsGetAble(Position, Length);
        }
        public bool IsGetAble(int Pos, int Length)
        {
            return Pos + Length <= Size;
        }
        public void Shift()
        {
            int length = ArraysCount;
            for (int i = 0; i < length; i++)
            {
                if (Position < Arrays[i].EndPoint)
                    return;
                Position -= Arrays[i].EndPoint;
                RemoveArrayAt(0);
            }
        }
        public void RemoveArrayAt(int index)
        {
            int length = ArraysCount;
            SNPools.Pool_Array.Put(Arrays[index]);
            for (int i = index+1; i < length; i++)
            {
                Arrays[i - 1] = Arrays[i];
            }
            ArraysCount--;
        }

        public void PutArray(SNArray<byte> array)
        {
            Arrays[ArraysCount++] = array;
        }

        public void GetArray(SNArray<byte> array, int offset, int length)
        {
            for (int i = 0; i < length; i++)
            {
                array[i + offset] = Get(Position ++);
            }
            Shift();
        } 

    }

    public class SNPool<T> where T : class
    {
        Queue<T> queue;

        public int Count
        {
            get
            {
                return queue.Count;
            }
        }
        public object LockKey;

        public Func<T> NewSetting = () => { return null; };
        public Action<T> GetSetting = (T t) => { };
        public Action<T> PutSetting = (T t) => { };

        public SNPool(int size)
        {
            LockKey = new object();
            queue = new Queue<T>(size);
        }

        public T Get()
        {
            T element = null;
            lock (LockKey)
            {
                if (queue.Count != 0)
                    element = queue.Dequeue();
            }
            if (element == null)
                element = NewSetting();
            GetSetting(element);
            return element;
        }
        public void Put(T element)
        {
            PutSetting(element);
            lock (LockKey)
            {
                queue.Enqueue(element);
            }
        }
    }


    public class SNDataChange
    {
        public static Endian endian = Endian.Little;

        float[] F = { 0.0f };
        int[] I = { 0 };
        double[] D = { 0.0f };
        long[] L = { 0 };

        public void PutLong(long data, SNArray<byte> array, int offset)
        {
            var _data = data;
            /*
            byte b;
            int length = 8;
            for (int i = 0; i < length; i++)
            {
                b = (byte)(_data & 0xFF);
                array[offset + ((endian == Endian.Little) ? i : ((length - 1) - i))] = b;
                _data = _data >> 8;
            }
            */
            if (endian == Endian.Little)
            {
                array[offset] = (byte)((_data) & 0xff);
                array[offset + 1] = (byte)((_data >> 8) & 0xff);
                array[offset + 2] = (byte)((_data >> 16) & 0xff);
                array[offset + 3] = (byte)((_data >> 24) & 0xff);
                array[offset + 4] = (byte)((_data >> 32) & 0xff);
                array[offset + 5] = (byte)((_data >> 40) & 0xff);
                array[offset + 6] = (byte)((_data >> 48) & 0xff);
                array[offset + 7] = (byte)((_data >> 56) & 0xff);
            }
            else
            {
                array[offset + 7] = (byte)((_data) & 0xff);
                array[offset + 6] = (byte)((_data >> 8) & 0xff);
                array[offset + 5] = (byte)((_data >> 16) & 0xff);
                array[offset + 4] = (byte)((_data >> 24) & 0xff);
                array[offset + 3] = (byte)((_data >> 32) & 0xff);
                array[offset + 2] = (byte)((_data >> 40) & 0xff);
                array[offset + 1] = (byte)((_data >> 48) & 0xff);
                array[offset] = (byte)((_data >> 56) & 0xff);
            }
        }

        public void PutInt(int data, SNArray<byte> array, int offset)
        {
            var _data = data;
            /*
            byte b;
            int length = 4;
            for (int i = 0; i < length; i++)
            {
                b = (byte)(_data & 0xFF);
                array[offset + ((endian == Endian.Little) ? i : ((length - 1) - i))] = b;
                _data = _data >> 8;
            }
            */
            if (endian == Endian.Little)
            {
                array[offset] = (byte)((_data) & 0xff);
                array[offset + 1] = (byte)((_data >> 8) & 0xff);
                array[offset + 2] = (byte)((_data >> 16) & 0xff);
                array[offset + 3] = (byte)((_data >> 24) & 0xff);
            }
            else
            {
                array[offset + 3] = (byte)((_data) & 0xff);
                array[offset + 2] = (byte)((_data >> 8) & 0xff);
                array[offset + 1] = (byte)((_data >> 16) & 0xff);
                array[offset + 0] = (byte)((_data >> 24) & 0xff);
            }
        }

        public void PutShort(short data, SNArray<byte> array, int offset)
        {
            var _data = data;
            /*
            byte b;
            int length = 2;
            for (int i = 0; i < length; i++)
            {
                b = (byte)(_data & 0xFF);
                array[offset + ((endian == Endian.Little) ? i : ((length - 1) - i))] = b;
                _data = (short)(_data >> 8);
            }
            */
            if (endian == Endian.Little)
            {
                array[offset] = (byte)((_data) & 0xff);
                array[offset + 1] = (byte)((_data >> 8) & 0xff);
            }
            else
            {
                array[offset + 1] = (byte)((_data) & 0xff);
                array[offset] = (byte)((_data >> 8) & 0xff);
            }
        }

        public void PutByte(byte data, SNArray<byte> array, int offset)
        {
            array[offset] = data;
        }

        public void PutFloat(float data, SNArray<byte> array, int offset)
        {
            F[0] = data;
            Buffer.BlockCopy(F, 0, I, 0, 4);
            PutInt(I[0], array, offset);
        }

        public void PutDouble(double data, SNArray<byte> array, int offset)
        {
            D[0] = data;
            Buffer.BlockCopy(D, 0, L, 0, 8);
            PutLong(L[0], array, offset);
        }

        public void PutString(string s, SNArray<byte> array, int offset, out int length)
        {
            length = Encoding.UTF8.GetBytes(s, 0, s.Length, array.array, offset);
            PutByte(0x00, array, offset + length);
            length+=1;
        }

        public void MoveArray(SNArray<byte> from, int FromOffset, int FromSize, SNArray<byte> to, int ToOffset, out int length)
        {
            for (int i = 0; i < FromSize; i++)
            {
                to[ToOffset + i] = from[FromOffset + i];
            }
            length = FromSize;
        }

        //--------------------------------------Get-------------------------------------

        public long GetLong(SNArray<byte> array, int offset)
        {
            long _data = 0;
            /*
            int length = 8;
            for (int i = 0; i < length; i++)
            {
                _data = _data << 8;
                _data = _data | array[offset + ((endian == Endian.Little) ? ((length - 1) - i) : i)];
            }
            */
            if (endian == Endian.Little)
            {
                _data = (_data | ((long)array[offset + 7] << 56));
                _data = (_data | ((long)array[offset + 6] << 48));
                _data = (_data | ((long)array[offset + 5] << 40));
                _data = (_data | ((long)array[offset + 4] << 32));
                _data = (_data | ((long)array[offset + 3] << 24));
                _data = (_data | ((long)array[offset + 2] << 16));
                _data = (_data | ((long)array[offset + 1] << 8));
                _data = (_data | array[offset]);
            }
            else
            {
                _data = (_data | ((long)array[offset] << 56));
                _data = (_data | ((long)array[offset + 1] << 48));
                _data = (_data | ((long)array[offset + 2] << 40));
                _data = (_data | ((long)array[offset + 3] << 32));
                _data = (_data | ((long)array[offset + 4] << 24));
                _data = (_data | ((long)array[offset + 5] << 16));
                _data = (_data | ((long)array[offset + 6] << 8));
                _data = (_data | array[offset + 7]);
            }
            return _data;
        }
        public int GetInt(SNArray<byte> array, int offset)
        {
            int _data = 0;
            /*
            int length = 4;
            for (int i = 0; i < length; i++)
            {
                 _data = _data << 8;
                _data = _data | array[offset + ((endian == Endian.Little) ? ((length - 1) - i) : i)];
            }
            */
            if (endian == Endian.Little)
            {
                _data = (_data | (array[offset + 3] << 24));
                _data = (_data | (array[offset + 2] << 16));
                _data = (_data | (array[offset + 1] << 8));
                _data = (_data | array[offset + 0]);
            }
            else
            {
                _data = (_data | (array[offset + 0] << 24));
                _data = (_data | (array[offset + 1] << 16));
                _data = (_data | (array[offset + 2] << 8));
                _data = (_data | array[offset + 3]);
            }
            
            return _data;
        }
        public short GetShort(SNArray<byte> array, int offset)
        {
            int _data = 0;
            /*
            int length = 2;
            for (int i = 0; i < length; i++)
            {
                _data = (short)(_data << 8);
                _data = (short)(_data | array[offset + ((endian == Endian.Little) ? ((length - 1) - i) : i)]);
            }
            */
            if (endian == Endian.Little)
            {
                _data = (_data | (array[offset + 1] << 8));
                _data = (_data | array[offset + 0]);
            }
            else
            {
                _data = (_data | (array[offset + 0] << 8));
                _data = (_data | array[offset + 1]);
            }
            return (short)_data;
        }
        public byte Getbyte(SNArray<byte> array, int offset)
        {
            return array[offset];
        }

        public float GetFloat(SNArray<byte> array, int offset)
        {
            I[0] = GetInt(array, offset);
            Buffer.BlockCopy(I, 0, F, 0, 4);
            return F[0];
        }
        public double GetDouble(SNArray<byte> array, int offset)
        {
            L[0] = GetLong(array, offset);
            Buffer.BlockCopy(L, 0, D, 0, 8);
            return D[0];
        }

        public string GetString(SNArray<byte> array, int offset, out int size)
        {
            size = 0;
            byte[] b = array.array;
            for (size = offset; size < array.EndPoint; size++)
            {
                if (b[size] == 0x00)
                    break;
            };
            size = (size + 1) - offset;

            return Encoding.UTF8.GetString(array.array, offset, size - 1);
        }

        // -----------------------------------------
        public void PutB2B(SNBuffer to, SNBuffer from, int size)
        {
            for (int i = 0; i < size; i++)
            {
                to.Array[to.Position++] = from.Array[from.Position++];
            }
        }
        public void PutB2BString(SNBuffer to, SNBuffer from, byte data)
        {
            for (int i = from.Position; i < from.EndPoint; i++)
            {
                to.Array[to.Position++] = from.Array[from.Position++];
                if (from.Array[i] == data)
                    break;
            }
        }
    }

    public enum SocketType
    {
        Tcp,
        Udp
    }

    public enum Endian
    {
        Little,
        Big
    }

    public interface IServerToss
    {
        void DataToss(SNServerClientSocket NowSocket, SNBuffer Read, SNBuffer My, SNBuffer Other);
    }

    public interface IServerSpaceLogic
    {
        void CreateSpace();
        void InClient(SNServerClientSocket clientIn);
        void OutClient(SNServerClientSocket clientOut);
        void Loop(List<SNServerClientSocket> clients);
    }

    public interface IStandard
    {
        void Clear();
    }

    public interface IPooling
    {
        void Setting();
    }

    public static class Const
    {
        static StringBuilder SB = null;
        static char[] A = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
        static char[] a = { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z' };
        static char[] num = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        static List<IpInfo> lists = null;
        static System.Random random;
        public static string GetCode()
        {
            if (SB == null)
                SB = new StringBuilder(1024);
            if (random == null)
                random = new System.Random();
            SB.Clear();
            for (int i = 0; i < 16; i++)
            {
                int NowNum = random.Next(0, 3);
                char[] NowChar = null;
                if (NowNum == 0)
                    NowChar = A;
                if (NowNum == 1)
                    NowChar = a;
                if (NowNum == 2)
                    NowChar = num;
                SB.Append(NowChar[random.Next(0, NowChar.Length)]);
            }
            return SB.ToString();
        }

        public static string GetIP()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            string ipAddr = string.Empty;
            for (int i = 0; i < host.AddressList.Length; i++)
            {
                if (host.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    ipAddr = host.AddressList[i].ToString();
                }
            }
            return ipAddr;
        }
        public static string GetBroadcastIP(string ip, string sub)
        {
            string BIP = string.Empty;
            string[] ipCut = ip.Split('.');
            string[] subCut = sub.Split('.');
            int[] ipNum = new int[ipCut.Length];
            int[] subNum = new int[ipCut.Length];
            for (int i = 0; i < ipCut.Length; i++)
                ipNum[i] = int.Parse(ipCut[i]);
            for (int i = 0; i < subCut.Length; i++)
                subNum[i] = int.Parse(subCut[i]);
            int subnetHostCount = 0;
            int subnetNetCount = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((subNum[i / 8] & (1 << (7 - (i % 8)))) != 0)
                {
                    subnetHostCount++;
                }
                else
                {
                    subnetNetCount++;
                }
            }
            // 102 / 64
            int NetRange = (256 / ((int)Math.Pow(2, (subnetHostCount % 8))));
            int LastPos = NetRange * ((int)(ipNum[(int)(subnetHostCount / 8)] / NetRange));

            ipNum[(int)(subnetHostCount / 8)] = (LastPos + (NetRange - 1));
            for (int i = ((subnetHostCount / 8) + 1); i < 4; i++)
                ipNum[i] = 255;

            return ipNum[0] + "." + ipNum[1] + "." + ipNum[2] + "." + ipNum[3];
        }
        public static string GetGatewayIP(string ip, string sub)
        {
            string BIP = string.Empty;
            string[] ipCut = ip.Split('.');
            string[] subCut = sub.Split('.');
            int[] ipNum = new int[ipCut.Length];
            int[] subNum = new int[ipCut.Length];
            for (int i = 0; i < ipCut.Length; i++)
                ipNum[i] = int.Parse(ipCut[i]);
            for (int i = 0; i < subCut.Length; i++)
                subNum[i] = int.Parse(subCut[i]);
            int subnetHostCount = 0;
            int subnetNetCount = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((subNum[i / 8] & (1 << (7 - (i % 8)))) != 0)
                {
                    subnetHostCount++;
                }
                else
                {
                    subnetNetCount++;
                }
            }
            // 102 / 64
            int NetRange = (256 / ((int)Math.Pow(2, (subnetHostCount % 8))));
            int LastPos = NetRange * ((int)(ipNum[(int)(subnetHostCount / 8)] / NetRange));

            ipNum[(int)(subnetHostCount / 8)] = LastPos;
            for (int i = ((subnetHostCount / 8) + 1); i < 4; i++)
                ipNum[i] = 0;
            ipNum[3] += 1;
            return ipNum[0] + "." + ipNum[1] + "." + ipNum[2] + "." + ipNum[3];
        }

        public static IpInfo[] GetIpinfo()
        {
            NetworkInterface[] netInter = NetworkInterface.GetAllNetworkInterfaces();
            lists = lists ?? new List<IpInfo>(64);
            for (int i = 0; i < netInter.Length; i++)
            {
                if (netInter[i].OperationalStatus == OperationalStatus.Up)
                {
                    IPInterfaceProperties properties = netInter[i].GetIPProperties();
                    foreach (var tempinfo in properties.UnicastAddresses)
                    {
                        long BitMask = 0;
                        int index = 0;
                        foreach (var mask in tempinfo.IPv4Mask.GetAddressBytes())
                        {
                            BitMask |= (long)mask << (8 * index);
                            index ++;
                        }
                        if (BitMask == 0) continue;
                        if (tempinfo.Address.ToString().Equals("127.0.0.1")) continue;
                        lists.Add(new IpInfo(tempinfo.Address.ToString(), tempinfo.IPv4Mask.ToString()));
                    }
                }
            }
            IpInfo[] info = new IpInfo[lists.Count];
            for (int i = 0; i < lists.Count; i++)
                info[i] = lists[i];
            return info;
        }
    }

    public struct IpInfo
    {
        public string Ipv4;
        public string SubNet;
        public string Broadcast;
        public string Gateway;
        public IpInfo(string Ipv4, string SubNet)
        {
            this.Ipv4 = Ipv4;
            this.SubNet = SubNet;
            Broadcast = Const.GetBroadcastIP(Ipv4, SubNet);
            Gateway = Const.GetGatewayIP(Ipv4, SubNet);
        }
    }


    namespace NetPackage
    {
        public class ObjectChange
        {
            System.Runtime.Serialization.Formatters.Binary.BinaryFormatter formatter;
            System.IO.MemoryStream stream;

            public ObjectChange()
            {
                formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                stream = new System.IO.MemoryStream(1024);
            }

            /// <summary>
            /// 오브젝트를 SNArray<byte>에 담는 기능
            /// </summary>
            /// <param name="data">오브젝트</param>
            /// <param name="array">반환 받을 SNArray</param>
            /// <code>
            /// ObjectChange change = new ObjectChange();
            /// SNArray<byte> array;
            /// change.Change_O2D(data, out array);
            /// </code>
            /// <returns>변환 성공 여부</returns>
            public bool Change_O2D(object data, out SNArray<byte> array)
            {
                stream.Position = 0;
                try
                {
                    formatter.Serialize(stream, data);
                }
                catch (Exception)
                {
                    array = null;
                    return false;
                }
                int length = (int)stream.Position;
                stream.Position = 0;
                array = SNPools.Pool_Array.Get();
                array.Setting(SNPools.Pool_Byte.Get());
                array.PointClear();
                for (int i = 0; i < length; i++)
                {
                    array[i] = (byte)stream.ReadByte();
                }

                return true;
            }
            public object Change_D2O(SNArray<byte> array)
            {
                stream.Position = 0;
                int length = array.EndPoint;
                stream.Write(array.array,0,length);
                stream.Position = 0;
                object obj = null;
                try
                {
                    obj = formatter.Deserialize(stream);
                }
                catch (Exception)
                {
                    obj = null;
                }
                return obj;
            }
        }

        public class SNBufferSerialization
        {
            ObjectChange objectChange;
            object LockPut;
            object LockGet;
            public SNBufferSerialization()
            {
                objectChange = new ObjectChange();
                LockPut = new object();
                LockGet = new object();
            }

            public bool PutObject(SNBuffer buffer, object data)
            {
                bool TF;
                SNArray<byte> array;
                lock (LockPut)
                {
                    TF = objectChange.Change_O2D(data, out array);
                }
                if (TF)
                {
                    buffer.PutInt(array.EndPoint);
                    buffer.PutArray(array, 0, array.EndPoint);
                    SNPools.Pool_Array.Put(array);
                }
                else
                {
                    buffer.PutInt(4);
                    buffer.PutInt(0);
                }
                return TF;
            }

            public object GetObject(SNBuffer buffer)
            {
                SNArray<byte> array = buffer.GetArray(buffer.GetInt());
                object obj;
                lock (LockGet)
                {
                    obj = objectChange.Change_D2O(array);
                }
                SNPools.Pool_Array.Put(array);
                return obj;
            }
        }

        public static class SNBufferPackage
        {
            public static SNBufferSerialization BufferSerialization = new SNBufferSerialization();
        }

        [Serializable]
        public struct Vect3
        {
            public float x;
            public float y;
            public float z;

            public Vect3(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
            /*
            public static implicit operator Vect3(Vector3 vect)
            {
                return new Vect3(vect.x, vect.y, vect.z);
            }
            public static implicit operator Vector3(Vect3 vect)
            {
                return new Vector3(vect.x, vect.y, vect.z);
            }
            */
        }

    }
}