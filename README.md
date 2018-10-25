Harmony by HQ
=============

[![License](https://img.shields.io/badge/License-RPL%201.5-red.svg)](https://opensource.org/licenses/RPL-1.5)

## Documentation

### Basic Usage

```csharp
var container = new NoContainer();
container.Register<IFoo>(r => new Foo(), Lifetime.Permanent);
## License
Harmony is licensed under RPL 1.5. More details can be found [here](https://github.com/hq-io/HQ.Cadence/blob/master/LICENSE.md)