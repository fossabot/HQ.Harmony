using System.Collections.Generic;
using System.Linq;
using nocontainer;
using Xunit;

namespace NoContainer.Tests
{
    public class RegisterTests : IClassFixture<NoContainerFixture>
    {
        readonly NoContainerFixture _fixture;

        public RegisterTests(NoContainerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Can_register_twice_and_get_back_a_collection()
        {
            _fixture.C.Register<IFoo>(() => new Foo(), Lifetime.Permanent);
            _fixture.C.Register<IFoo>(() => new OtherFoo(), Lifetime.Permanent);

            // strong-typed
            var strong = _fixture.C.ResolveAll<IFoo>();
            Assert.NotNull(strong);
            Assert.Equal(2, strong.Count());

            // weak-typed
            var weak = _fixture.C.ResolveAll(typeof(IFoo)).Cast<IFoo>();
            Assert.NotNull(weak);
            Assert.Equal(2, weak.Count());

            // implied
            var implied = _fixture.C.Resolve<IEnumerable<IFoo>>();
            Assert.NotNull(implied);
            Assert.Equal(2, implied.Count());
        }

        public interface IFoo { }
        public class Foo : IFoo { }
        public class OtherFoo : IFoo { }
    }
}
