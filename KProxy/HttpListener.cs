using System;
using System.Net;

namespace KProxy
{
    public sealed class HttpListener : Listener
    {
        public HttpListener(int port) : this(IPAddress.Any, port)
        {
            HttpClient.EventLog.Clear();
        }
        public HttpListener(IPAddress address, int port) 
            : base(port, address)
        {
            HttpClient.EventLog.Clear();
            Console.WriteLine("Proxy IP is: " + address.ToString () + " Port is:" + port);
        }
        public override void OnAccept(IAsyncResult ar)
        {
            try
            {
                var newSocket = ListenSocket.EndAccept(ar);
                if (newSocket != null)
                {
                    var newClient = new HttpClient(newSocket, RemoveClient);
                    AddClient(newClient);
                    newClient.StartHandshake();
                }
            }
            catch { }
            try
            {
                //Restart Listening
                ListenSocket.BeginAccept(OnAccept, ListenSocket);
            }
            catch
            {
                Dispose();
            }
        }
        public override string ToString()
        {
            return "HTTP service on " + Address + ":" + Port;
        }
    }
}
