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
    }

    public static class ContainerExtensions
    {
        public static void Import(this IContainer container, IEnumerable<ServiceDescriptor> descriptors)
        {
            foreach (ServiceDescriptor descriptor in descriptors)
            {
                Lifetime lifetime;
                switch (descriptor.Lifetime)
                {
                    case ServiceLifetime.Singleton:
                        lifetime = Lifetime.Permanent;
                        break;
                    case ServiceLifetime.Scoped:
                        lifetime = Lifetime.Thread;
                        break;
                    case ServiceLifetime.Transient:
                        lifetime = Lifetime.AlwaysNew;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (descriptor.ImplementationType != null)
                {
                    container.Register(descriptor.ServiceType, () => container.Resolve(descriptor.ServiceType));
                }
                else if (descriptor.ImplementationFactory != null)
                {
                    container.Register(descriptor.ServiceType, () => descriptor.ImplementationFactory(container.Resolve<IServiceProvider>()), lifetime);
                }
                else
                {
                    container.Register(descriptor.ImplementationInstance);
                }
            }
        }
    }

    internal sealed class NoServiceProvider : IServiceProvider, ISupportRequiredService
    {
        private readonly IContainer _container;

        public NoServiceProvider(IContainer container)
        {
            _container = container;
        }

        public object GetService(Type serviceType)
        {
            return _container.Resolve(serviceType);
        }

        public object GetRequiredService(Type serviceType)
        {
            return _container.Resolve(serviceType);
        }
    }
}
