# C# packaging test branch

此分支用于跨电脑审查和继续测试。当前生产实现为 `b59ff754`，包含三个提交：`1723b7ba`、`fddb33ae`、`b59ff754`。本次合入已验证的 Editor 入口调整，并同步工具及文档。

- [PR 草稿](pr.md)
- [计划](plan.md) / [工作日志](work-log.md)
- [Client/Server 简要总结与工具](ClientServer/README.md)
- [三版本 Editor 验证总结](validation.md)

UE 5.7/5.8 使用官方 Platforms > Package Project；UE 5.6 使用 UnrealSharp > Package Project。Windows Game 和 UE 5.8 source-engine Client/Server 已验证；Mac 打包及签名尚待在 Mac 上检查。

Review 目录仅为审查材料，不要部署到插件运行目录。工具保留原执行路径约定，需要按本机环境调整。原始日志、JSON 结果、哈希清单、二进制和缓存不随分支发布。文档中的“未推送”描述原验证阶段，本次按用户要求只推送 test 分支。
