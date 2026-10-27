> 2026-10-27 test 分支交接：本文保留原验证阶段记录。原始证据未随分支发布；跨电脑审查及复测见 [README](README.md) 和 [复现步骤](REPRODUCE.md)。功能 HEAD 仍是 fddb33ae，test 分支额外携带工具与文档。

# C# 自动打包工作日志

## 2026-10-25

- 读取 unrealsharp-pr 技能及 A/B、正式验证、测量、报告参考，读取工作区成对规则与权限配置、OpenSourcePR/README.md。
- 验证 fork remote、工作树与当前历史：原 feature/csharp-package 干净，位于 upstream/main cb8577e3；fetch 后主线未改变。按用户原文新建 featre/csharp-package，保留原分支。
- 查阅在线 packaging 文档、#244、#785 与 #786：现有流程仍要求 UE 打包后另跑 managed 发布；#785 仍开放且有合并冲突，不能假设已合入。
- 已发现既有 csharp-stage/package-target 调查目录，后续仅将其作为线索并重新核实当前源码。
- 下一步：核查默认测试环境、项目规则和 AutoBuild；完整比较插件构建/暂存扩展点后更新实现方案。

- 完整核对 UE 5.8.3 Installed 源码：插件 Automation 可直接发现，handler 自动注册；PreBuild 过早，PostBuild 受 receipt 缓存且 skip-build 不调用。源码引擎项目插件仍需显式 ScriptDir，选择保留这个入口边界而不生成项目桥接工程。
- 新增插件 handler 初版及两个最小配套改动。根据 receipt 激活、拒绝混合目标/配置和 Universal、发布到 Intermediate，再登记 NonUFS。未改任何 Project Target/uproject/AutoBuild。
- 准备 A/B 插件完整 fork 快照。原有插件、Script、项目 managed 输出与 UHT 输出移至各项目 Saved/UnrealSharpAB/20261025-csharp-package/before；B 的遗留 Build/UnrealSharp 桥接也移开。部署清单见 deployment-A.json、deployment-B.json。原生中间缓存未删除。
- 两项目加入同一最小 PackageStageProbe（仅测试目录），带 UModule 输出版本/真实程序集路径/target 宏及发布内容文件。已开始 A 的既有 AutoBuild Editor 构建，日志 build-A-editor.log。

- A/B 既有 Editor、Game AutoBuild 均退出 0：A Editor 130.16s、B Editor 172.11s；A Game 56.96s、B Game 63.84s（UBT 内部记录）。B Editor 期间发现另一个任务在 UnrealEngine1 构建；这组首次编译数据有后台负载混杂，不作为 feature 性能结论。原先 harness 把所有 cl.exe 当成冲突，已修正为本机 InstalledBuild5.8/两个测试项目的实际进程范围，不终止外部任务。
- 官方 BuildUserSolution 在 A/B 均退出 0。首次 A Cook 失败于夹具：UnrealSharp 使用 LoadFromStream，测试 UModule 的 Assembly.Location 为空；新建 Script 还没有初次启动生成的 RuntimeGlue 工程，Cook 中的嵌套 UAT 因外层 UAT mutex 被拒绝。保留 first-A-cook-stage.*，没有把失败归因于候选 handler。
- 修正两边相同探针为 UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory，重新官方 managed 构建，并启动无窗口 Editor 初始化 RuntimeGlue。A 日志已出现 PACKAGE_STAGE_PROBE version=1 target=Editor content=package-content-v1 和正常 Quit。
- A 初始化 Editor 未实际退出，核对本任务 PID/命令行/日志后停止该唯一进程，记录 initialize-A-owned-stop.json；B 改用 Python commandlet 初始化并正常退出 0。
- A Cook 成功后 Stage 被本机 Zen IPv6 代理错误阻断：.NET DefaultProxy.IsBypassed([::1]) 为 false。仅 UAT/运行测试子进程追加 localhost/127.0.0.1/::1/[::1] 到 NO_PROXY（A/B 一致），未改原生构建环境或引擎。重跑 A Stage 成功，托管目录缺失符合基线；保留失败日志。
- B 首次完整 Cook/Stage/Archive 退出 0，自动发布一次，NonUFS 271 项，实际打包程序日志加载 archive 内的 hostfxr、UnrealSharp、用户模块和 RuntimeGlue。首次 GUI 程序由 PowerShell 调用未取得可靠 exit code，Console.WriteLine 未进入引擎日志；测试 runner 改为 Start-Process -Wait，探针增加独立结果文件，随后重测，不把首次 null exit 当成功退出。
- 实际运行 A/B Game/Development 探针均成功，C# v1→v2 和发布内容 v1→v2 均更新；同侧原生 EXE 与 Editor probe 哈希不变。注入 #error 后 UAT 返回 1，未进入 Archive，旧归档 DLL 哈希不变；恢复后成功。
- 12 项基于真实 receipt 的内存边界检查通过（真实 handler DLL）：目标过滤、disabled、混合目标/配置、Universal Mac、Android、异项目、DLC。它们不是这些平台的真实打包测试。
- 初次 SkipStage 断言失败：夹具同时传 -pak，UE 的 ShouldCreatePak 仍触发 CreateStagingManifest/ApplyStagingManifest，实际更新到 v3。这是对 UE flag 语义的错误预期，不改插件去偏离正常调用链；改用 -skipstage -skippak 验证真正复用暂存目录，并保留失败证据。
- 全量产物审计的初版遍历了 bin/obj 内部缓存，影响并发增量用时；停止本任务 reader 后改为遍历时剪枝，无源码输出变更。无改动性能采样在审计结束后单独顺序执行，增量初轮用时不作性能推断。
- 完成源码 staged diff 审查及 diff --check，仅 3 个插件文件。核对 B 部署三文件 SHA256 与候选一致后提交 1723b7ba619b8e4ee778dde672cf9983e63cd6a9（Publish managed code during Unreal staging）；无后续修补提交。真正 skipstage+skippak 验证保留 v2，未发布 v3。项目 AutoBuild/uproj/env 控制文件哈希全部不变。
- 产物审计完成：Editor 插件 21978、项目 12；Game 插件 15219、项目 12 个生成文本文件全部逐字节相同。首次性能 gate 过早释放，noop-A-1 与本任务 warmup UAT 重叠，被 UAT singleton 拒绝（未执行实际任务）；保留失败，修正为单进程顺序 campaign，以全新 perf 标签重测，不计入性能结果。
- 最终无改动顺序 A1-B1-B2-A2-A3-B3：全部 UAT/游戏退出 0，Game/Development v2+内容 v2。A 总流程秒 [51.998,15.999,14.125]；B [46.733,47.806,56.318]。候选中位数慢 31.806s（198.8%），未筛掉慢样本。保留 performance.json。
- 为解释大幅波动，独立带时间戳诊断：managed publish 9.300s；Zen oplog 前就绪检查到启动服务间等待 26.522s。本机 UE 源码有 25s readiness polling，记录 engine-artifacts.json，未改 Engine 或常驻 Zen 以影响结果。
- 第二轮仅 C# /内容增量 v2→v4：A 54.360s、B 50.855s，单次有限证据。两边运行 v4 成功，原生 EXE/Editor probe 哈希仍相同。测试槽最终保留 v4，A 基线/B 最终候选。
- 最终每包 225 文件；B Publish/Stage/Archive 全部逐项同 hash。所有差异保存在 published-comparison、文本 full diff 和 PEReader 元数据/IL 记录中。共同 7 个 DLL API/IL 相同；probe 只差 file-local registrar 名。RuntimeGlue 的 A 比 B 多一个既有 Map 资产 ID 字段，加上项目名差异，完整源和 IL diff 已保留；不把该输入差异归因于 feature，也不伪造整体相同。
- 完成中文 validation.md 与英文 pr.md；明确源码引擎项目插件仍需 ScriptDir、Mac 实机签名未执行、旧 UE/其他真实目标未验证，以及本机观察到的端到端耗时增加。最终仅本地提交，无 push/PR/main/cp-main 集成。
- 最终交付复核：工作树干净，完整 base..head diff --check 通过，B 部署三文件与已提交候选哈希一致，项目控制文件未变化。fixture-manifest.json 保存测试脚本身份，英文草稿未含本机用户名、绝对路径或私有测试项目名。验证报告已请求在 Codex 打开。

## 2026-10-25 UE 5.6 / 5.7 追加验证
用户要求保持代码不变，验证两个旧版并分析问题。锁定 1723b7ba。发现既有 SharpTestProject56 / 57 分别绑定 Installed UE 5.6.1 / 5.7.4；原样 AutoBuild 入口齐全。5.6 缺少 CustomStagingHandler；5.7 提供该 API。部署完整 git archive，旧插件、5.7 旧桥接及项目托管输出移至各项目 Saved/PackageCompatibility-20261025/before，避免旧实现干扰。未修改源码、项目配置、测试 C# 或构建入口。原始证据目录：compat-ue56-ue57-20261025。

UE 5.6：原样 Editor/Game AutoBuild 均成功，UAT -List 自动发现项目 Automation。普通 Cook/Stage/Archive exit 0，但 Stage、Archive、Intermediate 发布目录均无 Managed。Cook 日志另有 C# 父类缺失：此前移走托管输出后未进行非 Cook 初始化，Cook 模式跳过用户构建，此次不能作为资产运行通过。手工 PackageProject exit 0，但日志显示错误选中 Engine/Plugins/Marketplace/UnrealSharp，并在那里生成 bin/obj 托管构建输出；因此该次回退验证无效，不继续执行混合产物。预检遗漏了 Marketplace 路径，已经递归核实：仅 5.6 存在重复插件，5.7 无引擎 UnrealSharp。源码 GetUnrealSharpUPlugin 取 EnumeratePlugins 首项，与 UE 项目覆盖优先级不同。保留共享引擎原状，不移走、不修复。继续 5.7 并补足正常 Editor 初始化流程。

UE 5.7：原样 AutoBuild Editor 在 UHT exporter 阶段报 System.ArgumentOutOfRangeException，exit 6。未清缓存/重建/换入口，未运行 Editor/Game/Cook。独立 UAT -List exit 0；其构建记录包含 UnrealSharpStagingHandler.cs，证明 API 编译和插件发现通过。静态定位既有 DefaultGuardProbe.h 的嵌套 Transform 默认值：SimpleTypePropertyTranslator.Split(',') 得到 10 段，却索引只有 3 项的 Children，可触发同类型异常；原始日志无完整堆栈，未改代码动态缩小首个失败参数。默认值解析和插件选择代码与 PR 基线一致。检查已有独立修复 ab0f42a8（default-value-parser）及 932f2bfc（plugin-find），仅列为后续候选，未合入。
进一步更正初始化原因：CSDotnetUtilties.cpp 在 FApp::IsUnattended() 时跳过用户构建，条件不限于 Cook；因此带 unattended 的空 Python 初始化也不足，应显式使用官方 managed 构建命令。5.7 没有运行该辅助脚本。
最终两个部署各 827 个版本管理文件全部仍匹配精确候选，源仓库 clean。5.6 的测试 Source/Config/Script 输入无变化；5.7 官方 UHT 更新一个 csproj 引用和 solution projects JSON，其余预先记录输入未变。所有任务测试进程已退出。完成 compatibility-ue56-ue57.md，并更新计划、总验证报告和英文草稿的旧版支持声明。没有修改实现、增加提交或发布。

## 2026-10-25 继续多版本 PR 实现
用户要求按更新 skill 实现 5.6/5.7 支持，允许不同入口和提交。选定 56_1/56_2、57_1/57_2，旧项目保留历史证据。重新 fetch upstream/main 仍为 cb8577e3；#785/#786 open。新 5.7 项目没有 DefaultGuardProbe，不纳入 parser 修复。四个项目缺少 Game AutoBuild，用户回复“可以做 我同意了”；已按每个现有脚本和配置新增 Game/Win64/Development 入口，未改变其他选项。
四项目部署完整 fork 基线/候选快照，原内容保存在 Saved/CSharpPackageMulti-20261025/before。使用一致 UModule 探针和附带内容文件，避免旧功能夹具混入打包验证。
第 2 步 resolver A/B：在同一 UE 5.6 安装运行真实方法。A 选择 Engine/Plugins/Marketplace，B 选择项目插件。只查询路径，没有执行错误来源发布。测试 Automation 初次缺少 TargetFramework 导致 UAT CsProjBuilder NullReference；补齐测试工程 net8.0 后两次 exit 0，原始失败保留。源码只复用 932f2bfc 的 5 行新增/16 行删除改动；已审查并提交 Resolve the publishing plugin using UBT precedence。证据目录 multi-version-20261025。

### 2026-10-25：旧版入口实现与首轮执行

- 89707cded9b453751d11525170354b0c03133025 已提交，独立负责插件优先级；基线 resolver 选 Engine/Marketplace，候选选项目插件。
- 将 receipt 校验、发布及 NonUFS 登记移至共享 UnrealSharpStaging；5.6 的 BuildCookRunUnrealSharp 通过 ModifyDeploymentContextCallback 接入，新版保留 CustomStagingHandler。
- 两个 B 槽均从 89707cde 完整快照加四个精确 pending 文件部署，源哈希记录在 multi-version-20261025/step3-pending-source.json，旧插件移至各项目 Saved/CSharpPackageMulti-20261025/step2-plugin。
- 56_2-List-step3 与 57_2-List-step3 均成功：仅 5.6 暴露新命令。没有添加项目 Automation 桥接。
- 56_1 原始基线的 canonical AutoBuild Editor、Game 均成功；分别约 99.28 秒和 73.10 秒。随后官方 BuildUserSolution 成功，开始 Cook/Stage。
- 测试 runner 串行执行每个项目的构建、managed、Cook/Stage/Archive，失败立即停止。所有命令、退出码与耗时保留在独立 result/command JSON，不把 runner 启动或日志尾部当通过。

### 2026-10-25：初始化失败与有效对照范围

- 首次 56_1-CookStage 失败（ExitCode=25）：RuntimeGlue 项目尚不存在，Editor 启动时 AddNewProject 嵌套执行 GenerateProject UAT，父 BuildCookRun 持有 UAT 单实例锁。原始日志保留，未清缓存、未改 native 构建入口。
- 修正测试前置：在独立 UAT 进程中调用官方 GenerateProject 创建 RuntimeGlue（GenerateSolution + RunUSharpProjectSetup），随后 BuildUserSolution，再 Cook。该动作属于已有项目初始化，不属于本 feature 的额外项目桥接。
- 5.6 原始 resolver 会让上述 GenerateProject 引入 Engine 副本，因此不进一步执行错误源构建。保留 cb8577e3 的完整插件与真实原生生成结果在 56_1/Saved/CSharpPackageMulti-20261025/original-base-plugin。
- 56_1 额外手工对照改用完整 89707cde 快照；5.6 在此版本不编译 handler，实际是原发布逻辑 + resolver 修复。重新执行 canonical Editor/Game，再测试标准 staging 缺失 managed 和手工发布。这不是原始基线全链通过，最终报告将明确区分。
- 新版共享代码尚未提交，等待候选真实 packaged probe 与边界验证。

### 2026-10-25：5.6 UHT 类型加载依赖

- 候选首次 56_2-BuildEditor 失败（ExitCode=6），真实 UHT 已导出 184 个原生文件，随后调用 UnrealSharp 命令派发时报 ReflectionTypeLoadException：AutomationScripts.Automation 5.6.0.0 不在 UHT 加载环境。
- 原因：UnrealSharpManagedGlue.ubtplugin 引用 UnrealSharp.Automation；CommandUtilities.RunCommand 使用 Assembly.GetTypes()，新增继承 BuildCookRun 的命令使枚举提前解析 UAT 专用父类。
- 修正同一未提交步骤：按完整类型名查找目标命令；保留 BuildCommand 类型检查、参数转发和缺失命令异常。没有将 UAT 脚本依赖复制进 UHT，没有修改 Engine。
- 三个 B 槽仅更新精确 CommandUtilities 源文件，原有完整部署快照和其他源码不变；未移动/删除失败构建的 native 输出或缓存。step3-v2-source-delta.json 记录唯一增量，原样 AutoBuild 重试日志使用 56_2-BuildEditor-v2，原始失败保留。

### 2026-10-25：5.6 候选首轮通过

- 56_2-BuildEditor-v2 与 56_2-BuildGame 原样 AutoBuild 成功（UBT 36.62 / 59.53 秒）。第一次 UHT 失败已导出文本但未完成 managed bindings；重试 native 后缺少这些 managed DLL，导致 56_2-Initialize 首次失败。通过现有官方 BuildSolution 命令（与 BuildUtilities.BuildBindings 相同 Folders、TargetConfiguration 参数）补齐纯托管输出，56_2-Initialize-v2 和 Managed 成功。未使用替代原生编译或缓存清理。
- BuildCookRunUnrealSharp 首轮 Cook/Stage/Archive 成功，Stage 26.98 秒，整次 UAT 52.63 秒；日志发布位置是隔离 Intermediate/UnrealSharp/Publish/.../Game 目标，最终 NonUFS 清单包含托管产物。
- 56_2-Run 退出 0，probe JSON 确认 version=1、Game/Development、package-content-v1，用户程序集与 runtime 都从 Archive/Windows 下加载。
- 17 项边界检查通过（激活、无插件/Editor/空 context、多目标/配置、移动端、Universal Mac、错误项目、DLC、缺失命令）。这是 synthetic receipt 检查，不是 Mac/mobile 实机打包。
- 开始 5.7 原始基线及候选的独立构建与全链验证；5.6 新源码已暂存但未提前提交。

### 2026-10-25：旧版支持提交与最终回归

- 新提交 777b1423（Add managed staging to the UE 5.6 packaging command），包含独立入口、共享 staging、条件编译和 UHT 按名命令解析。暂存 diff/check 已审查，没有报告、测试工具或项目文件进入提交。
- 5.7 原始基线和候选 Editor/Game AutoBuild 均成功；候选标准 BuildCookRun 首轮 Cook/Stage/Archive 成功（Stage 26.10 秒），包内 probe 与 runtime 路径正确，17 项边界通过。原始基线标准 Stage 不带 managed，手工发布后运行成功。
- 5.6 原始基线 UHT 与候选比较：Editor 16,778 + Game 11,652 = 28,430 个文本文件全部一致；测试项目本身无额外 native UObjects，因此 project UHT 输出为 0。220 个候选发布文件在 Publish/Stage/Archive 清单、大小、SHA256 完全一致。
- 5.6 额外手工对照与自动候选的 6 个公共 UnrealSharp DLL API/IL 检查记录一致。探针差异仅 compiler-generated file-local registrar 名；RuntimeGlue 文本相同，程序集属性仅含各自项目名称。保留完整 manifests、binary hashes、PE metadata/IL records 与 diff。
- 三个 B 槽五个变更文件与提交前工作树逐项同哈希；final-candidate.zip 固定交付快照。5.8 已部署同一候选，并开始 canonical Editor/Game + managed + Stage/Run 回归。

### 2026-10-25：5.7 产物比较与 5.8 回归前置

- 5.7 Editor 17,369 + Game 11,885 = 29,254 个 UHT 文本产物一致；project UHT 无额外类型。两版本共 57,684 个插件 UHT 文本产物均未变化。
- 5.7 候选 220 个发布文件在 Publish/Stage/Archive 完全一致；与原始基线手工发布的差异分类同 5.6：201 个文件逐字节一致，7 个公共命名 DLL/PDB 对与 UserCode.LoadOrder 的差异已保留完整清单；6 个 UnrealSharp DLL API/IL 记录相同，probe 仅 file-local registrar 名不同，RuntimeGlue 仅项目程序集属性不同，生成源码完全一致。
- final-source-verification.json 验证：A56 为 89707cde（827 文件）、A57 为原始基线（826 文件）；B56/B57/B58 均为 777b1423（829 文件）。只接受已知 C# 文本 Git CRLF/LF 差异，其他内容逐字节匹配。
- 5.8 候选 Editor/Game AutoBuild 成功（188.38 / 94.91 秒），Managed 成功；复用旧 cook 数据的 Stage 失败，原因是 IoStore package/project store 缺失。托管发布本身已成功。改为正常 CookStage 重新生成所需数据，未改 source、Engine 或 native 选项；失败日志保留。

### 2026-10-25：最终功能回归完成，开始 warm 性能样本

- 5.8 正常 CookStage/Archive 成功（Stage 41.35 秒，整次 UAT 约 84 秒），归档游戏运行 version=4、Game/Development、package-content-v4；17 项共享边界检查通过。225 个 managed/runtime 文件的 Publish/Stage/Archive 哈希一致；重建后的 37,221 个 UHT 文本文件与原已验证 B 产物完全一致。
- 5.6/5.7 候选均完成 managed-only v1→v2 更新与内容文件更新；故意注入 C# 语法错误使 UAT 非零退出且旧归档哈希未变；恢复源码后 skipstage+skippak 继续执行旧 v2；再次正常 Stage 切回 v1。两版本原生输出、项目 Editor managed 输出、插件原生输出和插件 managed 输出的完整 before/after 哈希均一致。
- 5.6 foreign/foreigncode 都在 SetupParams 前明确拒绝，未创建外部样本工程。
- 编译、生成文本和程序集分析均结束后，开始 5.6/5.7 同机串行 warm 采样。每版先 A/B 各一次预热，再按 A-B、B-A、A-B 顺序各三次；A=Stage+手工 Publish，B=自动 Stage。每轮额外运行归档 probe 检查有效工作，probe 启动时间不计入 staging 指标。原始数据逐轮写入 performance-raw.json。

### 2026-10-25：最终性能与交付核对

- 5.6 三轮 A=10.4128084/10.8297489/9.7669838 秒，B=9.7552632/9.7466842/9.8776415 秒；中位差 -0.6575452 秒（-6.31%）。5.7 A=10.6161119/10.5101533/9.6961868 秒，B=11.4293056/9.632436/10.8720971 秒；中位差 +0.3619438 秒（+3.44%）。两版样本范围重叠，未发现稳定提速证据；保留所有预热和正式样本，没有剔除较慢样本。
- 采样过程中所有包内 probe 均为 v1、Game/Development、package-content-v1，runtime 来自 Archive。性能不包含 native/cook 或 probe 启动，也不将原始 5.6 失败耗时纳入。
- FinalizePackagingEvidence 验证 26 个成功 staging/skip-stage case 的 publisher 调用次数；四个新项目各 12 个原始 AutoBuild/配置/uproject/Source/Config 输入哈希不变；最终 B56/B57 的 220 文件复制链再次一致。
- 已审查完整 cb8577e3..777b1423 diff 和 diff --check；最终源码仓库工作树干净。三个有序提交保留，未推送或创建远程 PR。
- validation.md 重写为最终中文报告，pr.md 重写为英文上游草稿，旧版本已保存到 multi-version-20261025/validation-before-multiversion.md 与 pr-before-multiversion.md。计划同步完成状态、提交映射、版本入口和验证限制。
- 最终槽位：A56=89707cde 手工对照；A57=cb8577e3；B56/B57/B58=777b1423。B56/B57 源码恢复 v1，B58 原夹具 v4 保留。备份均在对应项目 Saved 下，不参与插件发现。所有本任务启动的构建/Cook/游戏进程已正常结束。

### 2026-10-25：移除重复插件 resolver 修复的本地 amendment

- 用户已移除 UE 5.6 Installed UnrealSharp，要求验证并本地去掉第二个 commit；同名插件优先级由另一 PR 负责。本次不合入该 PR、不改变原始基线。
- 重新核对仓库为 Edenlia/UnrealSharp，featre/csharp-package 工作树干净；Installed UE 5.6/5.7/5.8 的 Engine/Plugins 均未发现 UnrealSharp.uplugin。
- 原基线 cb8577e3a47b5bdc5454239a17f61a6282a50ae2；改写前 HEAD 777b14237afbcfea740458d1643f433ef7769751 保存为 backup/csharp-package-before-drop-resolver-20261025。
- rebase --onto 移除 89707cded9b453751d11525170354b0c03133025。1723b7ba619b8e4ee778dde672cf9983e63cd6a9 不变；777b1423 -> fddb33aefd598fa0d1eb5e324dc5ae36fb788a47，patch 等价。改写前后只有 ProjectUtilities.GetUnrealSharpUPlugin 恢复原始实现。
- 计划改为两项完整责任，删除 resolver 前置范围。旧 plan/validation/pr 保存于 amend-drop-resolver-20261025/before-*.md。新验证正在执行，不以 rebase 成功代替功能通过。

### 2026-10-25：amend 验证准备与改写前对照

- 当前远端只读查询 upstream/main 仍是 cb8577e3；未 fetch 合入或改动任何远端。
- 5.6 在用户移除 Engine 副本后的新环境，改写前 777b1423 全链 Cook/Stage/Archive/运行和 17 项原有边界通过；完成预热与三轮 warm 采样。该组日志沿用初始前缀 56_2-final-*，但实际是 pre-amend 源码；pre-amend-source.json 核对全部 829 文件并明确标注。最终改写版本统一使用 amended 前缀，不能混淆。
- 新测试脚本初次准备遇到 57_1 尚无 Tools 父目录，改为递归创建后恢复；没有影响源码或 native build。
- 部署期间串行读取 28,430 个生成文件较慢，只停止了已核对命令行的本任务 Python deploy 进程，没有停止编译或用户进程。改用 8 线程读取哈希并排除 bin/obj 后重新核对。保留全部原有 Binaries/Intermediate 输出，没有清理缓存。
- 六槽部署从完整 fork Git archive 快照逐项比对，仅写差异源文件及删除快照中不存在的已知旧源码；A56 从旧手工对照恢复为原始基线。已存在的源码无须复制或更新时间，以免造成无关重编译。
- 在候选 receipt 边界夹具中增加发布插件与 native receipt 不一致时必须明确拒绝的检查，不修改生产代码。该安全检查属于第一个 staging 设计，删除 resolver 修复后仍须保留。

### 2026-10-25：5.6 原始基线与去 resolver 候选通过

- A56 现在确为原始 cb8577e3（826 个 fork 源文件），B56 为 fddb33ae（829 文件）。新身份命令通过直接程序集引用验证实际加载插件 Build/Scripts/bin/Development/UnrealSharp.Automation.dll；两边均解析项目 UnrealSharp.uplugin。
- 两边 canonical Editor/Game 构建均通过，native target up-to-date；自动重编译受影响的 UBT C# 插件。没有强制 UHT/native rebuild，既有导出源码将按相同 native/generator 输入做哈希比较，沿用历史真实 UHT 执行证据。
- A56 标准 Cook/Stage/Archive 后手工发布和包内 probe 成功。B56 单次 BuildCookRunUnrealSharp 完成 Cook/Stage/Archive/运行；Game/Development、v1 内容和包内 runtime 路径正确。
- B56 18 项边界通过，新增插件/receipt 身份不匹配明确拒绝。原始 resolver 已不再阻塞当前无重复插件环境；不扩展为重复安装场景已修复的声明。

### 2026-10-25：三个版本新候选功能与负例通过

- 六槽原样 AutoBuild Editor/Game 全部成功；5.7/5.8 原始基线和候选完成全链打包并运行包内 probe。候选三版共 54 项 receipt/命令边界全部通过。
- 5.6/5.7 的 v1->v2、5.8 的 v4->v5 managed-only 更新均成功；三版故意注入 C# 语法错误均非零退出，归档 managed 哈希未变。源码恢复后 skipstage+skippak 仍运行之前版本，正常 staging 恢复 v1/v4。
- 三版 regression 脚本核对项目 native、Editor managed、插件 native 和插件 managed 输出 before/after 完全一致；源码和内容均已恢复。日志里的三个 Failure case 是预期负例，不是候选异常失败。
- 当前进入完整产物核对，再进行新候选与原始基线的 warm 采样。UE 5.8 本轮完整 staging 仍约 34/42 秒（A 不含随后手工发布）；不将该单次观测当成稳定性能结论。

### 2026-10-25：产物等价与调试符号差异分析

- 当前 A/B 共 94,905 个 UHT 文本文件完全相同，各槽相对本次开始也未变。B56/B57 的 220 发布文件相对旧 HEAD 逐字节相同，B58 的 225 文件仅 UnrealSharp.Log.dll/PDB 不同；三版 Publish/Stage/Archive 均同哈希。
- 用 PEReader/MetadataReader 检查 B58 Log：API/IL 记录相同，9 个完整方法体与字段初始数据相同，CodeView 路径分别来自 RID/非 RID obj 目录。Portable PDB 解析核对 4 个文档仅 RID 路径不同，源内容哈希、序列点、作用域/局部变量相同，import blob 解码语义相同；编译引用元数据完整保留。
- A56 相对旧手工对照只有 UnrealSharp.pdb 变化，DLL 未变；旧/新 PDB 哈希分别精确匹配 win-x64 与非 RID 构建目录。522 个文档仅路径不同，82 个 import blob 解码相同，方法序列点未变。这是既有 publish 产物来源差异，不作为删除 resolver 的运行时行为变化。
- 诊断工具第一次误用不存在的 Debug 可执行路径，改用实际 Release 产物后成功；为避免对两个 52 MB JSON 做平方级文本匹配，停止了已核实的本任务 Python 符号对比进程，改为保留完整 old/new JSON 和每一处已变记录。没有停止 native build 或用户进程。
- 5.6/5.7 新三轮 warm 采样完成；5.8 正在执行。没有在采样阶段并行运行编译、哈希扫描或另一个测试流程。

- 后续解码补充：custom debug GUID b5feec05-8cd0-4a83-96da-466284bb4bd8 的内容实际为编译选项，其中 platform 为旧 X64 / 新 AnyCpu；前面日志中“编译引用元数据”的分类不准确，完整原始值保留。报告按平台/调试产物差异描述，不仅称路径不同。托管 API/IL 与方法体等价，当前 Win64 运行已实测通过；没有改动既有 PackageProject 发布逻辑来隐藏此差异。


### 2026-10-25：amend 最终交付

- 两提交最终 HEAD fddb33aefd598fa0d1eb5e324dc5ae36fb788a47；base 保持 cb8577e3a47b5bdc5454239a17f61a6282a50ae2。只移除 89707cde，旧 HEAD 由本地 backup 引用保留。
- 三版原始基线/候选的新功能、54 项边界、增量/失败保护/skipstage/恢复均完成；94,905 个既有真实 UHT 文本一致，生成器输入未改变，没有强制重新导出。
- 5.6/5.7 候选各 220 文件与改写前逐字节一致；5.8 Log DLL/PDB 的 RID 调试差异已做 API/IL、方法体与 Portable PDB 解析，完整数据保留。最终复制链一致，公共 runtime DLL 的新 A/B API/IL 检查一致。
- 三版 warm 采样中位差：5.6：+0.182 秒（+1.84%）；5.7：+0.293 秒（+2.98%）；5.8：+0.364 秒（+0.86%）。全部样本与预热保留，未删异常较慢值。
- 最终源码快照 A 各 826/B 各 829 文件匹配，构建入口与 uproject 未变，publisher 调用次数 47 项核对通过。
- validation.md、pr.md 和当前计划已更新，旧版本保留在本轮 evidence 目录。未推送、未发布/更新远端 PR、未合并其他分支。

### 2026-10-25：最终快照与证据范围核对

- warm 采样后的 B58 Log DLL/PDB 已回到 RID 产物，最终三个候选共 665 文件全部与 pre-amend 对应输出逐字节相同。过渡态副本与原始哈希已单独保存，未覆盖或隐藏先前观察。
- PE metadata 支持检查补全：RID Log 为 AMD64，非 RID 为 I386/AnyCPU；除 GUID heap 外只有 MethodDef 表变化，逐行确认仅 RVA 地址字段不同，其余列相同；完整方法体已有独立哈希一致证据。最终 Log 则与 pre-amend 全文件相同。
- A58 在 warm 发布也出现 UnrealSharp.dll/PDB 的 RID/非 RID 切换，体现既有发布依赖构建缓存行为。没有改写 PackageProject 处理这一独立现象。
- 最终公共框架 DLL API/IL 均一致；5.8 RuntimeGlue 除项目程序集属性外，A 还已有一个 SharpTestMap AssetIds 常量，B 无。完整 AssetIds.cs diff 已保存；本轮两者运行 Engine Entry/probe，不使用此常量，其 DLL 从首轮至最终未变。报告不再笼统声称三个版本 RuntimeGlue 仅有项目名差异。
- 最终 range-diff 确认仅删除中间提交，工作树干净；任务构建/Cook/游戏进程均已退出。全部修改仍为本地，远端未更新。

## 2026-10-27 test 分支交接

用户授权从当前功能分支创建 test/csharp-package-test，携带 tools、验证、计划、日志和 PR 草稿并 push，随后明确不要原始证据，只要总结与步骤。已按新范围整理 Review/CSharpPackage；临时证据副本的删除被自动策略拦截，随后安全移回本地报告目录，未纳入提交。原证据未改动。

新增 README 与 REPRODUCE，标明历史脚本路径迁移、六槽项目准备、canonical AutoBuild、三版 UAT 入口、probe、回归与采样步骤，以及未测范围。工具保留原执行源码；Review/.ubtignore 防止验证 Automation 项目被正常构建自动发现。28 个 Python 文件 AST 检查和 10 个 PowerShell 文件语法检查通过；没有原始证据类文件，常见 credential 模式扫描无匹配。本次只整理交接材料，不重复运行已完成的引擎矩阵，也未修改插件实现。后续 Git 提交和 origin test 分支标识以实际仓库历史为准。
