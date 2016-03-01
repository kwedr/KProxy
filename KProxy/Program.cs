using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HttpListener = KProxy.HttpListener;

namespace KProxy
{
    class Program
    {
        private static HttpListener _proxy;
        private static uint _port;

        static void Main(string[] args)
        {
            SetupProxy();
            StartProxy();

            while (true)
            {
                Thread.Sleep(1000);
            };
        }

        public static void StartProxy()
        {
            try
            {
                _proxy = new HttpListener(IPAddress.Any, (int)_port);
                _proxy.Start();
                Console.WriteLine("KProxy is Started!");
            }
            catch
            {
                throw new SocketException();
            }
        }

        public static void SetupProxy()
        {
            _port = 8887;            
        }
    }
}
