# TraeSwitch Pilot 执行说明

本文件用于 Phase 0（载体定位）与真实账号 Pilot。核心限制：定位需要"完全退出客户端再做快照"，
而对话正运行在该客户端上，因此定位应在**你本来就准备切号/结束本次对话**的自然时机执行。

## Phase 0：一次性载体定位（约 2 分钟）

前置：构建探针

```powershell
dotnet build TraeSwitch.Probe/TraeSwitch.Probe.csproj -c Release
```

按下面顺序操作（必须在**客户端完全退出**状态下做快照，托盘也退出）：

1. 退出当前账号的 TraeWork CN（完全退出，任务管理器无 `TRAE SOLO CN` 进程）；
2. `TraeSwitch.Probe snap 1`；
3. 打开客户端，正常登录/切换到另一个账号（走一次手机验证码，这正是要消灭的操作）；
4. 再次完全退出客户端；
5. `TraeSwitch.Probe snap 2`；
6. `TraeSwitch.Probe diff 1`，得到切号前后变化的相对路径清单。

判读：
- 把"账号相关、稳定复现变化"的小文件相对路径填到
  `%APPDATA%\TraeSwitch\settings.json` 的 `Fingerprint`（数组）；
- 大缓存/每账号级联数据（例如会话数据库、工作区）若随账号变化但体积巨大，
  不应纳入指纹——本工具只切换登录态小文件；
- 若 diff 显示成百上千文件且含大库，说明账号会话与本地数据深度绑定，
  需回到 spec 第 4 节降级预案评估。

定位结论请回填到 `docs/superpowers/specs/2026-09-05-trae-switch-design.md` 第 4 节，
并关闭对应开放项。

## Pilot：2 账号真机试运行（≥ 1 周）

1. 用上述指纹为账号 A、B 各执行一次 UI「建档（备份当前账号）」；
2. 日常切换走 UI「切换到选中账号」，观察：
   - 是否每次都免验证码直接进入目标账号；
   - 是否触发服务端风控（被要求重新验证/设备受限）；
   - vault 校验（「校验选中账号」）是否偶发失败（说明客户端重写载体 → 需重新建档）。
3. 稳定 1 周后再考虑给 3-4 账号的其他用户使用；
4. 任何异常按 `MainForm` 日志区提示操作；vault 目录在
   `%LOCALAPPDATA%\TraeSwitch\vault`，内含真实登录态字节，勿上传/勿入库。
