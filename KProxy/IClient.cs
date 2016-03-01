using System;

namespace KProxy
{
    public interface IClient : IDisposable
    {
        void StartHandshake();
    }
}
