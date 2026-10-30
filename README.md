# DuckovDiag —《逃离鸭科夫 / Escape from Duckov》崩溃诊断插件

一个**只做记录、不改玩法**的诊断 mod：把物品实例化、词条（Modifier）重算、槽位（Slot）初始化、
开枪、死亡等事件**逐行写进日志**，让"崩溃前最后一行"直接告诉你触发点。

> 适用场景：游戏出现原生崩溃（Unity 崩溃窗口 / `UnityPlayer` 栈 / `0xC0000005`），
> 官方 `Player.log` 只有调用栈、没有"当时在处理哪个物品、哪个事件"，这个 mod 就是补这块信息。

---

## 功能

| 日志标签 | 内容 |
|---|---|
| `[ITEM]` | 每个 Item 的 `Awake` / `Initialize` / `HandleEffectsActive`，带 typeID、名字、词条数 |
| `[STEP]` | `Item.Initialize` 内部各组件（StatCollection / SlotCollection / ModifierDescriptionCollection）的执行顺序 |
| `[MODS]` | 词条列表逐条输出：key、是否跨物品共享、`collection` 是否指向别的物品 |
| `[SLOTS]` | SlotCollection 内容（key、content 物品），用于排查槽位数据损坏 |
| `[COMBAT]` | 开枪（`ItemAgent_Gun.TransToFire`）与死亡（`CharacterMainControl.OnDead`）事件 |
| `PATCH` | 启动时导出**当前所有 Harmony 补丁表**（哪个方法被谁补丁、prefix/postfix 数量）以及**加载了几套 Harmony** |

日志文件：`<游戏安装目录>/DuckovDiag.log`（每行立即 flush，崩溃也不丢）。

插件还支持两个开关文件（放在游戏根目录）：

- `DuckovDiag.skipModifiers` — 存在时跳过 `ModifierDescriptionCollection.ReapplyModifiers` 原逻辑（用于验证"崩溃是否在词条重算"）
- `DuckovDiag.verbose` — 存在时输出全部细节（日志会非常大）

---

## 安装

**方式一（推荐）**：Steam 创意工坊订阅，然后在游戏内 `Mods` 菜单启用。

**方式二（手动）**：把 `DuckovDiag/` 整个文件夹放进

```
<Escape from Duckov>/Duckov_Data/Mods/DuckovDiag/
```

然后启动游戏，在 `Mods` 菜单启用（新 mod 默认是关闭的）。

文件夹、`info.ini` 的 `name`、`DuckovDiag.dll`、类名 `DuckovDiag.ModBehaviour` 必须同名，这是游戏 mod 加载器的要求。

---

## 使用

1. 启用插件，进游戏复现崩溃；
2. 崩溃后打开 `<游戏目录>/DuckovDiag.log`，**看最后几行**：

```
[019149] [COMBAT] TransToFire IG_Gun_AK47_Fire(Clone)
[019150] [COMBAT] OnDead Character(Clone)      <-- 崩溃就发生在这之后
```

3. 把最后 30 行 + `Player.log` 的崩溃栈一起贴到反馈帖即可。

> 日常游玩建议关闭：日志量可能很大，且会对热路径打点。

---

## 从源码构建

需要 .NET SDK 6/8/9/10（`netstandard2.1` 目标）。

```bash
export DUCKOV_GAME_DIR="$HOME/.local/share/Steam/steamapps/common/Escape from Duckov"
export DUCKOV_HARMONY_DLL="$HOME/.local/share/Steam/steamapps/workshop/content/3167020/<任意一个带 0Harmony.dll 的 mod>/0Harmony.dll"

dotnet build -c Release
# 产物: bin/Release/netstandard2.1/DuckovDiag.dll
```

也可以直接：

```bash
dotnet build -c Release -p:GameDir="/path/to/Escape from Duckov" -p:HarmonyDll="/path/to/0Harmony.dll"
```

> 依赖：游戏自带的 `ItemStatsSystem.dll`、`TeamSoda.Duckov.Core.dll`、`UnityEngine*.dll`，
> 以及工坊生态里通用的 `0Harmony.dll`（2.4.x）。这些都不随仓库分发。

---

## 日志格式说明

```
[000123] [ITEM] Initialize.BEGIN #42 '老钢盔' mods=2 items=1234
[000124] [STEP] component.Initialize:ModifierDescriptionCollection <- #42 '老钢盔'
[000125] [MODS] ReapplyModifiers BEGIN #42 '老钢盔' count=2
[000126] [MODS]   [0] key=Armor ownerCollectionIsThis=True enableInInventory=True
[000127] [MODS] ReapplyModifiers END #42 '老钢盔'
```

启动时还会打印：

```
---- Harmony 副本数量: 1 ----
  Harmony v2.4.1.0  <- ...3167020\3589088839\0Harmony.dll
---- 启用中的 mod(58): ...
  PATCH ItemStatsSystem.Item::get_TotalWeight  owners=[NotEquipmentWeight] ...
```

多套 Harmony、同名方法被多个 mod 重复补丁，是这类原生崩溃的高危信号。

---

## 已知限制

- 只记录**托管层**信息，无法定位纯原生（C++ 插件）崩溃；
- 不修改、不拦截游戏逻辑（`skipModifiers` 开关除外）；
- 大量 mod 时日志增长很快，请按需启用。

---

## 免责声明

本工具仅用于排查问题，**不修改存档、不修改游戏逻辑**。使用风险自负。

## License

MIT
