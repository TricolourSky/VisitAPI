# VisitAPI 任务系统 — 对话树示例

## 文件路径说明

- **任务定义**：`user/mods/VisitAPI-Server/db/quests/<任意文件名>.json`
- **本地化文本**：`user/mods/VisitAPI-Server/db/locales/<语言代码>.json`（如 `zh-cn.json`、`en.json`）
- **对话树**：`BepInEx/config/VisitAPI/<traderId>.json`

---

## 对话选项新字段速查

| 字段 | 类型 | 说明 |
|------|------|------|
| `questId` | string | 任务 ID，供任务动作使用 |
| `action` | string | `acceptQuest` / `handoverItems` / `completeQuest` |
| `showWhenStatus` | string \| string[] | 仅当任务处于这些状态时显示 |
| `hideWhenStatus` | string \| string[] | 当任务处于这些状态时隐藏 |

**状态名称**（大小写不敏感）：
- `Locked` (0) — 锁定
- `AvailableForStart` (1) — 可接取
- `Started` (2) — 进行中
- `AvailableForFinish` (3) — 可完成
- `Success` (4) — 已完成
- `Fail` (5) — 失败

---

## 对话树节点示例（在商人的 .json 对话文件中）

```json
"nodes": {

  "root": {
    "npcText": "有什么需要我帮忙的吗？",
    "options": [
      { "text": "看看你有什么好东西。", "action": "openTrade" },
      { "text": "说说你的任务。", "next": "quest_menu" },
      { "text": "没什么了，再见。", "next": null }
    ]
  },

  "quest_menu": {
    "npcText": "我这里有一个任务，感兴趣吗？",
    "options": [
      {
        "text": "【接取】收集医疗物资",
        "action": "acceptQuest",
        "questId": "76697369746170696166616b",
        "showWhenStatus": "AvailableForStart",
        "next": "quest_accepted"
      },
      {
        "text": "【递交】送来医疗物资",
        "action": "handoverItems",
        "questId": "76697369746170696166616b",
        "showWhenStatus": "Started",
        "next": "quest_handover_done"
      },
      {
        "text": "【完成】领取任务奖励",
        "action": "completeQuest",
        "questId": "76697369746170696166616b",
        "showWhenStatus": "AvailableForFinish",
        "next": "quest_complete_done"
      },
      {
        "text": "任务已完成，谢谢。",
        "showWhenStatus": "Success",
        "questId": "76697369746170696166616b",
        "next": "root"
      },
      { "text": "← 返回", "next": "root" }
    ]
  },

  "quest_accepted": {
    "npcText": "很好，收集好了再来找我。",
    "options": [
      { "text": "明白了。", "next": "root" }
    ]
  },

  "quest_handover_done": {
    "npcText": "不错，物资已收到，感谢你的帮助。",
    "options": [
      { "text": "随时效劳。", "next": "root" }
    ]
  },

  "quest_complete_done": {
    "npcText": "完成得很漂亮，这是你应得的报酬。",
    "options": [
      { "text": "多谢。", "next": "root" }
    ]
  }
}
```

---

## 任务 JSON 格式（SPT 原生格式）

见同目录 `visitapi_example_handover.json`。关键字段：

- `_id`：任务 ID（须与对话树中 `questId` 一致）
- `traderId`：关联商人 ID
- `conditions.AvailableForFinish`：任务完成条件（如 HandoverItem）
- `rewards.Success`：完成奖励（Experience / TraderStanding / Item）

**HandoverItem 条件**中 `target` 为物品模板 ID 数组，`value` 为数量。

---

## HTTP 接口（端口 6970）

| 路径 | 方法 | 说明 |
|------|------|------|
| `/visitapi/quest/accept` | POST | 接取任务，写入 PMC 档案 |
| `/visitapi/quest/handover` | POST | 扣除背包物品，置为可完成 |
| `/visitapi/quest/complete` | POST | 完成任务，发放经验/好感度奖励 |
| `/visitapi/quest/status` | POST | 批量查询任务状态 |

请求体均为 `{ "ProfileId": "...", "QuestId": "..." }`，状态查询为 `{ "ProfileId": "...", "QuestIds": ["..."] }`。
