# 在另一台电脑审查与复测

## 只审代码

从 README 的命令拉取 test 分支，比较 `cb8577e3..fddb33ae`。后续 Review 目录提交只是交接材料。本次整理没有改动插件实现，也没有重新执行此前已完成的引擎矩阵。

关注共享 staging 的 receipt 校验、NonUFS 文件登记、发布失败传播，以及 5.6 的 `ModifyDeploymentContextCallback` 入口。`ProjectUtilities.GetUnrealSharpUPlugin` 已恢复基线实现，重复安装优先级不属于本功能。

## 准备复测环境

1. 使用 Windows、对应的 Installed UE 5.6/5.7/5.8，以及插件要求的 .NET SDK。原测试使用 5.6.1、5.7.4、5.8.3，managed SDK 10.0.400；不同版本的结果应另行记录。
2. 每个引擎准备两个专用 C++ 测试项目 A/B。原命名为 56 的 SharpTestProject56_1/56_2、57 的 SharpTestProject57_1/57_2、58 的 SharpTestProject/SharpTestProject1。完整项目与 Unreal 资产不在此分支中；使用自己准备的测试项目，不假设只有 probe 即构成可运行项目。
3. A 的项目插件部署 `cb8577e3`，B 部署 `fddb33ae` 的完整源码快照。不要将 test 分支的 Review 工具目录当作插件运行内容。三个 Engine/Plugins 中均不放重复的 UnrealSharp。确认实际 native receipt 与发布命令选中同一份项目插件。
4. 阅读各项目现有 AutoBuild、AutoBuildGame 和其配置，核对引擎、项目、Target、平台、配置、架构及全部参数。编译仅通过这些正式入口；入口缺失时先为项目建立正确入口，不从本文推断 UBT 命令。
5. 迁移 `tools/amend-harness/` 到自己的测试工具目录；按目标路径调整 Python 的 W/REPO/ROOT/R/TOOLS/project 映射、PowerShell 的 reports/project/engine 校验、csproj 的 HintPath/导入路径，以及独立程序集分析工具的路径。`AmendPackaging.py` 的 HEAD 依赖原功能分支名；另机应明确固定为 `fddb33ae`。PRE 指向旧历史，当前 test 分支不保证含该对象；普通 A/B 复测不需要执行 pre-amend 路径。
6. 使用新的报告与 Saved 目录，避免覆盖历史记录。fixtures 对应的 probe 放入测试项目 Script 下，并按项目插件的正式生成/初始化流程加入 solution。csproj 中的 plugin/props/引用路径必须匹配实际项目布局；内容文件按对应 probe 项目的 CopyToPublishDirectory 规则发布。

原始脚本带有部署、修改 probe 和构建操作，且部分依赖先前本机报告。按下面顺序选择需要的操作；不要直接批量运行所有历史 prepare/deploy/report 脚本。

## 执行顺序

1. 检查同引擎/项目的构建、Editor、Commandlet 与游戏进程占用，串行执行项目 canonical Editor AutoBuild 和 Game AutoBuild，分别确认退出成功。原矩阵为 Win64 x64 Development Game。
2. 通过项目正式流程生成/构建 managed solution。先确认基础 Editor 和 Game 输出可用；不在已有 native 构建运行时部署插件或启动验证。
3. 使用 `InvokeAmendPackaging.ps1` 中 Identity/Boundary 操作核对插件路径与 receipt。边界工具在 harness 的 56/57/58 子目录；测试时复制匹配版本到对应项目工具目录并修正引用。每版预期 18 项通过，包含插件身份不匹配拒绝。
4. 执行完整 Cook/Stage/Archive。命令模板如下，只需按本机路径替换；native build 已在步骤 1 完成，所以这里保留 skipbuild。

```text
<Engine>/Engine/Build/BatchFiles/RunUAT.bat
  -ScriptsForProject=<Project.uproject>
  <Command>
  -project=<Project.uproject> -target=<GameTarget>
  -platform=Win64 -clientconfig=Development
  -skipbuild -nocompileeditor
  -cook -map=/Engine/Maps/Entry -stage -pak -archive
  -stagingdirectory=<FreshStageDirectory>
  -archivedirectory=<FreshArchiveDirectory>
  -unattended -utf8output
```

这是多行展示的参数模板，执行时组合为正确的 shell 参数。B56 的 Command 为 `BuildCookRunUnrealSharp`，B57/B58 为 `BuildCookRun`。A 全部使用标准 `BuildCookRun`，检查其自动 staging 没有 managed 文件，再按 runner 的 Manual 操作执行旧 `PackageProject`，构成两步对照。不要为 B 追加手工发布，否则无法证明自动集成生效。

5. 从 Archive 中实际运行游戏，参数使用 `/Engine/Maps/Entry -nullrhi -unattended -nosound -ExecCmds=Quit`。按 runner 设置 `PACKAGE_STAGE_PROBE_RESULT`，核对 probe 版本、内容、Game/Development 和 Archive 下的程序集/运行时路径，不能只检查 UAT 成功退出。
6. 核对 Publish、Stage、Archive 中 managed/runtime 文件的相对路径、大小和 SHA256；对照真实 UHT 文本与公共程序集 API/IL。native up-to-date 时应说明这是已有生成输出的核对，不能声称新执行了 UHT。

## 回归与性能

1. 只修改 C# probe 和内容版本，保留 native 输出，使用 `-skipcook` 代替 cook/map 再 staging，确认新包运行新版本。
2. 注入一处可恢复的 C# 编译错误：发布应非零退出，旧归档哈希不变。恢复源码。
3. 使用 `-skipstage -skippak` 检查保留旧归档，随后正常 staging 确认恢复后的版本。记录 staging 前后 native/Editor 输出哈希，验证未被改写。
4. 每版 A/B 各预热一次，再按 A-B、B-A、A-B 取三轮独立 UAT 样本。A 计 Stage+Manual，B 计自动 Stage；每次都运行 probe 确认有效工作，但 native build、cook、游戏启动不计入这个时间指标。不要把三轮小样本当作普适性能结论。

原测试的详细结果在 validation.md。复测应生成自己的日志和结果，不能用本分支的总结替代执行证据。5.6 默认 Editor 打包按钮、Mac 签名和移动端均不在已通过结论中。
