using System;
using System.Net;
using System.Net.Sockets;
using System.Collections;

namespace KProxy
{
    public abstract class Listener : IDisposable
    {
        protected Listener(int port, IPAddress address)
        {
            Port = port;
            Address = address;
        }
        protected int Port
        {
            get
            {
                return _port;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentException();
                _port = value;
                Restart();
            }
        }
        protected IPAddress Address
        {
            get
            {
                return _address;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException();
                _address = value;
                Restart();
            }
        }
        protected Socket ListenSocket
        {
            get
            {
                return _listenSocket;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException();
                _listenSocket = value;
            }
        }
        protected ArrayList Clients
        {
            get
            {
                return _clients;
            }
        }

        public bool IsDisposed { get; private set; }

        public void Start()
        {
            try
            {
                ListenSocket = new Socket(Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                ListenSocket.Bind(new IPEndPoint(Address, Port));
                ListenSocket.Listen(1024);
                ListenSocket.BeginAccept(OnAccept, ListenSocket);
            }
            catch
            {
                ListenSocket = null;
                throw new SocketException();
            }
        }
        protected void Restart()
        {
            //If we weren't listening, do nothing
            if (ListenSocket == null)
                return;
            ListenSocket.Close();
            Start();
        }
        protected void AddClient(IClient client)
        {
            if (Clients.IndexOf(client) == -1)
                Clients.Add(client);
        }
        protected void RemoveClient(IClient client)
        {
            Clients.Remove(client);
        }
        public int GetClientCount()
        {
            return Clients.Count;
        }
        public IClient GetClientAt(int index)
        {
            if (index < 0 || index >= GetClientCount())
                return null;
            return (IClient)Clients[index];
        }
        public bool Listening
        {
            get
            {
                return ListenSocket != null;
            }
        }
        public void Dispose()
        {
            if (IsDisposed)
                return;
            while (Clients.Count > 0)
            {
                ((IClient)Clients[0]).Dispose();
            }
            try
            {
                ListenSocket.Shutdown(SocketShutdown.Both);
            }
            catch { }
            if (ListenSocket != null)
                ListenSocket.Close();
            IsDisposed = true;
        }
        ~Listener()
        {
            Dispose();
        }
        public abstract void OnAccept(IAsyncResult ar);
        public override abstract string ToString();
        private int _port;
        private IPAddress _address;
        private Socket _listenSocket;
        private readonly ArrayList _clients = new ArrayList();
    }
}
