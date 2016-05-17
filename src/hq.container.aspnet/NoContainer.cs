using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace hq.container
{
    public partial class NoContainer : IContainer
    {
        private readonly IEnumerable<Assembly> _fallbackAssemblies;

        public bool ThrowIfCantResolve { get; set; }

        public NoContainer(IEnumerable<Assembly> fallbackAssemblies = null)
        {
            _fallbackAssemblies = fallbackAssemblies ?? Enumerable.Empty<Assembly>();
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

        public void Register<T>(Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            Func<T> registration = WrapLifecycle(builder, lifetime);
            _registrations[typeof(T)] = registration;
        }

        public void Register<T>(string name, Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            Func<T> registration = WrapLifecycle(builder, lifetime);
            _namedRegistrations[new NameAndType(name, typeof(T))] = registration;
        }

        public void Register<T>(string name, Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            Func<IDependencyResolver, T> registration = WrapLifecycle(builder, lifetime);
            _namedRegistrations[new NameAndType(name, typeof(T))] = () => registration(this);
        }

        public void Register<T>(Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class
        {
            Func<IDependencyResolver, T> registration = WrapLifecycle(builder, lifetime);
            _registrations[typeof(T)] = () => registration(this);
        }

        public void Register<T>(T instance)
        {
            _registrations[typeof(T)] = () => instance;
        }

        #endregion

        #region Resolve

        public T Resolve<T>() where T : class
        {
            Func<object> builder;
            if (!_registrations.TryGetValue(typeof(T), out builder))
                return AutoResolve(typeof(T)) as T;
            var resolved = builder() as T;
            if (resolved != null)
                return resolved;
            if (ThrowIfCantResolve)
                throw new InvalidOperationException($"No registration for {typeof(T)}");
            return null;
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

        public T Resolve<T>(string name) where T : class
        {
            Func<object> builder;
            if (_namedRegistrations.TryGetValue(new NameAndType(name, typeof(T)), out builder))
                return builder() as T;
            if (ThrowIfCantResolve)
                throw new InvalidOperationException($"No registration for {typeof(T)} named {name}");
            return null;
        }

        public object Resolve(Type serviceType, string name)
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

        private readonly IDictionary<Type, ObjectActivator> _activators = new ConcurrentDictionary<Type, ObjectActivator>();
        private readonly IDictionary<Type, ConstructorInfo> _constructors = new ConcurrentDictionary<Type, ConstructorInfo>();
        private readonly IDictionary<ConstructorInfo, ParameterInfo[]> _constructorParameters = new ConcurrentDictionary<ConstructorInfo, ParameterInfo[]>();

        public object AutoResolve(Type serviceType)
        {
            Func<object> creator;

            // got it:
            if (_registrations.TryGetValue(serviceType, out creator)) return creator();

            // want it:
            TypeInfo typeInfo = serviceType.GetTypeInfo();
            if (!typeInfo.IsAbstract) return CreateInstance(serviceType);

            // need it:
            Type type = _fallbackAssemblies.SelectMany(s => s.GetTypes()).FirstOrDefault(i => serviceType.IsAssignableFrom(i) && !i.GetTypeInfo().IsInterface);
            if (type == null)
            {
                if (ThrowIfCantResolve)
                    throw new InvalidOperationException($"No registration for {serviceType}");
                return null;
            }

            return AutoResolve(type);
        }

        private object CreateInstance(Type implementationType)
        {
            // type->constructor
            ConstructorInfo ctor;
            if (!_constructors.TryGetValue(implementationType, out ctor))
                _constructors[implementationType] = ctor = GetSuitableConstructor(implementationType);

            // constructor->parameters
            ParameterInfo[] parameters;
            if (!_constructorParameters.TryGetValue(ctor, out parameters))
                _constructorParameters[ctor] = parameters = ctor.GetParameters();

            // activator 
            ObjectActivator activator;
            if (!_activators.TryGetValue(implementationType, out activator))
                _activators[implementationType] = activator = DynamicMethodFactory.Build(implementationType, ctor, parameters);

            object[] args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                args[i] = AutoResolve(parameters[i].ParameterType);

            return activator(args);
        }

        private static ConstructorInfo GetSuitableConstructor(Type implementationType)
        {
            // Pick the widest constructor; this way we could have parameterless constructors or
            // simple constructors for testing, without having to do anything special to get the
            // "real" one, such as attributes or other nonsense

            ConstructorInfo[] ctors = implementationType.GetConstructors();
            ConstructorInfo ctor = ctors.OrderByDescending(c => c.GetParameters().Length).Single();
            return ctor;
        }

        #region Object Activation

        public delegate object ObjectActivator(params object[] parameters);

        /// <summary>Source: <see cref="http://stackoverflow.com/questions/2353174/c-sharp-emitting-dynamic-method-delegate-to-load-parametrized-constructor-proble"/></summary>
        private static class DynamicMethodFactory
        {
            public static ObjectActivator Build(Type implementationType, ConstructorInfo ctor, IReadOnlyList<ParameterInfo> parameters)
            {
                var dynamicMethod = new DynamicMethod($"{implementationType.FullName}.ctor", implementationType, new[] { typeof(object[]) });
                var il = dynamicMethod.GetILGenerator();
                for (int i = 0; i < parameters.Count; i++)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    switch (i)
                    {
                        case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                        case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                        case 2: il.Emit(OpCodes.Ldc_I4_2); break;
                        case 3: il.Emit(OpCodes.Ldc_I4_3); break;
                        case 4: il.Emit(OpCodes.Ldc_I4_4); break;
                        case 5: il.Emit(OpCodes.Ldc_I4_5); break;
                        case 6: il.Emit(OpCodes.Ldc_I4_6); break;
                        case 7: il.Emit(OpCodes.Ldc_I4_7); break;
                        case 8: il.Emit(OpCodes.Ldc_I4_8); break;
                        default: il.Emit(OpCodes.Ldc_I4, i); break;
                    }
                    il.Emit(OpCodes.Ldelem_Ref);
                    Type paramType = parameters[i].ParameterType;
                    il.Emit(paramType.GetTypeInfo().IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, paramType);
                }
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
                return (ObjectActivator)dynamicMethod.CreateDelegate(typeof(ObjectActivator));
            }
        }

        #endregion

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
//#if SupportsRequests
                case Lifetime.Request:
                    registration = RequestMemoize(builder);
                    break;
//#endif
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
//#if SupportsRequests
                case Lifetime.Request:
                    registration = RequestMemoize(builder);
                    break;
//#endif
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
            }
            return registration;
        }

        private static Func<T> ProcessMemoize<T>(Func<T> f)
        {
            var cache = new ConcurrentDictionary<Type, T>();

            return () => cache.GetOrAdd(typeof(T), f());
        }

        private static Func<T> ThreadMemoize<T>(Func<T> f)
        {
            ThreadLocal<T> cache = new ThreadLocal<T>(f);

            return () => cache.Value;
        }

        private Func<IDependencyResolver, T> ProcessMemoize<T>(Func<IDependencyResolver, T> f)
        {
            var cache = new ConcurrentDictionary<Type, T>();

            return r => cache.GetOrAdd(typeof(T), f(this));
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

    public enum Lifetime
    {
        AlwaysNew,
        Permanent,
        Thread,
//#if SupportsRequests
        Request
//#endif
    }

    public interface IContainer : IDependencyResolver, IDependencyRegistrar { }

    public interface IDependencyRegistrar : IDisposable
    {
        void Register<T>(Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(string name, Func<T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(string name, Func<IDependencyResolver, T> builder, Lifetime lifetime = Lifetime.AlwaysNew) where T : class;
        void Register<T>(T instance);
    }

    public interface IDependencyResolver : IDisposable
    {
        T Resolve<T>() where T : class;
        T Resolve<T>(string name) where T : class;
        object Resolve(Type serviceType);
        object Resolve(Type serviceType, string name);
    }

    public static class DependencyContext
    {
        public static IContainer Current { get; set; }
    }
}