# VisitAPI — Server Mod

VisitAPI-Server 在**端口 6970** 独立监听，为客户端插件提供任务接取、递交、完成和状态查询 API。

对话树格式、任务系统完整说明和示例请参阅根目录 [README.md](../README.md)。

---

## 目录结构

```
user/mods/VisitAPI-Server/
├── package.json
├── VisitAPI-Server.dll
└── db/
    ├── locales/           ← 任务本地化文本（en.json、zh-cn.json …）
    └── quests/            ← 任务定义文件（SPT 4.0.13 原生格式）
        └── *.json
```

---

## HTTP API（端口 6970）

| 端点 | 方法 | 说明 |
|---|---|---|
| `/visitapi/quest/accept` | POST | 接取任务 |
| `/visitapi/quest/handover` | POST | 递交物品，推进至「可完成」 |
| `/visitapi/quest/complete` | POST | 完成任务，发放奖励 |
| `/visitapi/quest/status` | POST | 批量查询任务状态 |

**请求体（accept / handover / complete）：**
```json
{ "ProfileId": "存档ID", "QuestId": "任务ID" }
```

**请求体（status）：**
```json
{ "ProfileId": "存档ID", "QuestIds": ["任务ID1", "任务ID2"] }
```

---

## 注意

- 任务文件在**服务端启动时加载**，修改后须重启 SPT 服务端。
- SPT 4.0.13 内部中文代码为 `ch`，VisitAPI-Server 会自动将 `zh-cn.json` 同时注册为 `ch`，无需创建两份文件。
