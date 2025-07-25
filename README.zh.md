# Nino

终极高性能C#二进制序列化库。

![build](https://img.shields.io/github/actions/workflow/status/JasonXuDeveloper/Nino/.github/workflows/ci.yml?branch=main)
![license](https://img.shields.io/github/license/JasonXuDeveloper/Nino)
[![nino.nuget](https://img.shields.io/nuget/v/Nino?label=Nino)](https://www.nuget.org/packages/Nino)
[![openupm](https://img.shields.io/npm/v/com.jasonxudeveloper.nino?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.jasonxudeveloper.nino/)


[官网](https://nino.xgamedev.net/zh/)

大概率是C#里最快速、最灵活的二进制序列化库。QQ群：[1054653934](https://qm.qq.com/q/cvalwQWRvU)

![Activity](https://repobeats.axiom.co/api/embed/a9aea9d0b7b75f40c14af83e3c1f20eca39486c4.svg "Repobeats analytics image")


## 功能列表

- 支持**全部非托管**类型（`int`/`float`/`DateTime`/`Vector`/`Matrix`等）
- 支持 `ValueTuple`/`Tuple`/`KeyValuePair`
- 支持**全部**用`[NinoType]`标识的自定义`interface`/`class`/`struct`/`record`/`record struct`类型（包括`泛型`，支持自定义反序列化构造函数）
- 支持任意**IEnumerable**类型（`List`, `Dictonary`, `ConcurrentDictonary`, `Hashset`, `ArraySegment`, `Stack`, `ReadOnlyList`等）
- 支持任意**Span**类型
- 支持任意**Nullable**类型
- 支持**嵌套**上述类型（例如`Stack<Dictionary<Int, List<SupportedType[]>[]>[]>`）
- 支持**多态**
- 高性能，低GC
- 支持**类型检查**（保证数据完整性）
- 支持**版本兼容**（例如添加字段，更改字段类型等）
- 支持**跨项目**（C#项目）类型序列化（例如从B.dll序列化A.dll中的类型）

## 快速开始

[文档](https://nino.xgamedev.net/zh/start)

## 性能

[微基准测试](https://nino.xgamedev.net/zh/perf/micro)
