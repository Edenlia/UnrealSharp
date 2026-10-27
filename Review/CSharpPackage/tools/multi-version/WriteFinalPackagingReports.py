import json
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
root=workspace/'CodeProjects/UnrealSharp-reports/csharp-package'
raw=root/'multi-version-20261025'
perf=json.loads((raw/'performance-summary.json').read_text(encoding='utf-8'))
reg=json.loads((raw/'regressions.json').read_text(encoding='utf-8'))
assert all(reg[v]['native_editor_outputs_unchanged'] and reg[v]['failure_preserved_archive'] for v in ('56','57'))
assert json.loads((raw/'58-final-audit.json').read_text())['managed']['equal']
rows=[]
for v in ('56','57'):
    a,b=perf[v]['1'],perf[v]['2']
    assert a['n']==b['n']==3
    rows.append(f"| 5.{v[1]} | {', '.join(f'{x:.3f}' for x in a['seconds'])} | {', '.join(f'{x:.3f}' for x in b['seconds'])} | {a['median']:.3f} | {b['median']:.3f} | {perf[v]['delta_seconds']:+.3f} / {perf[v]['delta_percent']:+.1f}% |")
table='\n'.join(rows)

validation=f'''# C# staging 多版本验证

## 交付与范围

分支 `featre/csharp-package`，仓库 `C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp`。
原始基线 `cb8577e3a47b5bdc5454239a17f61a6282a50ae2`；最终 HEAD `777b14237afbcfea740458d1643f433ef7769751`。
已完成请求的 UE 5.6/5.7 Win64 Development Game 功能和性能验证，并补做最终共享代码的 UE 5.8 回归。只创建本地提交和 PR 草稿；没有推送、发布 PR、合并 main 或同步 cp-main。

| 提交 | 完整责任 | 增量证据 |
| --- | --- | --- |
| `1723b7ba619b8e4ee778dde672cf9983e63cd6a9` | 通过 CustomStagingHandler 发布并登记 NonUFS；复用 PackageProject | 原 5.8 完整 A/B；最终 5.7/5.8 标准入口回归 |
| `89707cded9b453751d11525170354b0c03133025` | 按已有 UBT 插件覆盖规则解析 UnrealSharp，避免 Engine 副本覆盖项目插件 | 5.6 真实 resolver：原始基线选 Engine/Marketplace，候选选项目插件；完整发布使用项目插件 |
| `777b14237afbcfea740458d1643f433ef7769751` | 5.6 独立入口、共享 staging、按名解析 UHT 所需命令 | 5.6/5.7 实际构建、发布、运行、增量、失败恢复；5.8 回归 |

最终差异仅 7 个插件 C#/csproj 文件，162 行新增、38 行删除。测试、日志、计划、项目初始化与构建脚本都未进入 UnrealSharp 提交。

## 入口与设计结论

| 版本 | 打包入口 | 行为 |
| --- | --- | --- |
| 5.6 | `RunUAT -ScriptsForProject=<project.uproject> BuildCookRunUnrealSharp <既有打包参数>` | 插件命令继承 BuildCookRun，通过 ModifyDeploymentContextCallback 在平台封装/签名前登记 managed 文件 |
| 5.7 / 5.8 | `RunUAT -ScriptsForProject=<project.uproject> BuildCookRun <既有打包参数>` | 自动发现插件 CustomStagingHandler |

5.6 的普通 BuildCookRun 和默认 Editor 打包按钮不会因此自动接入，必须使用上述独立入口。5.7/5.8 验证的是与 Editor 打包相同项目脚本上下文的 UAT 命令，未另外点击 Editor UI。源码引擎的项目插件发现可能仍需 `-ScriptDir=<plugin>/Build/Scripts`；本轮不扩大源码引擎矩阵。

两个入口共用 receipt 选择、单目标/架构验证、插件身份核对和发布目录：`Intermediate/UnrealSharp/Publish/<Target>/<Platform>/<Configuration>/<Architecture>`。完整发布输出登记为 `Binaries/Managed/net10.0` 下的 NonUFS 文件，随后由 Unreal 处理平台目录布局和封装。每次候选 staging 只调用一次发布。

PreBuild 位于 UHT 之前；PostBuild 由 native receipt/action 的状态决定。二者不能保证在仅 C# 变化或跳过 native build 的 staging 中执行，因此不能作为主入口。5.6 的 CustomDeploymentHandler 又依赖平台实现和配置选择，不能提供同等通用的插件自动发现。

没有新增项目 Target、uproject 配置、打包设置或项目 Automation 桥接。仍会产生正常 Intermediate/Binaries/Saved 输出，以及既有 UnrealSharp 初始化/发布流程中的 Script/global.json、RuntimeGlue 和 launch settings；不把“插件接入”描述成“项目目录零写入”。

## 环境、来源与执行身份

Windows 11 Pro 10.0.26200；AMD Ryzen 9 9900X（12 核 / 24 线程），约 64 GiB 内存；managed SDK 10.0.400，发布的 self-contained runtime 为 10.0.11。

| 版本 | Engine | A / B 项目 | A / B 实现 |
| --- | --- | --- | --- |
| 5.6 | Installed UE 5.6.1，CL 44394996；UAT .NET 8.0.300 | SharpTestProject56_1 / SharpTestProject56_2 | 原始基线失败/生成证据保留；有效手工对照为 89707cde，B 为最终 HEAD |
| 5.7 | Installed UE 5.7.4，CL 51494982；UAT .NET 8.0.412 | SharpTestProject57_1 / SharpTestProject57_2 | 原始 cb8577e3 / 最终 HEAD |
| 5.8 | Installed UE 5.8.3，CL 58210709 | SharpTestProject / SharpTestProject1 | 保留原基线与 1723b7ba 历史 A/B；B 最终更新至 HEAD 做回归 |

各版本在本版本内比较，没有将不同引擎版本的耗时当作插件 A/B。四个新测试项目的 Game AutoBuild 由用户明确授权创建，仅将各自既有 Editor 入口的 Target 改为对应 Game；其他构建参数、引擎和配置原样保留。

5.6 的安装包含同名 Engine/Marketplace 插件。原始 resolver 选错该目录，不能安全完成原始基线的 managed 发布。本轮保留真实 resolver 负例和原始基线的原生构建/UHT 产物；完整发布及性能使用明确标注的 89707cde 手工对照。该版本在 5.6 不编译自动 handler，执行原手工发布流程加必要 resolver 修复。它不是原始 cb8577e3 全链通过的替代声明，也没有把失败耗时算作性能。

完整源快照来自已验证 Edenlia/UnrealSharp fork。`final-source-verification.json` 核对 A56 827、A57 826、三个 B 各 829 个源文件；除已知 C# 文本 Git CRLF/LF 转换外，内容匹配固定快照。没有取用 cp-main 或旧测试引擎中的插件实现。

所有 native 编译使用相应 AutoBuild Editor/Game，成功后才运行 Editor/Cook/游戏。运行日志记录项目插件挂载、包内 hostfxr、UnrealSharp.dll、用户程序集实际加载路径；probe JSON 记录 Game/Development、内容值、runtime 路径和实际进程路径。B 边界检查另记录实际加载的 UnrealSharp.Automation.dll 路径。

## 功能与失败记录

| 检查 | 5.6 | 5.7 | 5.8 最终回归 |
| --- | --- | --- | --- |
| canonical Editor / Game 构建 | 原始 A、手工对照 A、B 通过 | 原始 A、B 通过 | B 通过 |
| 标准 A staging / 手工发布 | 对照 A：Stage 0 个 managed，手工发布后 220 个并运行成功 | 原始 A：Stage 0 个 managed，手工发布后 220 个并运行成功 | 原历史证据保留 |
| 候选 Cook/Stage/Archive/运行 | 独立入口通过，220 文件 | 标准入口通过，220 文件 | 标准入口通过，225 文件 |
| 仅 C# / 内容修改 | v1→v2 正确，无 native rebuild | v1→v2 正确，无 native rebuild | 原 5.8 同类证据保留 |
| 注入编译错误 | 非零退出，旧归档哈希不变 | 非零退出，旧归档哈希不变 | 原 5.8 同类证据保留 |
| `-skipstage -skippak` | 源恢复 v1 后仍运行已暂存 v2 | 同左 | 原 5.8 同类证据保留 |
| 正常 Stage 恢复 | 重新运行 v1 | 重新运行 v1 | 最终 probe v4 正确 |
| native / Editor 输出隔离 | 四组 before/after 哈希不变 | 四组 before/after 哈希不变 | 原隔离证据与最终全链回归 |
| synthetic receipt/命令边界 | 17 项通过 | 17 项通过 | 17 项通过 |

四组输出为项目 native、项目 managed、插件 native、插件 managed。增量/失败检查没有改动或删除这些构建产物；测试源码已恢复。`regressions.json` 保存完整 before/after manifest。

边界包括 Game/Client/Server 激活，空上下文、Editor、未使用插件时不发布；混合目标、混合配置、Android/iOS、Universal Mac、错误项目、DLC 被拒绝；不存在的命令仍报告错误。另有 5.6 foreign/foreigncode 两项真实 UAT 拒绝检查。这些 synthetic receipt 检查不能替代相应平台/target 的真实打包。

保留的失败及处理：

1. 原始 56_1 首次 Cook 在 RuntimeGlue 尚未初始化时嵌套启动 UAT，触发单实例锁。改为在 Cook 前独立执行官方 GenerateProject 和 BuildUserSolution；不属于 feature 的项目桥接。
2. 56_2 初始未提交候选的 UHT 命令枚举加载 BuildCookRun 父类，缺少 AutomationScripts.Automation。777b1423 将派发收窄为按完整类型名查找；原样 AutoBuild 重试成功，没有复制 UAT 依赖进 UHT。
3. 上述中断留下已生成文本但未完成的 managed bindings，导致初始化 RuntimeGlue 缺少程序集。使用与 UHT BuildUtilities 相同参数的官方 BuildSolution 补齐纯托管输出；未清 native 缓存或改编译方法。
4. 5.8 复用旧 cook 数据的 Stage 缺少 IoStore package/project store；重新正常 CookStage 后通过，保留失败日志。
5. resolver 测试工具初次遗漏 net8.0 TargetFramework，导致 UAT 工程加载失败；修正工具后 A/B v2 通过。没有将测试工具错误算成产品缺陷。

## 真实生成输出与发布产物

| 比较 | Editor 文本 | Game 文本 | 内容差异 |
| --- | --- | --- | --- |
| 5.6 原始 A UHT / 最终 B | 16,778 | 11,652 | 0 |
| 5.7 原始 A / 最终 B | 17,369 | 11,885 | 0 |
| 5.8 已验证旧 B / 最终 B | 21,990 | 15,231 | 0 |

5.6/5.7 新项目没有额外 native UObjects，所以 project UHT 范围为 0；5.8 包含每 target 12 个 project 文本文件。前两版合计 57,684 个实际插件 UHT 文本文件一致；5.8 的 37,221 个文件与原有已验证产物相同。没有手改生成 glue。

5.6/5.7 各 220 个候选 publish 文件、5.8 的 225 个候选文件在 Publish→Stage→Archive 的路径、大小、SHA256 均相同，全部登记为 NonUFS。文件数不同来自版本/夹具差异，没有跨版本宣称二进制等价。

每个旧版 A/B 发布比较中，201 个文件逐字节一致；差异清单包含 7 组公共命名 DLL/PDB、UserCode.LoadOrder，以及各项目命名的 4 个 RuntimeGlue 文件。保留完整 manifests、原始文本 diff、PE metadata/IL records、MVID 和 debug path：

- UnrealSharp、Core、Binds、Log、Plugins、StaticVars 六个 DLL 的受检 API/IL 记录一致；二进制哈希与 MVID/debug 信息并不相同，不将其声称为逐字节一致。
- probe 受检记录仅 file-local module registrar 的编译器生成名称不同；实际 target、代码版本与内容通过包内执行确认。
- RuntimeGlue 生成源码完全相同，受检程序集属性记录的差异为项目名称；LoadOrder 和项目命名文件也保留对应名称。完整原始差异保留，没有过滤名称或 GUID 来强行得出相等结论。

源变化没有修改 native 布局、导出签名、可见性、默认值解析或 marshalling 生成算法；真实 UHT 文本相同与运行探针支持这一范围内的回归结论，不代表覆盖所有 UnrealSharp 导出类型组合。

## 性能

固定 Win64 x64 Development Game，使用已经完成 native build/cook 的等价 workload。每版先 A/B 各预热一次，然后 A-B、B-A、A-B 各三次独立 UAT 进程。A 统计标准 Stage+Archive 与独立 PackageProject 的进程 wall time 之和；B 统计自动发布 Stage+Archive 的进程 wall time。native build、cook、测试工具准备和额外 packaged probe 启动均不计入。所有正式样本和预热均运行 probe 验证有效输出，没有失败/少做工作的样本参与统计。

单位秒，负差值表示候选更快；n=3/side：

| UE | A 原始样本 | B 原始样本 | A 中位数 | B 中位数 | B−A / 相对变化 |
| --- | --- | --- | --- | --- | --- |
{table}

范围：5.6 A {perf['56']['1']['min']:.3f}–{perf['56']['1']['max']:.3f}，B {perf['56']['2']['min']:.3f}–{perf['56']['2']['max']:.3f}；5.7 A {perf['57']['1']['min']:.3f}–{perf['57']['1']['max']:.3f}，B {perf['57']['2']['min']:.3f}–{perf['57']['2']['max']:.3f}。这是本机、此夹具、warm staging 的观测；样本少且没有隔离整机后台负载，不外推到完整项目、冷构建或其他平台。

两版样本范围均有重叠：5.6 候选中位数略低，5.7 略高，不能据此声称稳定提速。没有通过继续挑选样本消除较慢结果。

5.6 的性能 A 是 89707cde 的有效手工对照，绝非不能正确发布的原始基线。原始失败没有性能百分比。5.7 A 是原始 PR 基线。

原 5.8 在 1723b7ba 上的三轮历史结果为 A 中位 15.999 秒、B 47.806 秒；独立诊断区分出约 9.3 秒发布和约 26.5 秒 Zen readiness 等待，属于当时环境中的实际回归。此次最终代码在 5.8 做功能/生成产物回归，未重新做三轮性能 A/B；历史数字不冒充最终 HEAD 的 benchmark，也没有修改引擎 Zen 行为或宣称修复该等待。

## 限制与最终状态

验证的平台是 Windows Win64 x64、Development Game。Mac 的平台布局/签名顺序来自 Engine 源码接入点分析，本机没有执行 Mac 打包或签名验证；Client/Server/Shipping 未完整实跑。Android/iOS、Universal Mac、DLC 和 NativeAOT 没有新增支持。不能将这些范围标为已验证。

原始 5.6 在存在 Engine 同名插件的环境中无法安全提供完整发布 A/B，已保留失败并提供明确区分的有效手工对照；这个比较限制不会被隐藏为“所有原始基线全通过”。旧 SharpTestProject56/57 没有作为本阶段的测试槽，也没有为旧 DefaultGuardProbe 的独立默认值解析问题引入无关修复。

最终槽位：A56=89707cde，A57=cb8577e3，B56/B57/B58=777b1423。B56/B57 的测试源码和最后打包 probe 为 v1；B58 保留原夹具 v4。原 5.6 基线插件/生成结果保存在 `SharpTestProject56_1/Saved/CSharpPackageMulti-20261025/original-base-plugin`；四个新项目的初始内容在对应 `Saved/CSharpPackageMulti-20261025/before`；B56/B57 的 step2-plugin 和 B58 的 previous-plugin 同样位于 Saved，不参与插件发现。没有自动恢复旧槽位或清理这些证据。

## 复现与证据索引

所有新原始记录位于 [multi-version-20261025](multi-version-20261025/)。每个 UAT/native case 的 `*.command.json` / `*.result.json` 保存可复现命令、cwd、开始时间、退出码和 wall time；`*.log` / `*-UBT.log` 保留实际输出。

- [源快照核对](multi-version-20261025/final-source-verification.json)、[主机](multi-version-20261025/host.json)、[最终 zip](multi-version-20261025/final-candidate.zip)。
- [5.6 产物](multi-version-20261025/artifact-audit-56.json)、[5.7 产物](multi-version-20261025/artifact-audit-57.json)、[5.8 回归产物](multi-version-20261025/58-final-audit.json)。各 generated manifest/diff、published-delta/text.diff 和 assembly-audit 子目录保留完整实例。
- [5.6 边界](multi-version-20261025/56_2-Boundary-cases.json)、[5.7 边界](multi-version-20261025/57_2-Boundary-cases.json)、[5.8 边界](multi-version-20261025/58_2-boundary-cases.json)、[旧入口参数](multi-version-20261025/legacy-option-checks.json)。
- [增量/失败/恢复](multi-version-20261025/regressions.json)、[性能原始值](multi-version-20261025/performance-raw.json)、[性能汇总](multi-version-20261025/performance-summary.json)。
- [最终 26 个 staging case、项目输入与复制链核对](multi-version-20261025/final-evidence-checks.json)。
- `B-final-multiversion-*` 位于本报告根目录，记录最终 5.8 编译、失败 Stage、成功 CookStage 和运行。旧 5.8 性能与 [历史兼容调查](compatibility-ue56-ue57.md) 保留，不能替代当前版本记录。
- 测试脚本位于 `UEProjects/SharpTestProject56_1/Tools`；主入口 InvokeMultiVersion.ps1；RegressionMultiVersion.py、MeasureMultiVersion.py、AuditMultiVersion.py 记录操作顺序。它们不属于上游补丁。

[当前计划](plan.md) · [完整工作日志](work-log.md) · [英文 PR 草稿](pr.md)
'''
(root/'validation.md').write_text(validation,encoding='utf-8')

pr=f'''# Publish managed assemblies during Unreal staging, including UE 5.6

An UnrealSharp package currently needs a separate managed publish after Unreal packaging. This change publishes assemblies and the self-contained runtime while UAT constructs the staging manifest, so platform packaging and signing can include those files through the normal pipeline. Publish failures stop staging before archiving.

The plugin reuses PackageProject, selects the target/configuration/architecture from the native receipt, publishes into an isolated Intermediate directory, and registers the complete output as NonUFS. It does not require project Target, descriptor, packaging configuration, or Automation bridge changes. Existing UnrealSharp initialization/build outputs and the manual PackageProject command remain part of the normal workflow.

## Engine entry points

- UE 5.7/5.8: the plugin's CustomStagingHandler participates in standard BuildCookRun with the project script context (`-ScriptsForProject=<project.uproject>`).
- UE 5.6: `BuildCookRunUnrealSharp` derives from BuildCookRun and attaches the same staging implementation through ModifyDeploymentContextCallback. This is an explicit alternative entry point; ordinary UE 5.6 Editor packaging/BuildCookRun does not automatically opt in. The command rejects foreign sample creation and is excluded on engines providing CustomStagingHandler.
- Source-engine project-plugin discovery can still require `-ScriptDir=<plugin>/Build/Scripts`; this change does not generate a project bridge.

PreBuild/PostBuild are insufficient because native actions may be skipped when only managed code changes. The staging entry points cover that case directly and discover published dependencies before copying the manifest.

Two supporting details are necessary: resolve UnrealSharp using the existing UBT plugin-precedence helper, so a project plugin overrides an engine installation; and resolve only the requested managed command type, so UHT does not eagerly load the new command's UAT-only BuildCookRun dependency.

## Validation

Verified on Installed UE 5.6.1 and 5.7.4, Win64 x64 Development Game, using separate A/B projects per engine. Native Editor/Game builds used each project's canonical build scripts. The final shared implementation also passed UE 5.8.3 build, Cook/Stage/Archive, and packaged-runtime regression checks.

- Baseline/control standard staging omitted managed output. Manual publishing produced working packages; the candidate included the files in staging and ran without a post-packaging publish.
- The 5.6/5.7 packages contained 220 managed/runtime files each, with identical publish/stage/archive hashes. The 5.8 fixture contained 225. Packaged probes confirmed Game/Development code, published content, and the packaged runtime path.
- Managed-only updates were picked up without native rebuilding. An injected C# error failed staging and preserved the previous archive. Skipping staging/pak creation retained the previous output; normal staging recovered correctly.
- Project/plugin native and Editor managed output hashes were unchanged during those checks. Seventeen receipt/command boundary checks passed on each engine.
- 57,684 generated UHT text artifacts matched across the 5.6/5.7 comparisons. The final 5.8 rebuild preserved its previously verified 37,221 artifacts. Inspected API/IL records matched for the common UnrealSharp runtime assemblies; fixture-specific identity differences are recorded separately.

The original 5.6 baseline selects a duplicate engine plugin in the test installation. Its failure evidence is retained; equivalent-work timing uses an explicitly separate manual-publish control containing only the required resolver correction in the active 5.6 path. The 5.7 control is the original PR base.

Three independent warm samples per side compared Stage+manual publish with automatic Stage, excluding native build/cook and runtime verification. Median UAT wall time was {perf['56']['1']['median']:.3f} s vs {perf['56']['2']['median']:.3f} s on 5.6, and {perf['57']['1']['median']:.3f} s vs {perf['57']['2']['median']:.3f} s on 5.7. Raw values, ranges, source mappings, and failure logs are retained in the local validation report. These small fixture measurements are not a general performance guarantee. Historical 5.8 samples showed additional Zen-readiness waiting; final 5.8 performance was not resampled and no engine readiness behavior was changed.

## Limits

The publishing path is restricted to Win64 and Mac JIT. Mac packaging/signing was not executed on this Windows host. Client/Server and Shipping have not received full packaging runs. Mixed targets/configurations, universal architectures and DLC are rejected; mobile and NativeAOT support are not added.

Addresses [#786](https://github.com/UnrealSharp/UnrealSharp/issues/786) and the [documented packaging workflow](https://www.unrealsharp.com/getting-started-and-fundamentals/packaging). Related [#785](https://github.com/UnrealSharp/UnrealSharp/pull/785) remains separate; this change does not depend on its archive target-name detection.
'''
(root/'pr.md').write_text(pr,encoding='utf-8')
print('Wrote current Chinese validation and English PR draft',flush=True)
