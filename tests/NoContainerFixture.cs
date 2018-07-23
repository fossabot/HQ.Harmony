using System;
using nocontainer;

namespace NoContainer.Tests
{
    public class NoContainerFixture : IDisposable
    {
        public NoContainerFixture()
        {
            C = new nocontainer.NoContainer();
        }

        public void Dispose()
        {
            C.Dispose();
        }

        public IContainer C { get; }
    }
}