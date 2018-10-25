using System;
using nocontainer;

namespace HQ.Harmony.Tests
{
    public class HarmonyContainerFixture : IDisposable
    {
        public HarmonyContainerFixture()
        {
            C = new HarmonyContainer();
        }

        public void Dispose()
        {
            C.Dispose();
        }

        public IContainer C { get; }
    }
}