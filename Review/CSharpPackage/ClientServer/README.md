# Client/Server 工具与总结

UE 5.8.3 source build，Engine-level UnrealSharp，Win64 x64 Development：实际点击官方 Editor Package Project，CSharpPackageClient 和 CSharpPackageDedicated 均自动打包成功；包内 C# 分别识别 Client/Server，加载归档内 runtime，正常退出。没有单独补跑 PackageProject。Mac 尚未实测。

`fixture/` 是必要的纯文本测试项目模板及原执行工具，包含 canonical AutoBuild、Target、C# probe 和三个 PowerShell 脚本。不含引擎、资产、生成 glue、日志或构建输出。设备连接配置未同步。

这些是 Windows 工具，原布局为 `<WORKSPACE>/UnrealEngine2/USharpTests/CsPkg`。路径配置为 `project_env.config.txt.example`；另机使用前填写正确路径并检查脚本中的报告/源码目录。PrepareClientServer 依赖预先导出的 b59ff754 源码快照、干净宿主和该 revision 的源码 checkout，不要在 test 分支直接执行或对已有工作项目执行。

原顺序：canonical Editor/Client/Server/UnrealPak 构建全部成功 → 正式 managed 构建 → Editor（Build=Never）→ 官方菜单分别选 Client/Server 打包 → VerifyPackagedRuntime 检查归档。保留默认插件配置，Exclude editor content when cooking 为 false；短路径避免已遇到的 ISPC 长路径问题。

本次仅同步，未重跑测试。Mac 请使用当地项目已有的 canonical 构建入口和对应 Editor 打包流程；这些 Windows .bat/.ps1 不能直接当作 Mac 构建入口。
