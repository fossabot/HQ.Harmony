using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace hq.container
{
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
                _container.Register(descriptor.ServiceType, () => _fallback.GetService(descriptor.ServiceType), Lifetime.Permanent);
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
