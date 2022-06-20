# Serilization 性能报告

#### [**测试数据**](/Nino/Assets/Nino/Test/Data.cs)

*第一次序列化的时候，Nino会对类型进行缓存，达到预热效果，使得同一类型的第二次开始的序列化速度大幅度提升，其他库亦是如此*

> Nino 反序列化还在优化中，反序列化性能的对比图还没更新，目前Nino Reflection反序列化比Protobuf快10%，Nino Code Gen反序列化比MsgPack快60%

## Unity平台性能测试

### 结论

体积方面，Nino最小，MsgPack其次，其他库不尽人意

序列化速度方面，Nino Code Gen最快，MsgPack略慢一筹，Nino Reflection基本与Protobuf-net一致，其他库不尽人意

反序列化速度方面，Nino Code Gen最快，MsgPack比Nino慢60%，Nino Reflection略跨于Protobuf-net一致，略微逊色于MongoDB.Bson，BinaryFormatter最糟糕

GC方面，Nino Code Gen和MsgPack碾压全部其他库，一览众山小，第一次序列化数据时产生的GC是其他序列化库的几分之一，甚至几十分之一！第二次开始序列化的时候会复用对象池内的数据，使得产生的GC仅仅只是其他序列化库的几百甚至几千分之一！

> Nino Code Gen和MsgPack的GC基本只有写入二进制数据是创建的二进制Buffer所造成的GC，无任何额外GC

### 易用性

Nino、BinaryFormatter、可以轻松用于Unity或其他C#平台（Mono以及IL2CPP平台），无需针对不同平台进行任何额外操作

MsgPack需要在IL2CPP平台（Unity和Xamarin）进行额外处理（防止AOT问题，需要预生成代码，不然会导致无法使用），该操作十分繁琐

Protobuf-net以及MongoDB.Bson在IL2CPP平台下，字典会无法使用，因为是AOT问题，暂时没找到解决方案

### 备注

- 测试的时候[MsgPack有生成代码](/Nino/Assets/Nino/Test/MessagePackGenerated.cs)，所以不要说Nino生成代码后和其他库对比不公平
- 这里测试用的是MsgPack LZ4压缩，如果不开压缩的话，MsgPack的速度会快10%，但是体积则会变大很多（大概是Protobuf-net的体积的60%，即Nino的数倍）
- MsgPack之所以比较快是因为它用到了Emit以及生成了动态类型进行序列化（高效且低GC），但是在IL2CPP平台下，会遇到限制，所以上面才会提到MsgPack在IL2CPP平台使用起来很繁琐，Nino Code Gen这边是静态生成进行序列化（高效且低GC），即便不生成代码也不影响IL2CPP下使用，并且Nino生成的代码可以搭配ILRuntime或Huatuo技术实时热更
- Odin序列化也会针对编辑器/PC Mono平台使用Emit优化性能，以及动态生成序列化方法，在IL2CPP下有和MsgPack一样的限制，故而这里就不做与Odin序列化的性能对比了
  - Odin序列化的性能，比Protobuf-net略快（测试结果出自Odin序列化官方），故而该库性能比MsgPack慢，故而不如Nino性能好
  - Odin序列化的体积，与Protobuf-net相差无几，比Nino大三倍

### 为什么Nino又小又快、还能易用且低GC

- GC优化
  - Nino实现了高性能动态扩容数组，通过这个功能可以**极大幅度**降低GC（20M降到5M，甚至搭配对象池可以降到100K）
  - Nino底层用了对象池，实现了包括但不限于数据流、缓冲区等内容的复用，杜绝重复创建造成的开销
  - Nino在生成代码后，通过生成的代码，避免了装箱拆箱、反射字段造成的大额GC
  - Nino在序列化和反序列化的时候，会调用不造成高额GC的方法写入数据（例如不用Decimal.GetBits去取Decimall的二进制，这个每次请求会产生一个int数组的GC）
  - Nino改造了很多原生代码，实现了低GC（如Unity平台下DeflateStream的底层被改造了，实现了复用）
- 速度优化
  - Nino缓存了类型模型
  - Nino生成代码后直接调用了底层API，**大幅度**优化性能（速度快一倍以上）
  - Nino底层写入数据的方法经过测试，不比用指针这些不安全的写法写入的速度慢
- 体积优化
  - Nino写Int64和Int32的时候会考虑压缩，最高将8字节压缩到2字节
  - Nino采用了C#自带的DeflateStream去压缩数据，该库是目前C#最高性能压缩率最高的压缩方式，但是只能用DeflateStream去解压，所以在其他领域用处不大，在Nino这里起到了巨大的作用



### 体积（bytes）

![i1](https://s1.ax1x.com/2022/06/15/XowpM4.png)

> Nino < MsgPack (LZ4 Compress) < Protobuf-net < BinaryFormatter < MongoDB.Bson
>
> 体积方面可以忽略是否预热

### 序列化速度（ms）

![i2](https://s1.ax1x.com/2022/06/15/XodP4f.png)

> Nino Code Gen < MsgPack (LZ4 Compress) < MongoDB.Bson < Nino Reflection < Protobuf-net < BinaryFormatter

### 反序列化速度（ms）

![i3](https://s1.ax1x.com/2022/06/15/XodCUP.png)

> MsgPack (LZ4 Compress) < Nino Code Gen < MongoDB.Bson < Protobuf-net < Nino Reflection < BinaryFormatter





## 非Unity平台性能测试

``` ini
BenchmarkDotNet=v0.12.1, OS=macOS 12.0.1 (21A559) [Darwin 21.1.0]
Intel Core i9-8950HK CPU 2.90GHz (Coffee Lake), 1 CPU, 12 logical and 6 physical cores
.NET Core SDK=6.0.301
  [Host]   : .NET Core 5.0.11 (CoreCLR 5.0.1121.47308, CoreFX 5.0.1121.47308), X64 RyuJIT  [AttachedDebugger]
  ShortRun : .NET Core 5.0.11 (CoreCLR 5.0.1121.47308, CoreFX 5.0.1121.47308), X64 RyuJIT

Job=ShortRun  Platform=AnyCpu  Runtime=.NET Core 5.0  
IterationCount=1  LaunchCount=1  WarmupCount=1  

```

| Method                |         Mean |  Error |      DataSize |        Gen 0 |        Gen 1 |        Gen 2 |      Allocated |
| --------------------- | -----------: | -----: | ------------: | -----------: | -----------: | -----------: | -------------: |
| **MsgPackSerialize**  | **1.746 ms** | **NA** |   **3.36 KB** |        **-** |        **-** |        **-** |    **3.45 KB** |
| **NinoSerialize**     | **5.851 ms** | **NA** |   **1.79 KB** |        **-** |        **-** |        **-** |    **4.07 KB** |
| **ProtobufSerialize** | **3.374 ms** | **NA** | **615.24 KB** | **671.8750** | **593.7500** | **589.8438** | **3041.56 KB** |

### 说明

非Unity平台下，测试序列化**一万个数据**时，Nino序列化速度对比MsgPack和Protobuf会比较慢，但是**全部库都比Unity下测试快数倍**，这是因为**用了.net core和JIT**的原因，unity下测试是mono或il2cpp环境。

### 性能

- MsgPack和Protobuf在**非Unity环境用了Emit**，实现了高性能和低GC，在**Unity下**由于Emit技术的**限制**，相同测试下MsgPack和Protobuf的**GC是MB为单位**的，耗时也是Nino的数倍
- 由于Nino还**没实现Emit**技术的原因，在**非Unity平台**下，Nino序列化的速度**对比其他库较慢**，但是差距**不到一倍**，同时比Unity下快不少

- **Nino无论在任何平台，GC都把控的很出色，可以在非Unity环境实现与MsgPack相差不大的GC（相差不到1KB）**

- Nino在**Unity平台**是目前当之无愧的**性能最卓越**的二进制序列化库，真正意义上实现了**全平台都低GC**，并且在**Unity平台下**的**序列化速度远超同类**
- 之后Nino**补上Emit后**，**非Unity平台**的性能和GC可以**与MsgPack持平**

### 体积

- Nino的体积依然是最小的，**体积**是MsgPack LZ4压缩的**一半**，是Protobuf的**数百分之一**

- 在**全部C#平台**下，Nino序列化结果的**体积**是**当之无愧最小**的，最利于数据存储和网络通信的。
