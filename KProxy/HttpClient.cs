using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KProxy
{
    public delegate void DestroyDelegate(IClient client);
    public class HttpClient : IClient
    {
        public HttpClient(Socket clientSocket, DestroyDelegate destroyer)
        {
            ClientSocket.Socket = clientSocket;
            _destroyer = destroyer;
            EventLog.Write("["+ this.GetHashCode()+"][" + ClientSocket.Handle + "] HttpClient Connect");
        }

        public override string ToString()
        {
            return "Client connection";
        }

        public async void StartHandshake()
        {
            int errno = 0;
            Task<int> t = ClientSocket.RecvAsync(ref errno);
            int len = await t;
            ClientSocket.RecvAsyncRet = null;
            ConnectDest(len);

            Task.Factory.StartNew(() => Dispose());
        }

        public async void Dispose()
        {
            while (true)
            {
                int count = 0;
                if (ClientSocket.Socket == null)
                        count++;

                if (DestSocket.Socket == null)
                    count++;

                if (count >= 2 && _destroyer != null)
                {
                    EventLog.Write("[" + this.GetHashCode() + "] Destroy");
                    _destroyer(this);
                    return;
                }

                Thread.Sleep(1000);
            }
        }

        private void ConnectDest(int len)
        {
            try
            {
                IPAddress address = Dns.GetHostEntry("localhost").AddressList[1];
                var destinationEndPoint = new IPEndPoint(address, 8888);

                DestSocket.Socket = new Socket(destinationEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                DestSocket.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
                Task t = DestSocket.ConnectAsync (destinationEndPoint);
                t.ContinueWith(task =>
               {
                   if (task.IsCompleted)
                   {
                       DestSocket.BindSocket = ClientSocket;
                       ClientSocket.BindSocket = DestSocket;

                       EventLog.Write("[" + DestSocket.Handle + "] Dest Connect");

                       DestSocket.SendBuffer.Write(ClientSocket.RecvBuffer, len);
                       DestSocket.Send();
                   }
               });
            }
            catch
            {
            }
        }

        public class SocketBuffer
        {
            private const int max = 4096;
            private byte[] _Buffer = new byte[max];
            private int length = 0;
            private object BufferLock = new object();
            public byte[] Data
            {
                get
                {
                    lock (BufferLock)
                    {
                        return _Buffer;
                    }
                }
                set { }
            }
            public int Length
            {
                get
                {
                    lock (BufferLock)
                    {
                        return length;
                    }
                }
                set { }
            }
            public int FreeSapce
            {
                get {
                    lock (BufferLock)
                    {
                        int len = max - length;
                        Debug.Assert(len >= 0);
                        return len;
                    }
                }
                set { }
            }

            public void Write (byte[] buffer, int len)
            {
                lock (BufferLock)
                {
                    Buffer.BlockCopy(buffer, 0, _Buffer, length, len);
                    length += len;
                    Debug.Assert(length <= max);
                }
            }

            public void Remove(int len)
            {
                lock (BufferLock)
                {
                    Buffer.BlockCopy(_Buffer, len, _Buffer, 0, length - len);
                    length -= len;
                    Debug.Assert(length >= 0);
                }
            }
        }

        public class RemoteSocket
        {
            public RemoteSocket BindSocket = null;
            public byte[] RecvBuffer = new byte[BUFFER_SIZE];
            public SocketBuffer SendBuffer = new SocketBuffer();
            private Socket _socket = null;
            private object AsyncLock = new object();
            public int Handle = 0;

            public IAsyncResult RecvAsyncRet = null;
            public IAsyncResult SendAsyncRet = null;

            private int _RecvAsyncTask = 0;
            private int _SendAsyncTask = 0;
            private int RecvAsyncTask
            {
                get
                {
                    lock (AsyncLock)
                    {
                        return _RecvAsyncTask;
                    }
                }

                set
                {
                    lock (AsyncLock)
                    {
                        _RecvAsyncTask = value;
                    }
                }
            }

            private int SendAsyncTask
            {
                get
                {
                    lock (AsyncLock)
                    {
                        return _SendAsyncTask;
                    }
                }

                set
                {
                    lock (AsyncLock)
                    {
                        _SendAsyncTask = value;
                    }
                }
            }

            public Socket Socket
            {
                get
                {
                    lock (this)
                    {
                        return _socket;
                    }                    
                }

                set
                {
                    lock (this)
                    {
                        if (_socket != null)
                        {
                            EventLog.Write("[" + Socket.Handle.ToInt32() + "] Close");
                            _socket.Close();
                            Handle = 0;
                        }
                        _socket = value;
                        if (_socket != null)
                        {
                            Handle = _socket.Handle.ToInt32();
                        }
                    }
                }
            }

            public Task ConnectAsync(EndPoint remote)
            {
                lock (AsyncLock)
                {
                    var result = Socket.BeginConnect(remote, _ => { }, Socket);
                    return Task.Factory.FromAsync(result, (r) => Socket.EndConnect(r));
                }
            }

            public Task<int> SendAsync(ref int errorno)
            {
                lock (AsyncLock)
                {
                    if (SendAsyncRet != null)
                    {
                        errorno = SendAsyncRet.GetHashCode();
                        return null;
                    }
                        
                    try
                    {
                        SendAsyncRet = Socket.BeginSend(SendBuffer.Data, 0, SendBuffer.Length, SocketFlags.None, _ => { }, Socket);
                    }
                    catch
                    {
                        errorno = 1;
                        Socket = null;
                        return null;
                    }
                    Task<int> t = Task.Factory.FromAsync(SendAsyncRet, (r) =>
                    {
                        try
                        {
                            return Socket.EndSend(r);
                        }
                        catch
                        {
                            Socket = null;
                            return -1;
                        }
                    } );

                    return t;
                }
            }

            public Task<int> RecvAsync(ref int errorno)
            {
                lock (AsyncLock)
                {
                    if (RecvAsyncRet != null)
                    {
                        errorno = RecvAsyncRet.GetHashCode();
                        return null;
                    }

                    int len = BUFFER_SIZE;
                    if (BindSocket != null)
                        len = BindSocket.SendBuffer.FreeSapce > BUFFER_SIZE ? BUFFER_SIZE : BindSocket.SendBuffer.FreeSapce;

                    if (len <= 0)
                    {
                        errorno = 1;
                        return null;
                    }

                    try
                    {
                        RecvAsyncRet = Socket.BeginReceive(RecvBuffer, 0, len, SocketFlags.None, _ => { }, Socket);
                    }
                    catch
                    {
                        Socket = null;
                        errorno = 2;
                        return null;
                    }

                    Task<int> t = Task.Factory.FromAsync(RecvAsyncRet, (r) =>
                        {
                            try
                            {
                                return Socket.EndReceive(r);
                            }
                            catch
                            {
                                Socket = null;
                                return -1;
                            }
                            
                       } );

                    return t;                                        
                }
            }

            public void Send()
            {
                if (SendAsyncTask == 0)
                {
                    int errno = 0;
                    Task<int> t = SendAsync (ref errno);
                    if (t == null)
                    {
                        EventLog.Write("[" + Handle + "] Send Errno:" + errno);
                        return;
                    }

                    EventLog.Write("[" + Handle + "] Task:" + t.Id + " BeginSend:" + SendBuffer.Length + " Async:" + SendAsyncRet.GetHashCode ());

                    SendAsyncTask = t.Id;

                    t.ContinueWith(task =>
                    {
                        EventLog.Write("[" + Handle + "] Task:" + t.Id + " EndSend:" + task.Result + " Async:" + SendAsyncRet.GetHashCode());

                        SendAsyncRet = null;
                        SendAsyncTask = 0;

                        if (task.IsCompleted)
                        {
                            if (task.Result < 0)
                            {
                                Socket = null;
                                EventLog.Write("[" + Handle + "] Send Fault:" + t.Exception.ToString());
                                return;
                            }

                            SendBuffer.Remove(task.Result);                            

                            if (SendBuffer.Length > 0)
                            {
                                Send();
                            }
                            else
                            {
                                BindSocket.Recv();
                            }
                        }
                        else if (task.IsFaulted)
                        {
                            EventLog.Write("[" + Handle + "] Send Fault:" + t.Exception.ToString());
                            Socket = null;
                            return;
                        }
                        else
                        {
                            EventLog.Write("[" + Handle + "] Send Cancel:" + t.Exception.ToString());
                        }

                        Recv();
                    });                    
                }
                else
                {
                    EventLog.Write("[" + Handle + "] Not Send Task:" + SendAsyncTask);
                }
            }
            public void Recv ()
            {
                if (RecvAsyncTask == 0)
                {
                    int errno = 0;
                    Task<int> t = RecvAsync (ref errno);
                    if (t == null)
                    {
                        EventLog.Write("[" + Handle + "] Recv Errno:" + errno);
                        return;
                    }

                    EventLog.Write("[" + Handle + "] Task:" + t.Id + " BeginRecv:" + BindSocket.SendBuffer.FreeSapce + " Async:" + RecvAsyncRet.GetHashCode ());

                    t.ContinueWith(task =>
                    {
                        EventLog.Write("[" + Handle + "] Task:" + task.Id + " EndRecv:" + task.Result + " Async:" + RecvAsyncRet.GetHashCode());

                        RecvAsyncRet = null;                        
                        RecvAsyncTask = 0;

                        if (t.IsCompleted)
                        {                            
                            if (task.Result > 0)
                            {
                                BindSocket.SendBuffer.Write(RecvBuffer, task.Result);
                                BindSocket.Send ();
                            }
                            else if (task.Result <= 0)
                            {
                                Socket = null;
                            }
                        }
                        else if (t.IsFaulted)
                        {
                            EventLog.Write("[" + Handle + "] Recv Fault:" + t.Exception.ToString ());
                            Socket = null;
                            return;
                        }
                        else
                        {
                            EventLog.Write("[" + Handle + "] Recv Cancel:" + t.Exception.ToString());
                        }

                        Recv();
                    });
                }
                else
                {
                    EventLog.Write("[" + Handle + "] Not Recv Task:" + RecvAsyncTask);
                }
            }
        }

        public string ToString(bool withUrl)
        {
            string ret = "HTTP Connection";
            return ret;
        }

        private RemoteSocket ClientSocket = new RemoteSocket();
        private RemoteSocket DestSocket = new RemoteSocket();
        private readonly DestroyDelegate _destroyer;
        private const int BUFFER_SIZE = 2048;

        public static class EventLog
        {
            static object locker = new object();
            public static string FullPathName
            {
                get
                {
                    string FilePath = System.Environment.CurrentDirectory;
                    if (string.IsNullOrEmpty(FilePath))
                    {
                        FilePath = Directory.GetCurrentDirectory();
                    }

                    string filename = FilePath +
                        string.Format("{0:yyyy-MM-dd}.txt", DateTime.Now);

                    FileInfo finfo = new FileInfo(filename);

                    if (finfo.Directory.Exists == false)
                    {
                        finfo.Directory.Create();
                    }

                    return filename;
                }
                set
                {

                }
            }
            
            public static void Write(string format, params object[] arg)
            {
                Write(string.Format(format, arg));
            }
            public static void Write(string message)
            {

                lock(locker)
                {
                    string writeString = string.Format("{0:yyyy/MM/dd HH:mm:ss} {1}", DateTime.Now, message) + Environment.NewLine;
                    File.AppendAllText(FullPathName, writeString, Encoding.ASCII);
                    Console.WriteLine(message);
                }                
            }

            public static void Clear()
            {
                lock (locker)
                {
                    File.WriteAllText(FullPathName, String.Empty);
                }
            }
        }
    }
}
