using System;

namespace NoContainer.Tests
{
    public class NoContainerFixture : IDisposable
    {
        public NoContainerFixture()
        {
            C = new NoContainer();
        }

        public void Dispose()
        {
            C.Dispose();
        }

        public IContainer C { get; }
    }
}