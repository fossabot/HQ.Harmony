//using System.Collections.Generic;
//using System.Linq;
//using Xunit;
//using hq.container;

//namespace hq.container.tests
//{
//    public class NoContainerTests : IClassFixture<NoContainerFixture>
//    {
//        private readonly NoContainerFixture _fixture;

//        public NoContainerTests(NoContainerFixture fixture)
//        {
//            _fixture = fixture;
//        }

//        [Fact]
//        public void Can_resolve_transient_twice_with_different_references()
//        {
//            _fixture.C.Register<IFoo>(() => new Foo(), Lifetime.AlwaysNew);

//            var first = _fixture.C.Resolve<IFoo>();
//            var second = _fixture.C.Resolve<IFoo>();

//            Assert.NotSame(first, second);
//        }

//        [Fact]
//        public void Can_resolve_singleton_twice_with_same_reference()
//        {
//            _fixture.C.Register<IFoo>(() => new Foo(), Lifetime.Permanent);

//            var first = _fixture.C.Resolve<IFoo>();
//            var second = _fixture.C.Resolve<IFoo>();

//            Assert.Same(first, second);
//        }

//        [Fact]
//        public void Can_resolve_instance_twice_with_same_reference()
//        {
//            var instance = new Foo();
//            _fixture.C.Register<IFoo>(instance);

//            var first = _fixture.C.Resolve<IFoo>();
//            var second = _fixture.C.Resolve<IFoo>();

//            Assert.Same(first, second);
//        }

//        [Fact]
//        public void Can_resolve_arbitrary_types()
//        {
//            _fixture.C.Register<IFoo>(() => new Foo(), Lifetime.Permanent);

//            var first = _fixture.C.Resolve<Bar>();
//            var second = _fixture.C.Resolve<Bar>();

//            Assert.NotSame(first, second);
//            Assert.Same(first.Baz, second.Baz);
//        }

//        [Fact]
//        public void Can_register_twice_and_get_back_a_collection()
//        {
//            _fixture.C.Register<IFoo>(() => new Foo(), Lifetime.Permanent);
//            _fixture.C.Register<IFoo>(() => new OtherFoo(), Lifetime.Permanent);

//            // strong-typed
//            var strong = _fixture.C.ResolveAll<IFoo>();
//            Assert.NotNull(strong);
//            Assert.Equal(2, strong.Count());

//            // weak-typed
//            var weak = _fixture.C.ResolveAll(typeof(IFoo)).Cast<IFoo>();
//            Assert.NotNull(weak);
//            Assert.Equal(2, weak.Count());

//            // implied
//            var implied = _fixture.C.Resolve<IEnumerable<IFoo>>();
//            Assert.NotNull(implied);
//            Assert.Equal(2, implied.Count());
//        }

//        public interface IFoo { }
//        public class Foo : IFoo { }
//        public class OtherFoo : IFoo { }

//        public class Bar
//        {
//            public IFoo Baz { get; set; }

//            public Bar(IFoo baz)
//            {
//                Baz = baz;
//            }
//        }
//    }
//}
