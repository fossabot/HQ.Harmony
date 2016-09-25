# HQ.io Container
## The next best thing to not needing one.

```
PM> Install-Package hq.container
```

### Introduction

NoContainer is a minimally useful implementation of a dependency injection library. It supports named instances,
function-descriptor style registration, lifecycle management, resolution of arbitrary (unregistered) instances,
auto-resolution via fallback assemblies, implicit collections, and will not throw exceptions implicitly for 
performance reasons. It is compact enough that it is deployable as a single file, rather than taking on a 
library dependency.

### Basic Usage

```csharp
var container = new NoContainer();
container.Register<IFoo>(r => new Foo(), Lifetime.Permanent);
container.Register<IBar>(r => new Bar(r.Resolve<IFoo>()), Lifetime.AlwaysNew);

var bar = container.Resolve<IBar>();
```

### Named instances

```csharp
var container = new NoContainer();
container.Register<IFoo>("one for the money", r => new Foo(1));
container.Register<IFoo>("two for the show", r => new Foo(2));

var foo = container.Resolve<IFoo>("one for the money");
```

### Auto-resolution by fallback assemblies

```csharp
var container = new NoContainer(new [] { typeof(FooImplementation).Assembly });
var foo = container.Resolve<IFoo>();
```

### Resolve unregistered instances

```csharp
var container = new NoContainer();
container.Register<IFoo>(() => new Foo(), Lifetime.Request);

public class Bar { public IFoo Baz { get; set; } }

var bar = container.Resolve<Bar>();
```

### Implicit and explicit collections

```csharp
var container = new NoContainer();
container.Register<IFoo>(r => new Foo(1));
container.Register<IFoo>(r => new Foo(2));

var foos = container.Resolve<IEnumerable<IFoo>>(); // or,
var foos = container.ResolveAll<IFoo>();
```

### Turn on exceptions on failed resolutions

```csharp
var container = new NoContainer { ThrowIfCantResolve = true };
```