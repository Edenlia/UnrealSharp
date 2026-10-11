# 官方 UE 5.8 插件打包验证

Engine level 和 Project level 安装可用，并不保证官方 BuildPlugin 的完整流程可用。安装位置主要影响插件、项目、manifest 和 UAT script 的发现；BuildPlugin 的 Target 和生成范围是另一层验证。

## 为什么普通使用没有暴露这些错误

BuildPlugin 创建独立 HostProject，先构建 UnrealEditor Win64 Development，再构建 UnrealGame Win64 Development 和 Shipping。官方 UnrealEditor.Target.cs 开启 bBuildAllModules；UBT 因此将更多有效 Engine 模块加入 Target。它们不一定是默认启用的插件。

UnrealSharp exporter 遍历整个 UHT session，Engine bindings 又递归编入一个 C# assembly。全新插件目录触发全量导出，因此不曾被当前项目覆盖的反射类型也可能让编译失败。既有项目的 Target 和旧生成件不能证明这些类型已正确生成。

Editor 编译还使用 UnrealEd shared PCH，而本次 UnrealGame 编译没有该 PCH。源文件依赖 PCH 间接带入的 Core/UObject/Engine 声明，会在 Game 中暴露为缺少头文件。修复直接依赖，而不改变 Target、Unity、PCH 或 Build.cs。

## 修复范围

基于 feature/engine-level-plugin 的修复覆盖枚举别名、接口命名、显式接口事件实现、managed 方法/property 名冲突、嵌套结构体默认值、缺少可选 Config 目录、原生头文件依赖和分发文件过滤。

结构体非数值默认值使用 FProperty 原生 ImportText 语义，复制到 managed value 后释放临时 native 值；内置数值元组继续使用原字段顺序。显式接口实现沿用 Interface.Wrap 的既有策略：非 BlueprintCallable 事件仍查找首个原生实现，不能据此声称所有 Blueprint override 都已验证。

FilterPlugin.ini 包含 managed assemblies、项目/Automation 源码、Config、Templates、props，以及 Editor/Game 生成的 C# bindings 和 defines。排除 managed/UAT/UHT 扩展项目的生成缓存、测试程序集和含 executable 的旧 BuildHost-net472；保留完整 BuildHost-netcore。该包面向 SDK-style C# 项目。

## 验证方法与结果

使用 Epic Launcher 官方 UE 5.8.2（CL 56702186），插件源码位于引擎目录外，在独立新目录运行用户指定命令：

```text
Engine\Build\BatchFiles\RunUAT.bat BuildPlugin -Plugin=<源码目录\UnrealSharp.uplugin> -Package=<新输出目录> -Rocket
```

2026-10-11，Win64 全流程成功，整体 ExitCode=0。UnrealEditor 225 个动作、UnrealGame Development 和 Shipping 各 160 个动作均成功；managed build 为 0 errors、25 warnings。10 个生成代码回归样例通过。

最终 Package 在 HostProject 删除后审计：三份官方 UBT XML 清单的 117/242/242 项插件产物全部存在；Editor .modules 与 Game .precompiled 的实际输出齐全。managed 依赖、源码和资源齐全，保留 Editor 34,644 个和 Game 14,872 个 UHT C# 文件；未发现 .exe/.com/.msi、测试程序集或上述生成缓存。

BuildPlugin 的 Game 阶段只验证 UHT 和 native build，当前 managed build 仍只在 Editor 阶段触发。本次未验证实际游戏 managed publish/AOT、PIE、热重载、默认值的运行时调用、Blueprint override 或 Mac。外部 .NET 10 SDK、Roslyn/MSBuild 等依赖仍须按 Fab 要求申报；编译成功不等于第三方许可或 Fab 审核通过。
