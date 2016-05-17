# NoContainer
## Because, no.

```
PM> Install-Package hq.container
```

### Introduction
NoContainer is a minimal implementation of a dependency injection library. It supports named instances,
function-descriptor style registration, lifecycle management, resolution of arbitrary (unregistered) instances,
auto-resolution via fallback assemblies, and will not throw exceptions implicitly for performance reasons.

### Usage

```csharp
var container = new NoContainer();
container.Register<IFoo>(r => new Foo());
container.Register<IBar>(r => new Bar(r.Resolve<IFoo>()));

var bar = container.Resolve<IBar>();
```