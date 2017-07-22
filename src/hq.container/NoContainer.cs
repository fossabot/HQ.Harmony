#region License
/*
   Copyright 2016 HQ.io

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
#endregion

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using hq.compiler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace hq.container
{
    #region Interfaces

    public enum Lifetime { AlwaysNew, Permanent, Thread, Request }

    public interface IContainer : IDependencyResolver, IDependencyRegistrar { }

    public interface IDependencyRegistrar : IDisposable
    {
        void Register(Type type, Func<object> builder, Lifetime lifetime = Lifetime.AlwaysNew);
        void Register<T>(Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(string name, Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(string name, Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(T instance);
    }

    public interface IDependencyResolver : IDisposable
    {
        T Resolve<T>() where T : class;
        object Resolve(Type serviceType);
        IEnumerable<T> ResolveAll<T>() where T : class;
        IEnumerable ResolveAll(Type serviceType);
        T Resolve<T>(string name) where T : class;
        object Resolve(string name, Type serviceType);
    }

    #endregion
    
    #region Core Features

    public partial class NoContainer : IContainer
    {
        private readonly IEnumerable<Assembly> _fallbackAssemblies;

        public bool ThrowIfCantResolve { get; set; }

        public NoContainer(IEnumerable<Assembly> fallbackAssemblies = null)
        {
            _fallbackAssemblies = fallbackAssemblies ?? Enumerable.Empty<Assembly>();
            _factory = new InstanceFactory();
        }

        #region Register

        public struct NameAndType
        {
            public readonly Type Type;
            public readonly string Name;

            public NameAndType(string name, Type type)
            {
                Name = name;
                Type = type;
            }

            public bool Equals(NameAndType other)
            {
                return Type == other.Type && string.Equals(Name, other.Name);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is NameAndType && Equals((NameAndType)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Type?.GetHashCode() ?? 0) * 397) ^ (Name?.GetHashCode() ?? 0);
                }
            }

            private sealed class TypeNameEqualityComparer : IEqualityComparer<NameAndType>
            {
                public bool Equals(NameAndType x, NameAndType y)
                {
                    return x.Type == y.Type && string.Equals(x.Name, y.Name);
                }

                public int GetHashCode(NameAndType obj)
                {
                    unchecked
                    {
                        return ((obj.Type?.GetHashCode() ?? 0) * 397) ^ (obj.Name?.GetHashCode() ?? 0);
                    }
                }
            }

            public static IEqualityComparer<NameAndType> TypeNameComparer { get; } = new TypeNameEqualityComparer();
        }

        private readonly IDictionary<Type, Func<object>> _registrations = new ConcurrentDictionary<Type, Func<object>>();
        private readonly IDictionary<NameAndType, Func<object>> _namedRegistrations = new ConcurrentDictionary<NameAndType, Func<object>>();
        private readonly IDictionary<Type, List<Func<object>>> _collectionRegistrations = new ConcurrentDictionary<Type, List<Func<object>>>();

        public void Register(Type type, Func<object> builder, Lifetime lifetime = Lifetime.AlwaysNew)
        {
            Func<object> next = WrapLifecycle(builder, lifetime);
            if (_registrations.ContainsKey(type))
            {
                Func<object> previous = _registrations[type];
                _registrations[type] = next;
                RegisterManyUnnamed(type, previous);
            }
            else
            {
                _registrations[type] = next;
            }
        }

        public void Register<T>(Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            Type type = typeof(T);
            Func<object> next = WrapLifecycle(builder, lifetime);
            if (_registrations.ContainsKey(type))
            {
                Func<object> previous = _registrations[type];
                _registrations[type] = next;
                RegisterManyUnnamed(type, previous);
            }
            else
            {
                _registrations[type] = next;
            }
        }

        public void Register<T>(string name, Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            var type = typeof(T);
            _namedRegistrations[new NameAndType(name, type)] = WrapLifecycle(builder, lifetime);
        }

        public void Register<T>(string name, Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            var type = typeof(T);
            _namedRegistrations[new NameAndType(name, type)] = () => WrapLifecycle(builder, lifetime)(this);
        }

        public void Register<T>(Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            Type type = typeof(T);
            Func<object> next = () => WrapLifecycle(builder, lifetime)(this);
            if (_registrations.ContainsKey(type))
            {
                Func<object> previous = _registrations[type];
                _registrations[type] = next;
                RegisterManyUnnamed(type, previous);
            }
            else
            {
                _registrations[type] = next;
            }
        }

        public void Register<T>(T instance)
        {
            Type type = typeof(T);
            Func<object> next = () => instance;
            if (_registrations.ContainsKey(type))
            {
                Func<object> previous = _registrations[type];
                _registrations[type] = next;
                RegisterManyUnnamed(type, previous);
            }
            else
            {
                _registrations[type] = next;
            }
        }

        private void RegisterManyUnnamed(Type type, Func<object> previous)
        {
            List<Func<object>> collectionBuilder;
            if (!_collectionRegistrations.TryGetValue(type, out collectionBuilder))
            {
                collectionBuilder = new List<Func<object>> { previous };
                _collectionRegistrations.Add(type, collectionBuilder);
            }
            collectionBuilder.Add(_registrations[type]);

            // implied registration of the enumerable equivalent
            Register(typeof(IEnumerable<>).MakeGenericType(type), () =>
            {
                IList collection = (IList)_factory.CreateInstance(typeof(List<>).MakeGenericType(type));
                foreach (var item in YieldCollection(collectionBuilder))
                    collection.Add(item);
                return collection;
            }, Lifetime.Permanent);
        }

        #endregion

        #region Resolve

        public T Resolve<T>() where T : class
        {
            var serviceType = typeof(T);
            Func<object> builder;
            if (!_registrations.TryGetValue(serviceType, out builder))
                return AutoResolve(serviceType) as T;
            var resolved = builder() as T;
            if (resolved != null)
                return resolved;
            if (ThrowIfCantResolve)
                throw new InvalidOperationException($"No registration for {serviceType}");
            return null;
        }

        public IEnumerable<T> ResolveAll<T>() where T : class
        {
            var serviceType = typeof(T);
            List<Func<object>> collectionBuilder;
            return _collectionRegistrations.TryGetValue(serviceType, out collectionBuilder)
                ? YieldCollection<T>(collectionBuilder)
                : Enumerable.Empty<T>();
        }

        private static IEnumerable<T> YieldCollection<T>(IEnumerable<Func<object>> collectionBuilder) where T : class
        {
            foreach (var builder in collectionBuilder)
                yield return builder() as T;
        }

        public object Resolve(Type serviceType)
        {
            Func<object> builder;
            if (!_registrations.TryGetValue(serviceType, out builder))
                return AutoResolve(serviceType);
            var resolved = builder();
            if (resolved != null)
                return resolved;
            if (ThrowIfCantResolve)
                throw new InvalidOperationException($"No registration for {serviceType}");
            return null;
        }

        public IEnumerable ResolveAll(Type serviceType)
        {
            List<Func<object>> collectionBuilder;
            return _collectionRegistrations.TryGetValue(serviceType, out collectionBuilder)
                ? YieldCollection(collectionBuilder)
                : Enumerable.Empty<object>();
        }

        private static IEnumerable YieldCollection(IEnumerable<Func<object>> collectionBuilder)
        {
            foreach (var builder in collectionBuilder)
                yield return builder();
        }

        public T Resolve<T>(string name) where T : class
        {
            Func<object> builder;
            if (_namedRegistrations.TryGetValue(new NameAndType(name, typeof(T)), out builder))
                return builder() as T;
            if (ThrowIfCantResolve)
                throw new InvalidOperationException($"No registration for {typeof(T)} named {name}");
            return null;
        }

        public object Resolve(string name, Type serviceType)
        {
            Func<object> builder;
            if (_namedRegistrations.TryGetValue(new NameAndType(name, serviceType), out builder))
                return builder();
            if (ThrowIfCantResolve)
                throw new InvalidOperationException($"No registration for {serviceType} named {name}");
            return null;
        }

        #endregion

        #region Auto-Resolve w/ Fallback

        private readonly InstanceFactory _factory;

        private object CreateInstance(Type implementationType)
        {
            // type->constructor
            ConstructorInfo ctor = _factory.GetOrCacheConstructorForType(implementationType);

            // constructor->parameters
            ParameterInfo[] parameters = _factory.GetOrCacheParametersForConstructor(ctor);

            // parameterless ctor
            if (parameters.Length == 0)
                return _factory.CreateInstance(implementationType);

            // auto-resolve widest ctor
            object[] args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                args[i] = AutoResolve(parameters[i].ParameterType);

            return _factory.CreateInstance(implementationType, args);
        }

        public object AutoResolve(Type serviceType)
        {
            while (true)
            {
                Func<object> creator;

                // got it:
                if (_registrations.TryGetValue(serviceType, out creator))
                    return creator();

                // want it:
                TypeInfo typeInfo = serviceType.GetTypeInfo();
                if (!typeInfo.IsAbstract)
                    return CreateInstance(serviceType);

                // need it:
                Type type = _fallbackAssemblies.SelectMany(s => s.GetTypes()).FirstOrDefault(i => serviceType.IsAssignableFrom(i) && !i.GetTypeInfo().IsInterface);
                if (type == null)
                {
                    if (ThrowIfCantResolve)
                        throw new InvalidOperationException($"No registration for {serviceType}");

                    return null;
                }

                serviceType = type;
            }
        }

        #endregion

        #region Lifetime Management

        private Func<IDependencyResolver, T> WrapLifecycle<T>(Func<IDependencyResolver, T> builder, Lifetime lifetime) where T : class
        {
            Func<IDependencyResolver, T> registration;
            switch (lifetime)
            {
                case Lifetime.AlwaysNew:
                    registration = builder;
                    break;
                case Lifetime.Permanent:
                    registration = ProcessMemoize(builder);
                    break;
                case Lifetime.Thread:
                    registration = ThreadMemoize(builder);
                    break;
#if SupportsRequests
                case Lifetime.Request:
                    registration = RequestMemoize(builder);
                    break;
#endif
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
            }
            return registration;
        }

        private Func<T> WrapLifecycle<T>(Func<T> builder, Lifetime lifetime) where T : class
        {
            Func<T> registration;
            switch (lifetime)
            {
                case Lifetime.AlwaysNew:
                    registration = builder;
                    break;
                case Lifetime.Permanent:
                    registration = ProcessMemoize(builder);
                    break;
                case Lifetime.Thread:
                    registration = ThreadMemoize(builder);
                    break;
#if SupportsRequests
                case Lifetime.Request:
                    registration = RequestMemoize(builder);
                    break;
#endif
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
            }
            return registration;
        }

        private static Func<T> ProcessMemoize<T>(Func<T> f)
        {
            var cache = new ConcurrentDictionary<Type, T>();

            return () => cache.GetOrAdd(typeof(T), v => f());
        }

        private static Func<T> ThreadMemoize<T>(Func<T> f)
        {
            ThreadLocal<T> cache = new ThreadLocal<T>(f);

            return () => cache.Value;
        }

        private Func<IDependencyResolver, T> ProcessMemoize<T>(Func<IDependencyResolver, T> f)
        {
            var cache = new ConcurrentDictionary<Type, T>();

            return r => cache.GetOrAdd(typeof(T), v => f(this));
        }

        private Func<IDependencyResolver, T> ThreadMemoize<T>(Func<IDependencyResolver, T> f)
        {
            ThreadLocal<T> cache = new ThreadLocal<T>(() => f(this));

            return r => cache.Value;
        }

        #endregion

        public void Dispose()
        {
            // No scopes, so nothing to dispose
        }
    }

    #endregion

    #region ASP.NET Features

    public partial class NoContainer
    {
        private Func<T> RequestMemoize<T>(Func<T> f)
        {
            return () =>
            {
                IHttpContextAccessor accessor = Resolve<IHttpContextAccessor>();
                if (accessor?.HttpContext == null)
                    return f(); // always new

                var cache = accessor.HttpContext.Items;
                var cacheKey = f.ToString();
                object item;
                if (cache.TryGetValue(cacheKey, out item))
                    return (T)item; // got it

                item = f(); // need it
                cache.Add(cacheKey, item);
                return (T)item;
            };
        }

        private Func<IDependencyResolver, T> RequestMemoize<T>(Func<IDependencyResolver, T> f)
        {
            return r =>
            {
                IHttpContextAccessor accessor = r.Resolve<IHttpContextAccessor>();
                if (accessor?.HttpContext == null)
                    return f(this); // always new

                var cache = accessor.HttpContext.Items;
                var cacheKey = f.ToString();
                object item;
                if (cache.TryGetValue(cacheKey, out item))
                    return (T)item; // got it

                item = f(this); // need it
                cache.Add(cacheKey, item);
                return (T)item;
            };
        }

        public IServiceProvider Populate(IServiceCollection services)
        {
            Register<IServiceProvider>(() => new NoServiceProvider(this, services), Lifetime.Permanent);
            Register<IServiceScopeFactory>(() => new NoServiceScopeFactory(this), Lifetime.Permanent);
            Register<IEnumerable<ServiceDescriptor>>(services);
            Register(this);
            return Resolve<IServiceProvider>();
        }

        internal sealed class NoServiceScopeFactory : IServiceScopeFactory
        {
            private readonly IContainer _container;

            public NoServiceScopeFactory(IContainer container)
            {
                _container = container;
            }

            public IServiceScope CreateScope()
            {
                return new NoServiceScope(_container);
            }

            private class NoServiceScope : IServiceScope
            {
                private readonly IContainer _container;

                public NoServiceScope(IContainer container)
                {
                    _container = container;
                }

                public IServiceProvider ServiceProvider => _container.Resolve<IServiceProvider>();

                public void Dispose() => _container.Dispose();
            }
        }

        internal sealed class NoServiceProvider : IServiceProvider, ISupportRequiredService
        {
            private readonly IContainer _container;
            private readonly IServiceProvider _fallback;

            public NoServiceProvider(IContainer container, IServiceCollection services)
            {
                _container = container;
                _fallback = services.BuildServiceProvider();
                RegisterServiceDescriptors(services);
            }

            private void RegisterServiceDescriptors(IServiceCollection services)
            {
                // we're going to shell out to the native container for anything passed in here
                foreach (ServiceDescriptor descriptor in services)
                    _container.Register(descriptor.ServiceType, () => _fallback.GetService(descriptor.ServiceType));
            }

            public object GetService(Type serviceType)
            {
                return _container.Resolve(serviceType) ?? _fallback.GetService(serviceType);
            }

            public object GetRequiredService(Type serviceType)
            {
                return _container.Resolve(serviceType) ?? _fallback.GetRequiredService(serviceType);
            }
        }
    }

    #endregion
}