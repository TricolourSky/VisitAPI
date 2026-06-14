# .dlg 对话剧本格式参考

[English](DLG_FORMAT.en.md)

一个商人 = 一个 `BepInEx/config/VisitAPI/<traderId>.dlg` 文件（UTF-8 编码）。
`.dlg` 优先于同名 `.json`；解析失败自动回退 `.json`，错误带行号写入 BepInEx 日志。
完整示例见 [sora.dlg](sora.dlg)。

## 文件头（第一个节点之前）

```
trader: <24位商人ID> "显示名"
start: <默认起始节点>
first: <首次拜访节点>                          ← 可选
trigger: raid <地图> (x, y, z) door 宽x高[x旋转] dist 3 radius 1.2 "交互提示"
trigger: hideout <区域枚举名> level 2 dist 4 (x, y, z) node <节点> if 任务=状态 "交互提示"
when: level>=4 -> <节点>                       ← 按商人等级/声望选起始节点，可多行
when: level>=3, standing>=0.05 -> <节点>       ← 条件可逗号并列
random: 10% <节点1> <节点2> ...                ← 战局结束后随机触发对话
quest <别名> = <24位任务ID>                     ← 任务别名，可多行，位置随意
tab: if 任务=状态[/状态]                        ← 商人界面"拜访"页签仅在任务处于指定状态时显示
```

- `trigger` 行中只有类型与地图/区域是必填，其余参数任意顺序、可省略
  （默认：dist 3 / radius 1.2 / level 1 / 提示"拜访"；门碰撞高度默认 2.2）。
- `trigger: hideout` 取代旧 `hideout_triggers.json`（旧文件仍兼容，同商人同区域时 .dlg 优先）。
- 区域枚举名：IntelligenceCenter / Generator / MedStation / Workbench …（EAreaType）。
- `trigger: hideout ... if 任务=状态`：带任务条件的触发器由**任务状态门控**——状态匹配期间
  每次进藏身处都会出现（不受"已拜访过"限制），状态不匹配后自动消失；
  不带 `if` 的触发器维持旧行为（仅首次拜访出现一次）。
- `tab:` 不配置时页签始终显示，**只影响配置了该行的商人**。

## 节点

```
<节点名> bg: 背景文件
> 旁白文字（黑屏字幕，可多行）
NPC 说的话（普通行；多行 = 逐段播放）
- 选项文本 -> 目标
- 选项文本 -> 目标 | 指令, 指令...
```

- **节点名**只允许英文/数字/`_`/`.`/`-`——台词里的 `[动作描写]`、`<富文本>` 不会被误判。
- **bg:** 纯文件名默认到 `backgrounds/` 下找；带路径则原样使用。省略 = 沿用上一个背景。
- **目标**：节点名 / `@start`（回起始节点）/ `@trade`（开交易页）/ `@tasks`（开任务页）；
  **省略 `-> 目标` = 关闭对话**。
- `#` 或 `//` 开头的行是注释；空行随意。

## 选项指令（`|` 之后，逗号分隔）

| 指令 | 作用 | 自动显示条件 |
|---|---|---|
| `accept: 任务` | 接取任务 | 仅"可接取"时显示 |
| `handover: 任务` | 打开交付物品界面 | 仅"进行中"时显示 |
| `complete: 任务` | 完成任务发奖励 | 仅"可完成"时显示 |
| `if: 任务=状态[/状态]` | 显式显示条件（覆盖自动条件） | — |
| `ifnot: 任务=状态[/状态]` | 处于该状态时隐藏 | — |
| `once` | 点过一次后不再出现 | — |
| `always` | 取消自动显示条件（隐藏任务流程用） | — |

- 任务可写别名（文件头 `quest xx = ...`）或完整 24 位 ID。
- 状态名：`Locked / AvailableForStart / Started / AvailableForFinish / Success / Fail`，也可写数字 0-5。
- 组合动作：主动作（complete/handover）在前、`accept:` 在后 = 执行的同时接取新任务，例：
  `| complete: usb, accept: mats, always`

## 与 JSON 的关系

- `.dlg` 在加载时编译成与 `.json` 完全相同的 DialogTree 模型，运行时无差别；
- `.json` 永久兼容，且同样支持新增的 `"hideoutTriggers": [...]` 内嵌字段；
- 文本若包含 ` -> ` 或 ` | `（前后带空格）会被误认为分隔符——中文对话基本不可能出现，
  真遇到了就改用 `.json` 写那个节点。

Sora is the Best~
