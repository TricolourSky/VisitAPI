# VisitAPI Rework — DEV NOTES（踩坑与关键知识档案）

> 按团队约定：源码少写注释，坑集中在这里；Memory.MD 只留一句话摘要。
> 本档案属 SPT 4.1.1 重写线；旧版(4.0.13)档案在 `E:\项目\VisitAPI Framework\docs\DEV_NOTES.md`。

## 引擎/架构

1. **一节点两模板**：`TraderDialogWindow.Redraw` 只在"对话第一行是玩家侧"时生成选项行。每个 .dlg 节点拆成 [NPC说话模板(单行+SwitchDialog 自动切换)] + [纯选项模板]。单模板混排 → NPC 行重复执行 IsBlocked 卡死且选项永不渲染。
2. **@start 语义（两次作案的坑家族）**：必须指向 `when:` 规则解析出的**正常大厅**——①不是字面 `start:` 节点（1.2 首案）②也不是本次被 `first:`/`node` 强制的实际入口节点（2.5 二案：首访节点里点 @start 无限循环回首访，且首访无退出+ESC 封锁=玩家被关小黑屋）。实现上 Register 把 entryNode（本次入口）与 startNode（@start 目标）分成两个参数。**剧本规范：first:/node 强制节点也务必留退出或 @start 选项**。
3. **对话重建 vs 网络确认的竞速**：`SetDialogProgress` 先 `ExecuteDialogAction`（任务动作只是"发起"异步网络事务）后**立刻同步** `method_0` 重建目标对话 → 门控可能读到旧任务状态。解法 = QuestRefresh 保险丝：逐帧快照剧本涉及任务的状态，变化且当前是空闲玩家侧对话时 `dc.SetCurrentDialog(dc.method_0(当前对话id))` 原生重建（两个方法都公开，零反射）。
4. **⚠️ OnActionFinished 广播对我们的订阅不可靠（两案定谳）**：1.9 首版自制标记动作不触发；@trade 原生 DialogTradingScreenAction 同样不触发（SORA 剧本实测, 且原生对话屏的 ActionFinishedHandler 对 TradingScreen/QuestsScreen 动作**没有关屏分支**——点了对话不关、无事发生）。**行级自定义行为的唯一正解 = 行 id 查表 + 订阅 `BaseTraderDialog.OnExecuteLine`**（点击执行总闸门：line.Execute → ChangeDialogHandler → 事件，主线程、先于一切网络动作；每换对话在 OnDialogChanged 里补订阅）——standing/setstatus/handover/once/@trade 五个系统全走这条。@trade/@tasks/@services 翻译成 **DialogQuitAction**（原生退出流关屏可靠）+ 行表记模式, 关屏后 TabRouter 切标签或原生桥开交易屏。
5. **原生条件门**：`DialogLineTemplate.Trigger` = MainConditionGroup(OR) → SubGroup(AND) → DialogCondition，DynamicTraderDialog 每次构建实测。原生 `QuestStatusCondition` 读 `context.QuestsData`（未接任务可能不在）→ 自建 `QuestGate` 捕获 QuestController 读任务书实时状态；任务不存在按 Locked(0)（ifnot 语义正确）。`EQuestStatus` 顺序值 0-9，0-5 与 .dlg 数字约定一致。
6. **零售 dialogue.json**：原生 `TraderDialogsDTO` 可直读；`IsStart` 被 0.16.9.5 静默丢弃 → 原始 JSON 补扫建入口表；嵌套本地化需自定义 JsonConverter；零售台词含任务行，QuestController 为 null 会 NRE。
7. **控制器来源**：藏身处 `player.QuestController` 恒 null → `Singleton<HideoutRepresentation>.Instance._questController` 回退，同时喂对话控制器和屏幕控制器。交易屏路径直接用 `TraderScreensGroup` 公开属性（Profile / QuestController / InventoryController），主菜单 MyPlayer=null 的难题就此绕过。

## UI/美术

8. **按钮定位要布局相对**：固定 anchoredPosition 在非 1080p 分辨率错位。正解 = 每次 SelectTrader postfix 里 `GetWorldCorners` 取 `_traderCardsContainer` 顶边换算父节点局部坐标，按钮底边贴上去（任意分辨率自适应）。
9. **图集裁剪坐标系**：Unity 精灵 rect 左下原点；System.Drawing 左上原点 → `top = 图高 - (y + h)`。1.0 访问按钮资产在 ChatBar 图集(sactx-0-512x256)：`Social_Trader-Screen_Chat_Button_0`(亮=悬停)/`_1`(暗=常态) 187x32 + `Full-Call-Icon (2)_0` 图标。
10. **TMP 中文**：新建 TextMeshProUGUI 要从界面现有 TMP_Text 借 `font`，否则中文可能变方块。
11. **背景压制 3D 场景的时序**：场景加载异步数秒 → 压制判断用同步标志 `SceneLoader.Requested`（Open 立即置位），且 `SceneLoader.Open` 必须先于 `DialogBackground.Attach`。
12. **Bmpq 场景套路**：场景根抬升 y+300 防穿模；加载 bundle 前快照原生 shader 名字典按名替换（查无保留=正解）；相机用 `Cam2_fps_hideout` 预制体 + 关 Cinemachine + 补 AudioListener；`EnvironmentUI.ShowEnvironment(false)` 隐藏菜单环境。

## 任务系统

13. **任务动作**：accept/complete 走原生动作（`DialogAcceptQuestAction`/`DialogFinishQuestAction`，ClientDialogController 原生处理真网络事务，状态/奖励/面板刷新引擎全包）。**handover 不走原生动作**（原生 method_16 自动收集上交, 会把玩家舍不得的物品也收走——Tech Leader 否决）→ 行表+OnExecuteLine 开**原生上交窗口**：`ItemUiContext.Instance.HandoverQuestItemsWindow.Show(cond, 真实currentValue, items, profile, inventoryController, acceptCallback, true)`（玩家自选物品/数量, Therapist 交药同款）; accept 回调里 `quests.HandoverItem(quest, cond, selected, true)` 真网络事务, ContinueWith 只记日志; ⚠️ currentValue 必传 `quest.ProgressCheckers[cond].CurrentValue` 否则显示 0/N 且可超交（旧版 #14 老坑）。
14. **⚠️ successMessageText 必写**：空完成文案 → 服务端完成响应异常 → 客户端不确认成功就不把本地任务标 Success → 卡在"可完成"可反复交（重复拿奖励）+ 收到空邮件。任务 JSON 写 `successMessageText` 键 + locale 文案，两个问题一起消失。
15a. **⚠️ "对话完成型"任务的惰性卡进度条件（4.1 变化, 两次迭代）**：旧版"假地图 **Location** 探索计数器"——**4.1 战局载入直接判过**（地图条件是载入时全图判定）。一版改 Skill≥100——卡得住但**渲染出技能进度条 40/100**（数值型条件都会出条）被 Tech Leader 否。**终版 = CounterCreator + 假地点 VisitPlace value 1**：地点条件只在踩进对应区域触发器时触发, 假地点永不存在 → 永不判过, 且 value 1 二元显示 = 纯目标文字无进度条（旧版视觉）。目标文案走本地化键（键=外层条件 id）照写剧情文案。
15b. **触发点生成要等地图 id 就绪**：GameWorld 刚实例化时 LocationId 可能为空串, 空串双向子串匹配任何地图 → 全图长触发点；TriggerManager 在 LocationId 为空时跳过本轮（1 秒后重试）。
15. **默认门控**：选项无显式 if:/ifnot: 时按指令自动配门——accept=可接取(1) / handover=进行中(2) / complete=可完成(3)；显式门优先。作者须保证每个节点至少一个无门控退出选项（全滤空时原生只给红字 Back 行）。

## 工程/编译

16. **名字冲突三连**：EFT 全局 `Paths` 抢 `BepInEx.Paths`（全限定解决）；SPT 服务端 Tables 命名空间有自己的 `Path` 抢 `System.IO.Path`（using 别名）；`MongoID` 在 EFT 命名空间（新文件记得 `using EFT;`）。
17. **CS0165**：`a == null || !TryX(out err)` 短路求值 err 可能未赋值 → 拆成两个 if。
18. **服务端 4.1 API 速查**：`IModMetadata` 接口（每 mod 恰好一个实现）；`[Injectable(typePriority: OnLoadOrder.PostLoad)]` + `IOnLoad.OnLoadAsync(CancellationToken)`；注入表模式（TemplateTable/LocaleTable 直接构造函数注入，DatabaseService 已删）；`CustomQuestService.CreateQuest(NewQuestDetails)` 一站式任务+locale 注册（**务必查 result.Errors**）；`JsonUtil.DeserializeFromFile<T>`；`ISptLogger<T>` 在 `SPTarkov.Common.Models.Logging`；OnLoadOrder 4.1 新值：SaveCallbacks=600000 / PostLoad=1000000。
19. **老铁律（沿袭旧版）**：绝不在 Task.ContinueWith 里驱动对话/UI（跨线程崩溃）；Unity 的 null 判断写法不许简化；战局内对话不许裸 ESC 退出（剧情安全）。

## 阶段二: 交互与战局对话（侦察结论速查, 2026-08-05）

20. **4.1 交互系统真名**：`GamePlayerOwner.AvailableInteractionState` = 公开 `BindableState<AvailableInteractionState>`（旧 ActionsReturnClass）；元素 `EFT.UI.InteractionAction { Name, Disabled, TargetName, Action }`（旧 ActionsTypesClass）；`InitSelected()`/藏身处用 `DefaultSelected()`。`HideoutPlayerOwner : EftGamePlayerOwner : GamePlayerOwner` → 一个 FindObjectOfType 通吃战局+藏身处。**自定义交互点直写 state.Value**（旧版验证路线：replace 单动作菜单 + 距离/视角锥双门控；藏身处 merge 进原生区域菜单只摘自己那条 + 一帧握手）——**别走 `InteractionContextHelper.GetAvailableActions` 工厂**（未知 IInteractive 直接 throw，得加补丁才行）。F 键执行 = ActionPanel → `SelectedAction.Action()`。
21. **战局对话链（BTR/灯塔原生样板）**：战局 LocalPlayer **自带全套控制器**——Profile / QuestController(QuestControllerClientLocalGame) / InventoryController / **DialogController(ClientDialogController)** 全非 null（bot 除外）；原生开法 `new TraderDialogScreenController(profile, traderId, quests, inv, null, player.DialogController).ShowScreen(Queued)`。Queued 上屏会 Close 掉 BattleUI HUD、关屏自动恢复；游戏**不暂停**。**ESC 剧情安全是原生免费的**：`BaseDialogScreen.TranslateCommand => BlockAll` 吞 ESC（仅放行看表 3 命令）；`TranslateAxes` 空 override（轴放行）；`ShouldLockCursor => ShowCursor`。关屏唯一正道 = QuitAction → `ScreenController.CloseScreen()`。数字键 1-9 选行、空格跳动画。
22. **MessageWindow 战局陷阱**：`ItemUiContext.Instance.ShowMessageWindow(desc, accept, cancel, caption, time, forceShow, alignment)`——**战局内 `forceShow:false` 不弹窗直接当"已接受"**，必须 `forceShow: true`；窗口 ESC = Decline（触发 cancelAction），Y/N 快捷键。
23. **旧版触发器语法（4.1 恢复目标, 100% 兼容）**：`trigger: raid <map> (x,y,z) [dist 3] [radius 1.2] "提示"`；`trigger: hideout <area> (x,y,z) [node <节点>] [dist 3] [if 任务=状态/状态] [free] "提示"`。map 双向子串大小写不敏感；hideout 默认 merge、`free` 才自立+视角锥；同坐标两条互斥 if 门控 = "这里接任务/回这里交差"模式；F11 打印站立坐标是唯一保留调试键。任务门控严格语义：读不到状态（未接）= 隐藏。
24. **旧版战局防死锁配套（ESC 被封的代价）**：节点选项全滤空 → 不能空着（1.7 原生 fallback 是红字 Back 行，战局可接受但难看——作者规范：每节点留无门控退出项）；`setstatus:` 消费 = 直改任务状态（4.1 真名 `SetConditionalStatus`/`TransitionStatus` 公开可调，勿再补通知事件防双弹）。旧决策沿用：**触发器入口不上演 3D 场景**（对话框浮在游戏世界上，bg:/音频可用）。

## 交易窗口与死循环（2026-08-05 大案）

25. **⚠️ SetDialogProgress 回声死循环（帧率级刷服务端, [diag] 两轮实锤）**：对话行执行 → `method_13` 发 SetDialogProgress（走 `/client/game/profile/items/moving`, **对话进度=库存操作**）→ 响应应用把对话"恢复"回该行所在对话 → NPC 侧的话 `ExecuteLine` 尾部自动推进（`LastNpcLine.Execute()`）再执行 → 再发包……每次网络往返转一圈直到退游戏（症状: 主菜单转圈+服务端刷 moving+退出 session error）。**真正的点火器 = 屏幕管理器复活废弃对话屏**：Queued 新屏入栈太快（旧对话屏退场未完成）→ 新屏 PreviousScreen 被记成旧对话屏 → 新屏关闭时 `Show(旧controller)` 在**已 Dispose 的控制器**上 StartDialog(#npc) → 循环点火, 且保险丝 StopDialog 掐不死（每次回包 op-apply 都重建对话复活循环）。**铁律双条**: ①对话屏关闭后想再开任何 Queued 屏, 必须先等 `FindObjectOfType<TraderDialogScreen>()` 变 null（旧屏彻底退场, PreviousScreen 链干净）②程序化重开/恢复对话一律落 `节点#opt` 玩家侧选项对话（TryOpenAt atOptions）——玩家侧无自动推进, 就算被复活也点不着火。DialogFuse 保险丝保留作报警+尽力止损（1 秒>25 次行执行→StopDialog+关屏）。
26. **对话内交易窗口（正式版同款）**：`TraderScreensGroup.DialogTraderScreenController`（4.1 现成、无人用）= 普通交易控制器 + `UseUnderlay=>true` → 预制体自带 `_underlay` 半透明底衬激活 = "窗口感"，**无需任何美术**；配 `new[]{trader}` 单卡片列表。流程: @trade 行表命中 → 对话原生退出 → 构造 DialogTraderScreenController ShowScreen(Queued) → 其 OnClose（基类 Closed=true 触发）延一帧 TryOpenAt 在原节点重开对话（全管线重建）。背景连续性: DialogBackground 常驻独立层（挂屏幕容器 sibling 0, 所有屏幕后/3D 环境前）+ KeepAlive 标志跨交易窗口存活, mp4 不断播; 窗口期 TalkButton 用 DialogWindowOpen 静态标志隐藏。
27. **SPT 聊天 give 指令的 Critical 不是我们的锅**：`/client/mail/msg/send` 报 "ObjectId must be a 24-character hex string, but got Roubles" = 玩家在 SPT 聊天机器人里用物品**名字**当 ID 发 give 指令, SPT 自己的 GiveSptCommand 崩请求; 无害, 与 VisitAPI 无关。

28. **🏆 0.16.9.5 内含正式服「拜访」原生运行时（Narrate 家族, 全休眠零调用）**：`NarrateGame : AbstractGame`（完整小游戏模式: 生成第一人称 **NarratePlayer** 真玩家、传送进商人房间预设点位、看向商人、收枪 GetOffGesture——正式服"人站在房间里"的拜访体验）+ `TarkovApplication.NarrateController`（编排: 藏 hideout → VendorScenePresets 校验 → 加载场景 → 跑 NarrateGame）+ `VendorScenePresets`（硬编码 5 商人: Fence/Mechanic/Prapor/Therapist/Lightkeeper, 场景名 "Vendors_Mechanic" 等 + 公共 "Vendors_Scripts"; **字典是 public readonly 可运行时补商人**）+ `MainMenuShowOperation.LoadNarrateScene(traderId)/FinishNarrate()`（含离开确认弹窗）+ `ShowTraderDialogScreen`（原生开对话入口, 自带 OnActionFinished→DialogTradingScreenAction→单商人交易屏的**原生 @trade 桥**）。**休眠原因 = 场景数据没随 0.16.9.5 发货**（StreamingAssets 查无 Vendors_*; 代码在、资产缺、无人调用）。tarkin 包 = 正是这些正式服场景的重打包 → 理论可行的混合路线: 先加载 tarkin bundle 让场景名可解析, 再驱动原生 NarrateController（"启用原生"2.0, 需专项侦察 ModernLoadScenesFromPreset 的场景名解析层）。现役 SceneLoader = DIY 摆拍（场景抬 y+300 + 相机预制体）, 原生版是真人入景。

29. **🏛️ 全量考古: 0.16.9.5 内正式服对话系统的原生/休眠版图（四路侦察定谳, 2026-08-06）**
    - **内容管线（原生且 ACTIVE, 只差服务端）**: 客户端每次登录自动 `POST /client/dialogue`（EftClientBackendSession.DownloadTraderDialogs, GetGlobalConfig 内发起）→ `TraderDialogsDTO{elements:[TraderDialogTemplate]}` → `DialogStorage.AddTemplates`；模板 JSON 自带 `localization` 字典（SetupLocalization 直灌 LocalizationManager）+ `CanBeFirstDialogue`。SPT 服务端没实现该路由 → 客户端仅打 "Failed to get dialogs"。**服务端实现此路由 = 对话内容走 100% 原厂管线, 客户端零注册代码**。入口对话 = `traderSettings[trader].MainDialog`（SPT 也须下发）。`GetTraderDialogs(traderId)` 按商人参数存在但从未使用（半休眠）。`TraderDialogsBackendDTO`（编辑器节点图载荷, JsonConstructor 自动 Convert 成模板）零引用休眠。
    - **进度持久化（原生 ACTIVE）**: 每次点行 → items/moving `"SaveDialogueState"` + `nodePathTraveled:[{traderId,dialogueId,nodeId}]`（BackEndInventoryController 按 Queued 批量聚合）; 服务端应回放节点动作（权威效果）。跨会话状态 = **profile `"Variables"` 字典**（ProfileVariablesStorage; 检查点整数选分支 = 正式服 when/once/first 的原生形态: 喂 splitter 的行挂 DialogSetVariableAction(MainVariable,n,Profile域), splitter 子行挂 VariableValueCondition, StartPoints[splitter]=n——BackendDialogData.Convert 把这套编码写得明明白白）。休眠配套: IEftSession.SetVariableValue(items/moving "SetVariableValue", 零调用)、ProfileSetVariableOperation。任务条件已能监听对话变量（ConditionsConnectorsManager.OnVariableChanged）。
    - **动作全家桶（14 种）**: ACTIVE = SetVariable/SwitchDialog(带 SplitterId)/Quit/EmbedQuestDialog/SelectQuest/AcceptQuest/HandoverItem(自动收集版)/FinishQuest/SelectSubService/PurchaseService；休眠 = **DialogQuestRewardAction(PlayerReward, 携带完整 QuestReward 含 TraderStanding——正式服"对话发奖励/好感"载具, 设计上服务端回放时应用)**、DialogDiaryNoteAction(纯数据)、DialogQuestsScreenAction(无屏处理退化为关屏)、GeneratedDialogFinishQuestAction。台词宏: {standing}/{loyaltyLevel}/{salesSum}。
    - **条件全家桶（10 种全 ACTIVE 机制）**: VariableValue(带 ECompareMethod)/QuestStatus/**TraderReputation(原生好感门!)**/QuestConditionStatus/HasNewQuests/主副逻辑组(含 Random 权重)/HasItemForHandover/ServiceAvailable/CurrentTrader。4.1 没有等级/金钱/背包条件（正式版新增 HasFreeSpecialSlot/CompletableItem）。
    - **访问按钮 = 正式版专属代码**: DialogStartButton/DialogStartData/EDialogEntryPoint{InLobby,InRaid,ViaRadio,ViaNotebook}/ShowDialogStartButton 全部只在 rip 里, 0.16.9.5 只发了图集美术没发代码 → 我们的 DIY 按钮继续服役（美术反而是原厂的）。
    - **对话屏白名单四席**: Lightkeeper/BTR/**Prapor/Therapist**——后两位在 4.1 无任何调用者 = BSG 预接线的 Visit 商人。
    - **Narrate 补充侦察**: ModernLoadScenesFromPreset.LoadScene = 裸 SceneManager.LoadSceneAsync(name)（**零 manifest 耦合 → 预载 tarkin bundle 即原生可跑**）; 硬阻点 = ①Mechanic 不在 TraderIdToType（背后是可变 Dictionary, 强转可补; 缺席会 KeyNotFound）②NarrateGame.PlayerFactory 调 `Profile.SetSpawnedInSession(false)` **清全背包 FiR 标记**（须护住）③场景 bundle 须带 NPCObject+SequenceReader+Animator+uLipSync+三字典组件栈（正式服场景自带; NPCObject.Get NRE = 场景脚本没绑上的症状）④服务端须供 MainDialog+带 playback 的对话图, 否则秒 QuitAction。退出 = 对话 Quit → Hide → 回主菜单（场景驻留可快速二进; NarratePlayerOwner 吞 ESC 只放行 Enter/Console）。动画时间轴在对话数据(playback→CombinedAnimationData), 剪辑/口型烘焙在场景 bundle。ETraderType 4.1 无 Jaeger。
    - **无原生等价、DIY 继续服役**: 访问按钮 / 触发点交互（IInteractive 封闭 switch, 无通用 talk-to-NPC）/ 对话内上交窗口（原生 method_16 自动收集; HandoverQuestItemsWindow 唯一原生调用者是任务页 HAND OVER 按钮——我们的对话→窗口是超越原生的增强）/ .dlg 语言本身。
    - **休眠彩蛋**: DialogTreeView+DialogPlayerView = BSG 内置对话编辑器/模拟器（存读模板 JSON、剪贴板导入节点图、变量沙箱试玩, 无屏注册纯 prefab）; EventDialogScreen(Storyteller 硬编码)带打字机/地图/统计面板可借鉴; ESubtitlesSource.LighthouseKeeper 零使用。

30. **N1 服务端侦察定谳（SPT 4.1.1 服务端三路反编译, 2026-08-06）**：
    - **`/client/dialogue` SPT 已实现**: DataStaticRouter → DataCallbacks.GetDialogue → `GetUnclearedBody(templateTable.Dialogue)`（忽略 traderId 参数, 全量返回）。SPT 自带 `templates/dialogue.json` 6.1MB **仅 53 元素**（灯塔守护者/BTR/说书人三家）——8 个正式服商人的拜访对话不在内。**喂数据 = 注入 `TemplateTable`(DI) 往 `Dialogue.Elements`(List 可变, 属性本身 required init) AddRange, 无需注册路由**。未注册路由的下场 = `{"err":404,"errmsg":"UNHANDLED RESPONSE...","data":null}` 携 HTTP 200。双路由同 URL: 全部执行、后注册者响应胜出（mod 在 core 之后）。
    - **`TraderBase.MainDialogue` 字段已存在**（json 键 `"mainDialogue"`, string?）——与客户端 GlobalConfiguration.TraderSettings.MainDialog 严丝合缝; TradersTable : Dictionary<MongoId,Trader>, GetTrader(id).Base 直改即生效（traderSettings 每次现算）。全模型带 [JsonExtensionData], 未知字段不丢。
    - **`SaveDialogueState` SPT 已原生处理**: InventoryItemEventRouter 第 24 号动作 → InventoryController.SetDialogueProgress → 存 `SptProfile.DialogueProgress`(json `dialogueProgress`, **整表覆盖式, 只写不读**)。未知 Action（如休眠的 SetVariableValue）= 服务端 error 日志 + continue, **响应永远成功**（这就是之前 moving 洪水全成功的原因）。mod 加动作处理器 = 声明 `ItemEventRouter` 子类([Injectable], ItemRouteAction<TRequest>, TRequest : BaseInteractionRequestData), DI 自动收集, FirstOrDefault 按优先级选路由（mod 可抢注覆盖 core）。
    - **`PmcData.Variables` 原生存在**（BotBase.Variables, Dictionary<MongoId,int>, 序列化名 "Variables" PascalCase 正合客户端 ProfileDescriptor）; 新档初始化为空字典（老档可能 null 需护）; 战局结算已回拷、profile/list 已下发——**跨会话变量持久化的地基是通的**, 缺的只是"回放 SaveDialogueState 节点动作→写 Variables"这一环（N3）。
    - **profile 读写模式**: ProfileHelper.GetPmcProfile(sessionId) 返回内存活对象, 直接改=持久化（SaveServer 定时/登出落盘）; sessionId 来自 PHPSESSID cookie 每请求都有。mod 私有数据惯例 = 各模型的 [JsonExtensionData]。
    - **tarkin dialogue.json（11.7MB）的坑**: 编辑器时代格式——用 `IsStart` 而非 `CanBeFirstDialogue`（SPT 模型默认 true, 门自动开; IsStart 仅用于推导 MainDialogue）; 部分元素可能缺 required 字段（StartPoints/localization/SubTraders）→ DialogueLoader 用 JsonNode 预处理补空再 Deserialize, 坏元素逐个跳过; 与 SPT 自带 53 元素按 Id 去重（客户端 AddTemplate 本就覆盖式, 双保险）。
    - **N1a 落地实测（2026-08-06, 直启 SPT.Server.exe 抓 stdout 验证）**: 最终 `+39 element(s), 8 trader entry(ies) set`, 0 坏元素（文件 92 元素 - 53 与自带表重复 = 39 新增; 8 商人 MainDialogue 全部设上）。坑①: 编辑器导出把**空 localization 写成空数组 `[]`**（不是空对象）→ `??=` 只补缺失不治错型, System.Text.Json 报 "could not be converted to Dictionary" 整元素被跳（受害 2 元素都是真实商人的中间节点, 丢了变死链）→ 守卫改 `is not JsonObject/JsonArray`（缺失/null/错型一网打尽）。坑②修正前条: SPT 自带 53 元素**不止三家——已含新商人 6864e812 部分元素**（SPT_Data\database\traders\ 有其完整目录）, 与灌入数据有真实 Id 交集, 去重实测拦下 1 个重复。模型映射反编译核实: `MainTrader` ↔ json 键 `"Trader"`; `TraderDialogs.Elements` ↔ `"elements"`; `TemplateTable.Dialogue` ↔ `"dialogue"`。

31. **N2 原生 Narrate 拜访全链路（六路侦察定谳 + 实装, 2026-08-06）**：
    - **入口链**: 唯一总入口 `TarkovApplication.NarrateController.Show(Profile.ETraderType)`（TarkovApplication.cs:537）; 拿控制器走 `TarkovApplication.Exist(out app)` → `app.NarrateControllerAccess`。上层 `MainMenuShowOperation.LoadNarrateScene(MongoID)` 全程序集**零调用点**（官方 UI 入口不在 Assembly-CSharp）——按钮永远得我们自己接。Show 全自动: HideHideout → 查 VendorScenePresets → 加载场景 → NarrateGameWorld/NarrateGame → NarratePlayer(第一人称) → NPCObject.GoIn → TraderDialogScreen。前置: 已登录进主菜单(依赖 _menuOperation 的三控制器), 必须主线程。
    - **场景机制**: `VendorScenePresets.narrateScenes`（public 可变 Dictionary, 经 `NarrateController.Scenes` 静态字段）只有 5 条: Fence/Mechanic/Prapor/Terapevt(→"Vendors_Therapist")/Lightkeeper("Ligtkeeper_test_scene" BSG 拼错+无资产); 公共场景 "Vendors_Scripts" tarkin 包**没有**（ModernLoadScenesFromPreset.LoadScene 失败仅 LogError 不抛, 无害）。加载器裸调 `SceneManager.LoadSceneAsync(名, Additive)` 零校验——**预载 AssetBundle.LoadFromFile 即命中**。tarkin 7 房间场景名=正式服原名 Vendors_*（binary 扫 bundle 确认）, fence bundle 里 NPCObject/SequenceReader/uLipSync 字符串全在（rip 保留原生脚本引用, GoIn 演出链有戏）。SPT 客户端自身未发行任何 vendor 场景（globalgamemanagers/manifest 双查证实）。
    - **商人映射**: `Profile.TraderInfo.TraderIdToType/TraderTypeToId`（Profile.cs:154/194）九项无 Mechanic; **反向表是静态 ToDictionary 快照, 补映射必须双向都补**, 否则 method_2 的 `TraderTypeToId[source]` 直接索引 KeyNotFound。运行时强转 Dictionary 后 Add 即可（初始化器本就用 string→MongoID 隐式转换）。ETraderType 有 Mechanic(7) 无 Jaeger。
    - **FiR 天坑**: `NarrateGame.PlayerFactory` 调 `_profile.SetSpawnedInSession(false)`——遍历 `GetPlayerItems(All)` = **仓库+全身+任务物品+分拣台+藏身处仓全清 FiR**, 且 _profile 是真实后端 profile、SpawnedInSession 带 [Diffable]。护栏 = Prefix 打 `Profile.SetSpawnedInSession`, 守卫 `!value && Singleton<GameWorld>.Instance is NarrateGameWorld`（!value 放行 OnGameStarted 的 Scav set-true; 类型判断保住 raid 开局清标/bot 设标两处合法调用）。不能 skip PlayerFactory 本体（async 状态机, skip 连玩家创建一起没了）。
    - **退出流**: 唯一正路=对话行 DialogQuitAction → NarrateController.HandleActionFinished → Hide()（GoOut+ShowMenuScreenSync 回主菜单+只销毁相机）; ESC 被 BaseDialogScreen.TranslateCommand BlockAll 吞死, NarratePlayerOwner 只放行 Enter/Console, OnLeave→Stop 是死线**别去激活**（Stop 销毁玩家, 行为完全不同）。场景+Game 驻留供二次进入（快速路径）, 真卸载=开战局/登出时原生调 Unload, mod 不要自己碰。
    - **误伤面**: 拜访期间 `Singleton<GameWorld>.Instance` = NarrateGameWorld(ClientLocalGameWorld 空子类, LocationId 空)且 Hide 后不 Release; Singleton<AbstractGame> 却还是残留 HideoutGame——两单例矛盾, 判环境别混用。TriggerManager/VisitTrigger 已加 `is NarrateGameWorld` 守卫; VisitTrigger 的 owner 获取排除 NarratePlayerOwner（驻留玩家会污染 FindObjectOfType）。
    - **对话屏白名单**: method_5 switch 只放行 Lightkeeper/BTR/Prapor/Therapist 四 id（Fence/Mechanic 都不在!）——我们的 finalizer 补丁按 RegisteredTraders 放行, 原生拜访前把商人 id 加进集合即可, 零新补丁。
    - **客户端拉取链确认**: 登录 GetGlobalConfig 拉完 traderSettings 后 fire-and-forget POST /client/dialogue → DialogStorage; 模板构造时 localization 自动 Merge 进 LocalizationManager; 入口对话= TradersSettings[id].MainDialog（json "mainDialogue"→N1a 已灌）。StartDialog 校验: 模板在库 + HasTraderId + CanBeFirstDialogue。fire-and-forget 无重试, 登录秒开拜访理论上有竞态（实际人手速到不了）。
    - **实装**: NarrateEntry(CanVisit 四闸门+Show 协程+Mechanic 双向补映射+Fix 收尾) / FirGuard / SceneLoader.EnsureNarrateBundles(共享 bundle 缓存防 already-loaded) / TalkButton 原生分支(.dlg 优先) / 触发器双守卫。首发四商人 Prapor/Therapist/Fence/Mechanic。
    - **⚠️ 首测崩溃案（2026-08-06, IL 级定谳）**: 点火后 `Player.Init` NRE → 玩家半初始化 → 每帧 ProceduralWeaponAnimation.ApplyPosition NRE 刷屏。IL 侦查（CG_Init.MoveNext 偏移 0x5de）= **`EnvironmentManager.Instance` 在主菜单/藏身处语境是 null**（战局地图场景才自带; `Vendors_Scripts` 公共场景的真实作用之一就是携带这类管理器, tarkin 包没有它）。FiR 护栏/bundle 预载/白名单当次全部正常（日志实证）。修复: Visit 协程开头 `Instance==null` 时裸建 `new GameObject().AddComponent<EnvironmentManager>()`（Awake→Init 自动登记单例, 无室内触发器时全安全回退, 查环境默认 Outdoor）; Show 完成后把它 **SetParent 到 NarrateWorld** 下——原生 Unload 销毁世界时顺手带走（OnDestroy 自动清 _instance）。**铁律: 这个裸管理器绝不能活到战局加载**——地图自带 EnvironmentManager 的 Init 见 _instance 非空会自我停用, 战局室内音效/曝光会整场损坏; 挂 NarrateWorld 保证 LoadMapAndData→Unload 时序先行清场; Show 失败路径当场 Destroy。
    - 已知症状: 若拜访中途失败, 半初始化玩家会每帧刷 NRE 直到重启客户端（原生 Show 吃掉异常但不清理玩家）; 待稳定后再议是否加清理保险。
    - **⚠️ 第二颗雷（2026-08-06 复测）**: 上一颗过了（玩家建成、无刷屏），新崩在 `PlayerCameraController.Create` → `CameraManager.SetCameraFromSettings`：①`EffectsController.Awake/Init` NRE（IL 0x57f = `GetComponent<FrostbiteEffect>()` 为 null）②`CameraManager.SetFSR2` NRE（`_ssaaImpl` null, method_2 的 `GetComponent<SSAAImpl>()` 没拿到）→ 炸停 Show。根因: `Construct` 用 `Singleton<LevelSettings>.Instance` 取相机预制体, 拜访时那是**藏身处的简化相机**（无战局后处理组件栈）。修复 = 第 4 补丁 NarrateCameraGuard: Prefix `SetCameraFromSettings`, Narrate 世界时把 settings 置 null → 走原生退路 `UnityResourcesProxy.Load("Cam2")` 完整战局相机。
    - **三连撞墙的共同根因 = `Vendors_Scripts` 场景缺失**（EnvironmentManager / LevelSettings+相机预制体 / 后续未知）。该公共场景是正式服 Narrate 的"运行时底座", tarkin rip 包只 rip 了 7 个商人房间没 rip 它。休眠代码=未测试代码, 预计还有雷; 每颗都可绕但成本递增。若绕不动的雷出现（如需要正式服专属资产）, 退路 = 我们已验证的 DIY SceneLoader 路线（1.6 游戏内通过）。
    - 待打磨: 拜访加载屏当前是原生硬编码的 HideoutLoadingScreen（`_hideoutImage` 藏身处 logo + `_hideoutIcon` 转圈 + `_background` 黑底）; 正式服实拍 = 黑底+转圈+**商人名字标签**（Tech Leader 截图）→ 复刻 = 隐藏 _hideoutImage 换商人名 Text, 无需外部美术。

32. **N3 对话进度/变量持久化（四路侦察定谳 + 实装, 2026-08-06）**：
    - **协议真相**: SaveDialogueState = `{Action:"SaveDialogueState", nodePathTraveled:[{traderId, dialogueId, nodeId}]}`——**不带任何变量值**, 服务端必须自己回放节点动作。批量语义: Queued 行(非 Profile 变量)客户端只攒批不发网, 遇到非 Queued 行(Profile 变量/任务/退出动作)整批冲刷; 服务端要按数组多条目处理。json 名陷阱: 数组内是 `dialogueId`, 存档字段是 `dialogueProgress`, 请求字段是 `nodePathTraveled`。
    - **变量三作用域**（BaseTraderDialogController）: Dialogue=dictionary_1(换对话清) / Session=dictionary_0(控制器存活期) / Profile=`Profile.ProfileVariables`; 写级联 1→0→Profile, 读级联同序。**Profile 层登录来源 = profile JSON 顶层 `"Variables"` 字典**(ProfileDescriptor.VariableData)——服务端写 `PmcData.Variables`(BotBase, json 名 "Variables", 随 /client/game/profile/list 下发)即可闭环; 对话条件 VariableValue 和任务条件 GlobalVariableValue 都读这条链。Dialogue/Session 作用域**绝不能持久化**（会遮蔽剧情复位）。
    - **服务端现状**: 核心处理器一行整表覆盖 `SptProfile.DialogueProgress`（存全量档、**不随 profile/list 下发**, 客户端重登拿不到——所以变量才是正确载体）; 战局结算会用客户端提交档整块覆盖 Variables（raid 中服务端写会被冲, 但客户端本地已写同值→无损）。
    - **抢注姿势**: 继承 `ItemEventRouter` + `[Injectable(typePriority<400000)]`（核心全部 400000）; ItemEventCallbacks 用 FirstOrDefault **赢者通吃**——核心处理器完全不再跑, 必须复刻原版落盘那一行; DI 沿 BaseType 链自动注册, 零手动接线; 未知 action 只 log error 不崩; output.Warnings 非空会中断同批后续事件（别乱塞 Warning）。
    - **⚠️ 路线图修正**: `PlayerReward`(客户端类 DialogQuestRewardAction) 是**纯展示数据**——客户端 ExecuteDialogAction 对它无匹配分支、全库零消费点, 真奖励由 FinishQuest→QuestComplete 服务端发放; **回放它发奖=双发**, 禁做。同理 AcceptQuest/FinishQuest/HandoverItem/PurchaseService 四动作客户端已各自独立请求落账, 回放绝不能重复执行。正式服数据实测: SetVariable×3080(Profile 档 257) / PlayerReward×1 / TraderReputation×102 全是条件门——"好感/奖励服务端回放"实际无正式服消费者, 留待 .dlg 服务端翻译时再议。
    - **服务端解析 Lines**: TraderDialogElement.Lines 是 List<object>, 运行时元素=装箱 JsonElement(无 object 转换器), SPT 无任何 Line/Action 强类型模型——JsonElement.TryGetProperty 手读。
    - **落盘**: profile 内存活对象即权威(GetFullProfile/GetPmcProfile 返回引用), 定时(profileSaveIntervalSeconds)/登出/战局结束自动落盘; 急需时 SaveProfileAsync 手动（带锁+MD5 去重）。
    - **实装**: DialogueReplayRouter(50行) = 复刻 dialogueProgress 落盘 + 回放 SetVariable(saveScope=Profile)→pmc.Variables(null 护 ??=) + debug 日志; 服务端启动验证零错误。

33. **⚠️ 客户端对话数据"一颗老鼠屎坏一锅粥"（三路排查定谳, 2026-08-06）**——**本条是往客户端喂对话数据的头号铁律, N4 服务端翻译 .dlg 时必须遵守**：
    - **症状**: 拜访推进到对话屏后 `DialogStorage.GetTemplate(67d847a8...)` KeyNotFoundException（Prapor 入口对话）。查证该元素在服务端数据里**确实存在**且 MainDialogue 已正确设置——断链在客户端。
    - **根因**: 数据里 10 处 `"type":"HasFreeSpecialSlot"`（正式版新增条件类型, 0.16.9.5 **没有**）。`DialogConditionConverter`(EFT\Dialogs\) 遇未知 type **直接 throw 而非忽略**; 而 `JsonConvert.DeserializeObject<TraderDialogsDTO>` 是**整批 12MB 一次性解析**——单个未知类型 → 整个响应失败 → `DownloadTraderDialogs` 见 Failed 直接 return → `AddTemplates` 永不执行 → **DialogStorage 全空（92 个模板一个不剩, 连 SPT 自带 53 个也没了）**。`AddTemplates` 全库仅此一处调用点, 无补偿。
    - **诊断陷阱**: BSG 的 throw 文案拼的是失败后的 out 参数（default=VariableValue）, 会误报 "Unable to parse dialog condition type:VariableValue"。**真凶名字只在 `EnumHelper` 的 `Debug.LogError("Enum \"EDialogConditionType\" does not contain any value named \"XXX\"")` 里**——查日志搜后者。另: 失败是 fire-and-forget（登录不卡住）, 玩家只看到"对话查不到", 极易误判为数据没灌进去。
    - **客户端白名单（0.16.9.5 实测全集）**: 条件 10 种 = VariableValue / QuestStatus / TraderReputation / QuestConditionStatus / HasNewQuests / MainLogicalGroup / LogicalSubGroup / HasItemForHandover / ServiceAvailable / CurrentTrader。动作 14 种 = SetVariable / DiaryNote / SwitchDialog / QuitAction / TradingScreenAction / QuestsScreenAction / SwitchQuestDialog / SelectQuest / AcceptQuest / HandoverItem / FinishQuest / PlayerReward / SelectSubService / PurchaseService（我们数据里的 13 种动作**全部命中**, 只有条件那一种出局）。
    - **修法（服务端消毒, 不改原始数据）**: `DialogueSanitizer`(35行) 递归扫每个 Line 的 Trigger 条件树 + Actions, 白名单外**整行丢弃**。选丢行而非删单个条件, 因为 `BaseDialogConditionGroup.Test` 在 Conditions 空时 **return true**——裸删条件会让该行变成恒真/与另一分支重复。实测精确命中: `10 line(s) dropped`, 分布在 10 个独立 Line（不清空任何元素）。白名单机制对未来新类型（CompletableItem 等）天然免疫。

34. **N2 最后一公里（两路排查定谳 + 实装, 2026-08-06）**——数据消毒后**对话屏成功打开**（`templates=92, present=True`）, 剩两个收尾问题:
    - **⚠️ 退出卡死 = 裸多播委托断链**: `InvokeActionFinished` 是**普通 multicast delegate**, 按订阅顺序串行, **前一个订阅者抛异常 → 后面全部不执行**。订阅序: ①MainMenuShowOperation lambda → ②`TraderDialogScreen.ActionFinishedHandler` → ③`NarrateController.HandleActionFinished`。②的 `Close()` 会 `dialogController.Dispose()`（Dispose 的是 MainMenuShowOperation **共享的** controller）——一抛, `ScreenManager.TryCloseScreen` 的 `Closed=true` 与 `UnregisterInput()` **都不执行** → 对话屏永远留在 InputTree 里 `BlockAll` 吃 ESC; 同时③被截断 → `NarrateController.Hide()` 根本没跑 → 主菜单不出现。**"背景全黑"的判据 = `PrepareEnvironment()` 没跑到**（只有它开 EnvironmentUI 相机容器）。
    - 二级坑: `NarrateGame.Hide()` 六步全裸, 第(3)`player.Look(0,0)`、(4)`ResetCameraRotation()` 对我们**半初始化玩家**（无武器 → `HandsContainer.WeaponRootAnim` null, 日志里 ApplyPosition 每帧 NRE 就是佐证）**几乎必炸**; 一炸则第(5)`vmethod_1()` 跳过 → `InputTree.Remove` 不执行 → **输入第二重锁死**。另: `ShowMenuScreenSync` 是 fire-and-forget 且 `ShowScreenAsync` 有静默 return false 路径（`_closeAwaiter` 未完成时), 不抛不打日志。
    - 修法（第 5~7 补丁）: `DialogScreenCloseGuard`(Finalizer 吞 Close 异常, 保住 Closed/UnregisterInput) + `NarrateGameHideGuard`(Finalizer 吞异常并**补做** `vmethod_1()` 摘输入 + `PlayerCameraController.Destroy`) + `NarrateHideGuard`(Prefix 起 EnsureMenu 兜底协程, Finalizer 吞异常); `EnsureMenu` 90 帧内没等到 MenuScreen 就自己调 `ShowMenuScreenSync`（`_menuOperation` 私有 → AccessTools 反射, 需引用 Comfort.Unity 程序集）。
    - **⚠️ 黑屏根因 = 没 `SetActiveScene`（与 v1.6 能看见的唯一差异线）**: Additive 加载的场景**自带的 RenderSettings(ambient/skybox/fog) 默认被忽略**, 只有 `SceneManager.SetActiveScene()` 才切过去——我们 v1.6 SceneLoader 正好调了(SceneLoader.cs:50)所以可见, 原生路径无人调。雪上加霜: rip 包里 **"LevelSettings" 字符串 0 次、"Lightmap-" 0 次**（既无场景设置组件也无烘焙光照）, 而 `LevelSettings.ApplySettings()` 是原生唯一给 `_DirectionLightShadow`/`_MinAmbientColor` 等 **EFT 全局 shader 参数**赋值的入口（=0 意味着无阴影 shader 全黑）。所以几何体和相机都在, 但全是黑面。
    - 修法: `SceneLighting`(50行) = SetActiveScene + 环境光过暗时补 Trilight + 手动 `Shader.SetGlobalFloat("_DirectionLightShadow",1)`/`SetGlobalColor("_MinAmbientColor",...)` + `Uncover()` 连续 30 帧重申 `EnvironmentUI.ShowEnvironment(false)`（对话屏 PrepareEnvironment 会再关一次相机容器, 必须持续压制）; 退出时 `Release()` 切回原场景并恢复 EnvironmentUI。

## 测试资产

- Ragman 测试剧本：`D:\EFT\BepInEx\config\VisitAPI\5ac3b934156ae10c4430e83c.dlg`（背景/视频/音频/@visit/任务链全覆盖）
- 测试任务：`76697369746170695f303137`（"visitapi_017" 的 hex；上交 2 个五金件，+500 XP）；服务端数据在 `Server\db\`
- 场景包：`D:\EFT\BepInEx\plugins\VisitAPI\scenes`（2.3GB，不入 git）
- 按钮美术：`Client\art\visit_tab(.hover)/visit_icon.png` ← ChatBar 图集精确裁得

## 发布前清理清单（2026-08-05 打磨完成）

- ✅ F10 调试键已移除（F11 坐标打印按约定保留）
- ✅ 日志分级完成：开发期日志（[PoC]/[gate]/[diag]/[tab] requested/watcher armed）删除；作者诊断（[visit] start/[standing]/[setstatus]/[once]/[refresh]/[trigger]/[handover]/[retail]/[scene]）降为 **LogDebug**（默认隐藏, BepInEx.cfg 的 Logging LogLevels 加 Debug 可开）；警告/错误/[fuse] 保留
- ✅ 交易↔对话过渡的 1 秒主菜单闪现：TabRouter OnClose 时 `DialogBackground.Cover()` 把常驻背景层 `SetAsLastSibling` 提到所有屏幕上方（全屏 SORA 盖住过渡）, Attach 重开时落回 sibling 0；背景 RawImage `raycastTarget=false`（纯视觉, 永不挡输入）
- ✅ 美术已嵌入 DLL（csproj EmbeddedResource + VisitArt 走 GetManifestResourceStream "VisitAPI.art.<文件名>"；发布物=1 个客户端 DLL + 服务端 mod；部署时自动删散文件 art 目录）
- ✅ 终审（4 视角审查+逐条对抗验证, 14 验 10 实 1 否）修复清单：
  ① 背景纹理泄漏——DialogVideo.Play 顶掉旧图前销毁 Texture2D；DialogBackground.OnDestroy 补销当前 Texture2D（每次开关漏 ~8MB, 长会话无上限）
  ② SceneLoader 关屏与异步加载竞速——OpenRoutine 加载完成后、上台前查 Requested, 已取消则 Unload 退出（否则商人房间滞留主菜单+原生相机被停）
  ③ **.dlg 解析警告死码**（F10 移除时丢了唯一消费者）——DialogLoader 缓存未命中时逐条 LogWarning（每文件每次修改仅一轮, 无刷屏）
  ④ 服务端坏 JSON 灭服——JsonUtil 对存在但损坏的文件抛异常且 SPT 启动无兜底会整服停机 → QuestLoader.Parse<T> try/catch 记文件名跳过
  ⑤ **死链跳转防硬锁**——选项/JumpTo 打错节点名会点击时 KeyNotFoundException 被吞→IsBlocked 永真+ESC 封锁=杀进程才能出（比死循环还狠）→ OptionMap.Act 未知目标降级 QuitAction、Register JumpTo 查无回落 #opt、解析尾对全部 Target/JumpTo 校验入 Warnings（保留字白名单）
  ⑥ 访问按钮双击重入——Queued 上屏前数帧窗口期可开出两个对话 controller（PreviousScreen 复活废屏点火条件）→ 同步 _opening 标志+入栈后 Rearm（异步上屏判断必须同步标志, #11 老律）
  ⑦ first: 标记提前落盘——开屏失败（如服务端未装）也永久消耗首访 → 改为 TryOpenPlayer 成功后才 MarkFirst
  ⑧ 音频异步回调打到已销毁 AudioSource——回调内补显式 null 判（Unity 重载 ==）

## #35 黑屏的真凶：Vendors_Scripts 缺失 → LevelSettings 空 → Cam2 朽路（2026-08-06）

**误判纠正**：曾据 `root 'ROOT' pos=(95.10, 0.00, -98.90)` 判定"玩家被传送到虚空"，随即写 NarrateSpawnGuard 用场景相机点改写 `NarrateSceneInfo.playerPosition`。实测改写结果 `(116.10, 34.07, 0.35)` 与原生硬编码 `(116.20, 34.30, 0.70)` 几乎重合 —— **原生坐标本来就是对的**。根节点 Transform 的 position 不代表其子层级几何的世界位置，不可拿来判断"人在不在房间里"。补丁无害（自适应场景包）故保留，但它不是解药。

**真实根因链**（一根线串起三次撞墙）：
1. tarkin 场景包只 rip 了 7 个商人房间，没带公共场景 `Vendors_Scripts` → 日志 `Scene 'Vendors_Scripts' couldn't be loaded`
2. `Vendors_Scripts` 内含 narrate 场景的 `LevelSettings`（挂 `CameraManager.ISettings`，持 `CameraPrefab` / `PrismPresetPrefab` / `PostProcessProfilePrefab`）
3. `PlayerCameraController.Construct` 硬取 `Singleton<LevelSettings>.Instance` → null
4. `CameraManager.SetCameraFromSettings(null)` → 走 `SetCameraFromPrefab()` 无参兜底 → `UnityResourcesProxy.Load("Cam2")`
5. **Cam2 是条平时没人走的朽路**（战局/藏身处永远有 LevelSettings 带自己的 CameraPrefab），其上的 `EffectsController.Init()` 中途 NRE @ IL 0x57f
6. `EffectsController` 是后处理**总控**：逐个 `AddComponentCopy` 暗角/模糊/色差/热成像后 `.enabled = false` 关掉。Init 半途夭折 ⇒ 后半段特效组件被挂上相机却没人关，带未配置状态开着渲染 ⇒ **黑屏**
7. Init 最后一行 `AddComponent<SSAAImpl>()` 从未执行 ⇒ 这正是 `camera has no SSAAImpl` 三连警告的来源（UpscalerGuard 是治标，此处才是病灶）

**修法**：`HealthEffectsGuard` Prefix `EffectsController.Awake`，拜访期间（`Singleton<GameWorld>.Instance is NarrateGameWorld`）整个跳过 —— 不是去补半死状态，而是根本不进入。一次商人拜访不需要"受伤/中毒/失温"的健康特效。

**为何安全**（逐条核实过，非想当然）：
- `_effectAccumulators` 是**字段初始化**的 `new List<>()` ⇒ 跳过 Awake 后 `Update()` 遍历空表，no-op
- 组件实体仍在 ⇒ `CameraManager.EffectsController = Camera.GetComponent<EffectsController>()` 不为 null
- 其唯二消费者 `CameraManager.SetDoFFocalDistance`（FOV 变焦）与 `RainController`（雨天）**只在战局跑**，拜访期间不触发
- 跳过 Init ⇒ `AddComponentCopy` 一次都不调 ⇒ 相机只保留 Cam2 预制体自身授权的组件，不再有"加上却没关"的残留

**铁律**：EFT 里 `Singleton<T>.Instance` 取到 null 往往不是"该 T 坏了"，而是**承载它的场景没加载**。顺着 null 往上找缺失的场景，比在 null 处打补丁有效得多。

**待验**：若仍黑屏，看新诊断 `[narrate] camera fx: ...`（列出相机上所有 enabled 的 Behaviour），可直接点名肇事后处理组件，无需再猜一轮。

## #36 弃修 Cam2 雷阵，整体绕开原生 FPS 相机（2026-08-06）

**#35 修法的败因**：跳过 `EffectsController.Awake` 后 `_dof` 永远为 null，而装相机时 FOV 设置绑定（`SetCamera → method_2 → ApplyFoV → SetDoFFocalDistance`）**当场回调一次** → 在 null `_dof` 上 NRE → 这次没被吞，`TarkovApplication.HandleError` 弹 ERROR 窗、拜访中断卡死。教训：**设置绑定（BaseBindable.Bind）注册即回调一次**，"只在战局跑"的判断必须把绑定注册瞬间算进去。

**路线切换**：Cam2 兜底是连环雷阵（相机预制体上 30 个后处理组件个个可能是下一颗雷），逐颗拆不如整体绕开——**Narrate 拜访不需要原生 FPS 相机**，v1.6 的 SceneCamera 已实测能渲染 tarkin 房间。原生只保留它擅长的：装场景、驻留玩家、开对话屏。

**4 路侦察定谳的完整方案**（Move 下游/CameraManager/SceneCamera/PCC 生命周期，逐行核对）：
1. `NarrateCameraBypass`：Prefix `PlayerCameraController.Create`，narrate 世界跳过（返回值在 `Move` L167 本就被丢弃）。下游全部安全：`Destroy` 有 player 判空+TryGetComponent 双守卫成 no-op；`OnDestroy` 那串无判空的 `action_2()~action_6()` 因组件从未挂载而永不回调（上一轮日志里 OnDestroy NRE 的根源=Create 先 AddComponent 后 Construct，Construct 在 Cam2 链炸掉留下半死组件）；战局/藏身处不误伤（`ClientLocalGameWorld is NarrateGameWorld`=false，继承方向核实）。
2. **唯一必炸点**：`Move` L168 `CameraManager.Instance.IsActive = true` —— `Instance` 懒构造永不为 null（`instance ?? (instance = new ...)`），但 setter 是裸 `Camera.gameObject.SetActive(value)`。修法：`NarrateSpawnGuard`（Move 的 Prefix）里 `SceneCamera.Show(相机点)` 后**把我们的相机直赋公开 setter** `CameraManager.Instance.Camera = SceneCamera.Current` —— L168 随即变成"激活我们的相机"，顺势而为。
3. **铁律：严禁走 `SetCamera`/`SetCameraFromPrefab` 给 CameraManager 塞相机**——`method_2` 对预制体组件有硬依赖（`GradingPostFX.OnAfterDestroy`、`VisorSwitcher.Init`），裸相机必 NRE。只能直赋 L182 的公开自动属性。
4. `CameraNullGuard`：Prefix `CameraManager.set_IsActive`，`Camera == null` 时跳过打日志。战局里 Camera 恒非 null 直接放行，零影响；把边角路径的"必炸"降为一行警告。
5. **退出时序坑**：`SceneCamera.Hide` 末尾会把 `CameraManager.Camera` 重新 `IsActive = true`（v1.6 藏身处语义=恢复 FPS 相机）。narrate 语境下它指向我们自己刚关掉的相机=自我复活。所以 `EnsureMenu`（统一退出口，controller.Hide 的 Prefix 触发，覆盖返回按钮/F9/异常兜底全部路径）必须**先 `CameraManager.Instance.Camera = null` 再 `SceneCamera.Hide()`**，让 Hide 的守卫短路。
6. `HealthEffectsGuard`/`UpscalerGuard` 保留：Cam2 不再实例化后成死码，但作为兜底无害。

**已知非崩溃残留（记录不修）**：SceneCamera 自带 AudioListener 与全局常驻监听器并存（v1.6 起就如此，Unity 取最新启用者）；正音路线=改用 `AudioListenerConsistencyManager.Follow/Reset`，与"跳过 Create 后全局监听器停在 (0,-1000,0)、3D 空间音源听不见"一起留待音频专项。体积光/贴花等相机伴生渲染特性在拜访场景无消费者。

## #37 黑屏真凶终判：特效没人关——HealthEffectsGuard 反噬自家相机（2026-08-06）

**证据链**（本轮日志零异常，全靠 camera fx 探针）：我们的 SceneCamera（Cam2_fps_hideout 预制体）身上挂满 enabled 的后处理（NightVision/ThermalVision/GlobalFog/BloodOnScreen/PostprocessGrayscale/…）。**EFT 相机预制体出厂特效全开，`EffectsController.Awake→Init()` 的职责就是逐个 `.enabled = false` 关掉它们**。#35 装的 HealthEffectsGuard 在 narrate 语境拦掉 Awake → 我们自己相机的特效初始化被拦 → 特效全开 → 黑屏。删补丁即修复。

**统一了两轮黑屏的机制**：
- 原生 Cam2 黑屏 = Init 炸在半路（对比 fx 列表：Cam2 上**没有** FrostbiteEffect 组件，Init 末段 `_frostbiteEffect.enabled = false` 不判空 → NRE @ IL 0x57f 对上）→ 后半特效没关
- 我们 Cam2_fps_hideout 黑屏 = Init 整个被拦 → 全部特效没关
- v1.6 出画面 = Init 正常跑完 → 特效全关

**铁律**：EFT 相机预制体上那排图像特效是"默认开、初始化时关"的反常设计。任何"跳过相机初始化"类的补丁都会把画面打瞎——黑屏 ≠ 没渲染，往往是"某个未配置的后处理把画面 blit 黑了"。

**教训（对上一轮的我）**：#36 落地时忘了审视 #35 的旧补丁与新路线的相互作用——旧补丁拦"所有 narrate 语境的 EffectsController.Awake"，新路线恰恰在 narrate 语境实例化自家相机。切换路线时必须重审全部旧守卫的适用条件。

**遗留待查（返回报错）**：本轮点"返回"报错但日志无任何异常、无退出链——疑似点击后发 SaveDialogueState 网络事务收到服务端错误响应弹的 backend 红字（而非客户端异常）。待办：下轮实测时盯服务端控制台红/黄字；若是我们 DialogueReplayRouter 的响应格式问题会在那里现形。

## #38 台词不显示：N1a 只灌了结构没灌文本（2026-08-06）

**症状**：3D 房间出来了（#36/#37 相机线收官），对话屏只有红色 "? 返回"，没有台词——红字+?图标 = 缺本地化的典型脸。

**根因**：dialogue.json 每个元素带 `localization: {语言: {行ID: 文本}}`（中文文本齐全）。v1.6 客户端直读路线里 RetailDialogs.Load 有一步 `LocalizationManager.Instance.UpdateLocales(lang, dict)` 把文本灌进客户端——台词能播全靠它。N 阶段换服务端下发模板后这一步没了：客户端拿行 ID 查 locale 查无 → 行不渲染。

**修法（服务端权威，正路）**：DialogueLoader 聚合全部元素的 localization → 按语言合进 `LocaleTable.Global`，姿势照抄官方 CustomQuestService.AddQuestLocales：`lazy.AddTransformer(data => { foreach TryAdd; return data; })`（LazyLoad 变换器，locale 首次被读时应用；TryAdd 不覆盖官方既有条目）。客户端启动拉 locale 时文本自然到位，全语言一次修通、零客户端改动。实测：`45407 locale entr(ies) merged`，启动零错误。

**坑注**：`LocaleTable.Global` 键是 EFT 语言码（ch/en/ge/fr...）且 OrdinalIgnoreCase，与 dialogue.json 的语言键同一套，直接对上；SPT 注释警告"别直接用该属性、改动不保存"指的是绕过 LazyLoad 直接改值——AddTransformer 正是官方提供的持久注入口。**服务端控制台日志超长行会被折行显示，验证要读重定向文件而不是截屏**（这次差点误判为部署失败）。

## #39 台词不播的两道闸：CanBeFirstDialog + 变量种子（2026-08-06）

**症状升级**：locale 灌了 45407 条后台词依然全无——且不是"文本空白"而是**一行都不渲染、无选项**，说明对话树根本没被驱动（#38 的 locale 修复是必要不充分）。

**排除**：StartPoints 假说不成立——实测 92 元素的 StartPoints 全是合法字典（行ID→行索引），DialogueLoader 的错型守卫没碰过它。

**两道闸（v1.6b 当年全都踩过并修过, narrate 路线两道都没做）**：
1. `BaseTraderDialogController.InitNewDialog` L519: `(currentDialog==null || Id不符) && !template.CanBeFirstDialog → StringError` 拒绝起播。JSON 键 `CanBeFirstDialogue` 在 tarkin 数据里不存在 → DTO 默认 false → **开场即拒**。v1.6b 的解法就在 RetailDialogs L53-54: 手动 `t.CanBeFirstDialog = true`。
2. 起点行挂条件门（VariableValue 等), **引擎对不存在的变量条件评估失败**; 新档案变量表全空 → 起点全灭。v1.6b 的解法 = SeedVariables: 把"被条件读取但全数据集中无人写"的变量种成 Session 0（只种"永不被写"的 → 绝不覆盖 profile 持久值/N3 回放值, ScanRaw 已保证）。

**修法**：`NarrateDialogEntryGuard` Prefix `InitNewDialog`（仅 NarrateGameWorld）: ① DialogStorage 取模板设 CanBeFirstDialog=true ② RetailDialogs.SeedVariables。挂在起播入口 Prefix = 时机天然正确（起点评估之前）。InitNewDialog 只被开屏的 method_8 调用, 对话中途 SwitchDialog 走 method_0 不经过它 → 种子只在开局跑一次, 不会中途清剧情变量。

**配套重构**：RetailDialogs 拆出 Scan()（只扫 entries/seeds, 轻量幂等）与 Load()（_loaded 标志; AddTemplates/UpdateLocales 照旧）——narrate 路线只要种子, 不能连带把 92 模板在客户端再注册一遍（服务端已下发, 双源冲突）。**遗留待办**: .dlg @visit 零售路线的 Load 与服务端下发模板现在双源并存, 后续 N 阶段收官时要整合成单源。

## #40 台词第三道闸：幽灵任务条件（2026-08-06）

**排查路径**（子代理撞额度限制后主循环直查）：DynamicTraderDialog 构造器逐行过滤（L108-118: NPC 行只留第一条 + `trigger.Test(context)` 条件门）→ 全滤空时 `GenerateFallbackLine`（L214-220）生成 **红色 "Back" + QuestionMark 图标的兜底行** —— 与截图完全吻合, 实锤 67 行全阵亡。

**行条件实测结构**（主对话前 6 行全同构）: MainLogicalGroup(Or) → LogicalSubGroup(**And**) → [4×VariableValue==0, **2×QuestStatus, 1×TraderReputation**]。变量条件被种子/默认 0 覆盖 ✅; TraderReputation 读 TradersInfo.Standing（SPT profile 有, 新档 0 能过）; 凶手 = **QuestStatus**:
```
if (context?.QuestsData == null) return false;
q = QuestsData.FirstOrDefault(q => q.Id == QuestId);
return q != null ? Statuses.Contains(q.Status) : false;   // ← 任务查无 = false
```
tarkin 数据引用的是**正式服 1.0 的任务 ID**, SPT 0.16.9.5 任务库根本没有 → 每行的 And 组里必有一个 false → 全灭。正式服新档案上这些条件是 true（任务在库、状态 Locked=未解锁）。

**修法**: `NarrateQuestGhostGuard` Prefix `QuestStatusCondition.Test`（仅 NarrateGameWorld）: QuestsData 里查无此任务时**按 Locked 语义模拟**——`__result = Statuses.Contains(EQuestStatus.Locked)`。语义精确: "要求任务未开"的行照常显示（正式服新档等价）, "要求任务已完成"的行依然隐藏（永不误放晚期剧情）。任务真实存在时（含我们自己注册的 custom quest）放行原生逻辑。

**变量种子的作用域链核实**（顺手确认了 #39 的种子确实活着）: SetVariableValue(Session) 写 dictionary_1(Dialog 层)+dictionary_0(Session 层); GetVariableValue 依次查 dictionary_1 → dictionary_0 → ProfileVariables; method_8 的 ClearTempData 只清 dictionary_1, Session 层种子存活 ✅。

**对话起点机制附注**: method_0(dialogId, startingPoint=null) 开场不写 MainVariable; 起点=StartPoints{行ID:int} 把 MainVariable 设为 int 再重建对话（SwitchDialog 用）; 开场态 = MainVariable 默认 0 → "==0" 的行即开场白。DynamicTraderDialog 每次构建都全表过滤, "第一条通过的 NPC 行"即台词, 后续 NPC 行跳过(flag), 玩家行全收即选项。

## #41 N2 台词收官日：三道闸全通 + 两个收尾修复（2026-08-06）

**里程碑**：#38 locale + #39 CanBeFirstDialog/种子 + #40 幽灵任务 Locked 模拟 三件套齐活后, **台词/选项/对话流全部工作**（Tech Leader 原话"非常完美所有功能正常工作"）; 返回按钮实测真通了（Therapist 退出链由返回触发非 F9）。

**收尾修复两件**：
1. **回主菜单被第三方 mod 卡住**: Amands Graphics 的 CameraManager.Blur Prefix 在退出路上读 `IsActive` **getter** → 我们只守卫了 setter, EnsureMenu 清空 Camera 后 getter 裸解引用 NRE 连环炸（GesturesQuickPanel.Close → Blur → getter）→ ShowMenuScreen 中断, 要 F9 补刀。修: CameraNullGetterGuard 给 getter 也加判空（Camera null → false）。**铁律: 给属性加空值守卫时 getter/setter 要成对**——你不知道第三方 mod 会从哪边解引用。
2. **Therapist 房间青色渲染破碎**: 日志零异常=渲染问题非崩溃。青色渐变+窗外过曝=EFT GlobalFog（距离雾）典型色——雾需要场景参数, tarkin 房间没配 → far 深度像素糊青白。Therapist 特显眼（窗大+场景架在 y=40 高空, 窗外无任何几何=全 far plane）; Prapor 窗小带百叶所以上一轮没暴露。修: SceneCamera 建相机时禁 GlobalFog（反射按名, 商人室内房间无距离雾需求; v1.6 .dlg 路线同受益）。
3. 种子日志降 Debug（InitNewDialog 在对话内导航反复触发, 幂等无害但刷屏）。

**已知遗留（下一专项）**：
- **任务对话选项不工作**——"有活儿吗"任务话题/接交任务选项。方向: GenerateSelectQuestLines 走 `Context.QuestController.NewQuests(traderId)`, narrate 语境 QuestController 可能 null（v1.6b #7 老坑）或 QuestsData 未接; 需侦察 narrate controller 的 QuestController/QuestsData 装配点后接上 SPT 任务书。
- 商人动画/口型/配音专项; 加载画面商人名美术; AudioListener Follow/Reset 音频专项; @visit 零售路线与服务端模板双源整合。

## #42 Fence 抽奖开场 + Ragman/Skier 解锁（2026-08-06）

**Fence "点 4 次返回突然出对话"定谳**：起点行带 `RandomLineCondition`——引擎掷一个骰子, 行的 `[StartValue, EndValue)` 区间接住才显示; 正式服一组随机开场白的区间拼满全程保证必中。我们的幽灵任务闸放行的只是**部分**行 → 区间出现空洞 → 骰子掉进空洞=全灭 fallback; 点"返回"=SwitchDialog 重建对话=重掷 → 掷进幸存区间就出话。修: `NarrateRandomGuard` Prefix `RandomLineCondition.Test` narrate 语境恒真——NPC 行只取第一条幸存的（DynamicTraderDialog 的 flag 机制）, 等效"固定第一句开场白", 牺牲随机性换必然出话。待办: 想恢复随机开场白可改为"只对幸存行集合重新划分区间"。

**Ragman/Skier 解锁**：两人**已在** `Profile.TraderInfo.TraderIdToType` 原生映射表（不像 Mechanic 要补）, 只缺 `VendorScenePresets.narrateScenes` 条目（硬编码 5 商人）。该字典是 public readonly Dictionary=运行时可写。修: CanVisit 的 IsValid 闸失败时 `RegisterScene`——EnsureNarrateBundles 载 bundle → `GetAllScenePaths()[0]` 拿场景真名 → `narrateScenes[type] = new NarrateSceneInfo { sceneName = name }`（坐标不填, NarrateSpawnGuard 会用场景相机点改写; NarrateSceneInfo 全字段核实过裸建安全, Load 只用 sceneName）。首次打开这两个商人界面会因同步加载 bundle 顿一下, 后续缓存零开销。

## #43 退出清场 + Mechanic/Jaeger 归队（2026-08-06）
**"Ragman 房间多个商人"+"鄙视手势"同根定谳**：原生 NarrateController.Hide 是**复用设计**——game/world/玩家实体全不销毁（Show 里 `_game != null` 分支只 Move 搬人, 真销毁在应用级 Unload 的 _unsubscriber.Dispose）。但 1.0 新版 Vendors 场景把舞台统一锚在 (0,40,0) 附近（Therapist/Fence/Ragman 站位相距仅 2~3 米）→ 历次访问的玩家实体（穿 PMC 装备）+ 未卸载场景堆积互见。"鄙视手势"=NarrateGame.Move 尾部 `SetInteractInHands(EInteraction.GetOffGesture)`（驱赶手势）——残留玩家模型循环播它。铁证: Skier F9 退出后 Mechanic 走 .dlg 路线时 narrate 守卫日志仍触发（NarrateGameWorld 残留实锤, 顺带污染 .dlg 对话）。
**修法**: NarrateHideGuard 加 Postfix 清场四连——① `game.Stop()`（玩家 Dispose+ReturnToPool, End 回调置空引用）② `Singleton<GameWorld>/<IGameLevel>.Release(world)` + `Destroy(world.gameObject)`（不释放则下次 Show 的 Singleton.Create 必炸）③ `_unsubscriber` 换新 CompositeDisposable（旧 delegate 持有已毁对象, 防未来 Unload 二次释放; 字段混淆后全 public 直赋）④ 协程等 `Scenes.UnloadAll()`（NarrateScene.Unload 有 IsLoaded 守卫, 对没加载过的 Vendors_Scripts 安全）。每次访问全新建 world（多 ~1s 创建开销, 正确性优先）。
**Mechanic 定谳**: config 目录躺着 SORA 演示 5a7c2eca….dlg, 而 TalkButton 分流 .dlg 优先于 narrate（modder 覆盖语义, 优先级本身正确）→ 被挤进老 @visit 路线（台词空+受 world 残留守卫污染）。官方拜访对话数据其实齐全（服务端 dialogue.json 有 IsStart 条目, MainDialogue=68c960fa858010816fc886c0 已灌）→ 改名 .dlg.demo 让位即通。想恢复 SORA 演示改回后缀即可。
**Jaeger 定谳**: 0.16.9 的 ETraderType 枚举根本没有 Jaeger（None/Lightkeeper/Btr/Ragman/ArenaManager/Prapor/Terapevt/Fence/Mechanic/Skier/Peacekeeper 到 9 为止）, 但官方对话（68cc0914aa2dd1fe171e75a1）和场景 bundle 都在。修: 映射表塞占位值 `(ETraderType)10`（越界枚举 int 合法, narrate 链路里 type 只当字典 key 用）+ NPCObject.Get 容错 Prefix——bundle 里 NPC 组件序列化的 _npc 枚举值来自提取版客户端不可知, 查无 key 时按 `gameObject.scene.name == info.sceneName` 从 _npcs 字典兜底匹配。
**服务端对话数据盘点**（db/dialogues/dialogue.json 的 IsStart 条目）: Prapor/Therapist/Fence/Skier/Ragman/Mechanic/Jaeger/BTR 司机/Lightkeeper + 3 个 1.0 新商人 ID（688246…/6864e8…）——比想象的全, 后续解锁按同款配方（映射+RegisterScene）。

## #44 原生包首测失败诊断（未定谳, 明早接力）（2026-08-07 夜）
**已回滚**: tradermod therapist 已还原, native 两包停在 `scenes\bundles\tradermod_backup\native_pending\`。复测时移回+挪走 tradermod therapist 即可。
**通了的**: vendors_scripts_native/therapist_native 都被客户端加载; `Vendors_Scripts` 场景**史上首次加载成功**（levelsettings/lightManager 节点都出现在日志）; Therapist 场景 roots=2 renderers=434 lights=44（vs tradermod 39 renderers, 原生内容量十倍）; 我们的加载适配链（EnsureNarrateBundles + commonScenes）全程无错。
**炸了的**: ① bundle 内 MonoBehaviour **大面积 Missing Script**（therapist_model×18/Trader_Elvira_head/Position_Camera_Therapist/levelsettings/lightManager 全中）→ NPCObject 没注册 → 原生 method_0 抛 "NPC Terapevt not loaded" 弹窗（我们 NarrateNpcGuard 场景名兜底也 miss——字典就是空的）② 画面惨白: dummy shader 的 fallback 'Hidden/Internal-BlackError' not found 刷屏, SceneShaders.Fix 没起效或快照(546 个, 主菜单抓的)缺 p0/Reflective 系列 ③ 左下角出现战局 UI（"战局 #3 等级69..."）= Show 异常后状态泄漏, 退出后自愈, 属次生灾害。
**Missing Script 嫌疑排序**:
1. **1.0 混淆类名 ≠ 0.16.9.5 混淆类名**（BSG 每版本混淆名重排）——混淆组件必死, 这部分无解, 但 NPCObject/SequenceReader/LipSyncDictionary/LevelSettings 是明文类, 理论上应该能绑。
2. **明文类是否真 missing 未实证**——missing 日志只打 GameObject 名不打类名。验证法: 客户端加探针, 场景加载后 GetComponents(typeof(Component)) 数 null 分量 + 对 therapist_model 找 NPCObject/uLipSync 组件是否存在。
3. **player 侧 MonoScript 解析线索**: bundle 的 MonoScript 记录 assembly "Assembly-CSharp"（stub 保名）; 运行时游戏 Assembly-CSharp 有同名类。若明文类也 missing → 查 fileID 匹配（FileIDUtility hash）或 FixPluginTypesSerialization 干扰或 TypeTree 反序列化问题。
4. 备选路线: 打包工程改用**按 SPT 版 Assembly-CSharp 做 stub**（类名集=0.16.9.5, 混淆组件在导出 yaml 里本来就引用 1.0 名——需要先做"1.0 名→SPT 名"的场景 yaml 重写? 工程量大, 先验证嫌疑 2 再决定）。
**shader 修法备选**: 快照改在拜访时机重抓（bundle 载入前后都抓一次）; 或 Fix 扩展到 Vendors_Scripts 场景 roots; 或收集器把 p0 系列 shader 换成引用剥离。

## #45 Missing Script 定谳：源生成器类型是总开关（2026-08-07）
**#44 的三个嫌疑全部推翻**。实测工具: `BundleProbe`（AssetsTools.NET 解剖 bundle, 数 MonoScript / 脚本指针空否 / 组件字节数）+ `ScriptAudit`（Mono.Cecil 枚举类型 + 自实现 MD4 复算 Unity 脚本 fileID, 把场景 yaml 引用逐条判生死）。两个工具在 scratchpad, 复现方法见本条末尾。

**推翻嫌疑 ①（1.0 混淆名 ≠ SPT 名）**: 打包工程里的 `Assembly-CSharp.dll` 类型数 **8642, 与 D:\EFT 的 SPT 版一字不差**——"Managed 冒充 hack"让 AssetRipper 直接用 SPT 程序集命名, 所以 rip 出来的类名天生就是 0.16.9.5 的真名。逐条裁决: 场景 87 个脚本引用里 **75 个可绑定 / 12 个必死**（那 12 个是 1.0 独有类, 连打包用的 DLL 里都没有）。**NPCObject / SequenceReader / LipSyncDictionary / LevelSettings / EnvironmentManager / NPCAnimationsEventReceiver 全在可绑定名单**。备选路线 4（"改用 SPT 版做 stub"）其实早就是既成事实, 不必再做。
**推翻嫌疑 ③（player 侧 fileID/序列化白名单）**: fileID 全部对得上（MD4 算法自校验通过, rip 表命中率 100%）, 与 FixPluginTypesSerialization 无关。
**真凶**: `Assembly-CSharp(-firstpass)` 是**唯二过了 AssemblyStubber 的 DLL**, 而 stubber 把**每个**方法体换成 `ldnull; throw`——包括 Unity 2021+ 编译期注入的 `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.Get()`。Unity 每次域重载都会 Invoke 它（原生栈 `MonoManager::AnalyzeDomain → MonoScriptInfoScraper::ScanForSourceGeneratedMonoScriptInfo → GetAssemblyScrapedMonoScripts`）→ 拿到 `throw null` → 报 "Assembly is incompatible with the editor" 整个不加载 → 场景里 81/87 个 m_Script 在构建时被写成空指针。日志侧铁证: build.log 里**没有任何 "Unable to resolve"**（不是缺引用）, 且被拒的恰好是唯二 stub 过的两个; uLipSync/Meta.XR.Audio 同样带这个源生成器类型却没被 stub, 所以一路正常。
**修法**: AssemblyStubber 加 `--keep-sourcegen` / `--rename=` 两个开关, 默认**摘除**整个 `UnitySourceGeneratedAssemblyMonoScriptTypes*` 类型（含嵌套）再 stub 其余。摘除后编辑器不再拒载、不再崩。
**战果**（therapist 包解剖对比）: MonoBehaviour 168 个里 **已绑定 159 / 空指针 9**（首测是 5/158 全灭）; MonoScript 从 7 涨到 35; NPC 七件套 + uLipSync 全家 + LimbIK×10 + 环境音/贴花/灯光组件全部就位。**"NPC Terapevt not loaded" 弹窗的根因已消除**。

**⚠️ 遗留（下一步的真问题）: 组件绑上了脚本, 但序列化字段全丢**——所有 `Assembly-CSharp` 组件在包里都是 **32 字节 = 只剩 m_GameObject/m_Enabled/m_Script/m_Name 四个基类字段**（对照: uLipSync.BakedData 平均 73KB 真数据）。判据是 `MonoScript.GetClass()`: EFTAssembly **0/2323 解析成功**, uLipSync **16/16**。差别就在源生成器类型——**Unity 2022 靠它建 "MonoScript → 托管类型" 映射, 没有它就只建得出 MonoScript 壳子, 构建时反射不到字段, 只写基类部分**。死局在于: 留着 EFT 那份 → 编辑器 SIGSEGV（实测 3 次: 原版/仅保留该类型体/改名程序集身份, 全崩, 崩点都在 ScanForSourceGeneratedMonoScriptInfo）; 摘掉 → 不崩但没字段。
**已排除的两条歧路**（省得下次重走）: ① Unity 保留名冲突——加 asmdef 消掉预定义程序集后 `Type.GetType("EFT.NPCObject, Assembly-CSharp")` 能解析、domain 里的 Assembly-CSharp 确实是我们的插件（15085 类型）, 但 GetClass() 依旧 0; 连 Cecil 真改程序集身份为 EFTAssembly 也没用。② .meta 的 `Editor: enabled 0`——改成 1 无变化。
**下一步设计方案（未动工）**: 数据本身**在工程里是全的**——yaml 里 NPCObject 明明白白写着 `_npc: 5`（Therapist）、`_onEnter` 事件表。所以走"让 Unity 自己编译类型"这条路: ① 用 Cecil 从 SPT 程序集给需要的那批类生成 C# 桩（namespace/类名/序列化字段照抄, 递归带上字段用到的枚举与可序列化类型）② 塞进一个 asmdef（如 `EFTScripts`）让 Unity 亲自编译 → 源生成器由本机 Unity 产出, 既不崩又有完整类型 ③ 把工程里所有 yaml 的 `{fileID: <hash>, guid: <DLL guid>}` 改写成 `{fileID: 11500000, guid: <桩 .cs guid>}`（映射表 ScriptAudit 已能直接产出）④ 打包 ⑤ 用 AssetsTools.NET 把包里 MonoScript 的 `m_AssemblyName` 从 `EFTScripts.dll` 改回 `Assembly-CSharp.dll`, 运行时即绑到 EFT 真类。
### #45b 游戏内首测（修复后, 2026-08-07）: 弹窗根因确认消除, 但空字段组件把游戏打崩
**验证成功的**: `[narrate] NPC matched by scene for Terapevt`——**NPCObject 真的注册上了, "NPC not loaded" 弹窗根因确认消除**; 场景 `roots=2 renderers=434 lights=44`; 相机绕行/FiR 护栏/对话入口解锁全部照常; shader 修复大获全胜——**546 个快照只漏 2 个名字**（`Characters/TraiderHair`、`Custom/Billboard_FogSheet_Simple`）, 惨白问题基本解决。
**崩溃链（就是遗留的空字段问题变成实伤）**: `AmbientLight` 的 shader 字段是 null → `AmbientLight.Initialize` 每帧 `new Material(null)` 抛 ArgumentNullException → `OnRenderObjectManager.OnRenderObject → AmbientLight.InitAmbientBuffer → CommandBuffer.DrawMesh(null material)` 继续抛 → 攒出的 `AmbientLight+CamSettings` 被 GC 回收时在**终结器线程**调 `RenderTexture.Release()` → `Graphics device is null` → 硬崩闪退。同源但只在 Awake 抛一次的还有: LocationScene / GrassTrampler / WindowBreakerManager(Instantiate(null)) / StaticDeferredDecal(→Renderer.ComputeHash) / LightKeeperEyeTargetFollower / LightKeeperEyesBlinking。
**止血修法 NativeSceneStrip(45行)**: 订阅 `SceneManager.sceneLoaded`, 在原生场景加载的当帧 `DestroyImmediate` 掉**所有来自 Assembly-CSharp 的组件, 只留 NPCObject**（原生 `NPCObject.Get` 要查表）。作用域用 `SceneLoader.IsNativeRoom`（房间包文件名以 `_native` 结尾）+ `Vendors_Scripts` 圈死, **绝不碰 tradermod 房间**（Prapor/Fence 等仍走老包, 不受影响）。Show 完成即 Disarm, 另在协程里补一次显式 Strip 兜底。代价: 原生房间退化成"几何+灯光+静止 NPC", 演出要等字段问题解决。

**复现命令**: `AssemblyStubber <in.dll> <out.dll> <搜索目录> [--keep-sourcegen] [--rename=NAME]`; Unity 构建 `Unity.exe -batchmode -quit -accept-apiupdate -projectPath E:\项目\VendorRepack\Project -executeMethod VendorBundleBuilder.Build -logFile <log>`（`VendorBundleBuilder.Probe` 是诊断入口, 打印每个 DLL 的 MonoScript 数与 classResolved 数）。**Unity 必须走 Start-Process 直通运行**（沙箱下静默失败, 连 log 都不生成）。工程里新增 `Assets/Editor/VendorRepack.Editor.asmdef` 防预定义程序集同名冲突。

## #46 1.6c 零售直连首测：台词全灭 = N2 台词闸随 NarrateExitGuard 陪葬（2026-08-13）
**症状**：按钮/开屏/3D 场景全通（`92 templates` 注册、`Vendors_Prapor staged`），但对话屏只有红色 "? 返回"——与 #38/#40 的兜底脸一模一样（DynamicTraderDialog 全行滤空 → GenerateFallbackLine）。
**根因**：#40 幽灵任务闸（QuestStatusCondition 对 SPT 任务库查无的正式服任务 id 恒 false）+ #42 随机区间空洞（RandomLineCondition），N2 的两个修复补丁挂在 NarrateExitGuard 里且带 NarrateGameWorld 门禁——阶段 N 摘除时随文件删除；菜单语境下它们本来就是 no-op，1.6b 验收浅（@visit 跳转演示）没暴露，直连开屏一上强度就露馅。
**修法 RetailGuards(28行)**：复刻两闸为**无门禁全局版**——①QuestGhostGuard: QuestsData 查无此任务才接管，`__result = Statuses.Contains(Locked)`（语义同 #40：要求未开的行放行、要求完成的行照旧隐藏；真实任务含 SORA custom quest 走原生逻辑）②RandomAlwaysGuard: 恒真（#42 同款，固定第一句开场白）。全局打安全的依据：这两个条件类型只存在于 BSG 对话数据，.dlg 管线走自建 QuestGate（DEV_NOTES 顶部 #5）。
**遗留观察**：日志有两行 `Array index (8/9) is out of bounds (size=0)`（场景 staged 后、无堆栈的 Unity 原生侧报错），疑与商人动画/口型/配音链相关（#41 遗留专项），非致命，台词修复后复测观察是否仍在。

## #47 零售对话的交易/任务话题卡死 → RetailTabs 接棒切页签（2026-08-13）
**症状**：零售对话里点"交易/任务"话题选项后对话僵住。
**机制定谳**（反编译）：这两个话题的 NPC 应答行挂原生 `DialogTradingScreenAction`/`DialogQuestsScreenAction`——`ExecuteDialogAction` 里它们与 QuitAction 同走 `isQuit=true` 分支（**原生设计=点了就退出对话**）；正式版主菜单流程（MainMenuShowOperation）靠订阅 `_dialogController.OnActionFinished` 接棒拉起商人屏。我们的直连路线没有接棒者 → 退出后没下文，观感即"卡死"。
**修法 RetailTabs(29行)**：RetailOpener 给 dc 订阅 OnActionFinished（官方同款姿势）——TradingScreenAction→`tsg.SetMode(Trade)` / QuestsScreenAction→`SetMode(Tasks)`；Route 协程先等对话屏自行关闭（90帧超时后 `Close()` 强关兜底），再等底下 TraderScreensGroup 活跃后 SetMode。ETraderMode = Trade/Tasks/Services。
**1.9 老坑复核**：当年"OnActionFinished 对自制动作不触发"的教训不适用此处——这里是原生数据里的原生动作、原生 Execute 链广播，正是官方消费者依赖的路径。
**改版（同日 Tech Leader 提醒拍板）**：初版"关对话→主菜单商人屏 SetMode 切页签"与 .dlg 路线体验不一致——.dlg 的 @trade 开的是 **DialogTraderScreenController 叠窗**（UseUnderlay 独立实例，不是主菜单那个屏），关窗后对话自动重开、3D 场景 KeepAlive 不闪断。RetailTabs 改为 TabRouter 同款叠窗流程：OnActionFinished → KeepAlive=true → 等对话屏自关(90帧超时强关) → 起 DialogTraderScreenController(任务话题在 MenuUI.TraderScreensGroup 激活后 SetMode(Tasks), Trade 是默认模式不用切) → OnClose 时 Cover()+RetailOpener.TryOpen 重开零售对话（零售对话无节点概念, 重开=回开场白, 可接受）; 失败路径统一 DialogBackground.Discard() 兜底。复用 TabRouter.DialogWindowOpen 旗标压访问按钮。坑: AchievementsControllerClientBackend 在 EFT.Quests 命名空间（缺 using 编译当场报 CS0246）。
**实测修正（同日）**：游戏内验证叠窗流程通了，但对话停留 1~2 秒才开窗——日志两次点击都命中 "dialog did not self-close, forcing"，**实锤直开路线下对话屏从不自关**（isQuit 只在窗口的选项点击路径被消费；屏幕动作挂在 NPC 应答行上由 SetCurrentDialog→LastNpcLine.Execute() 执行，这条路的 isQuit 没有消费者）。修：等待自关的宽限期 90 帧→3 帧（约 50ms，只避开动作执行当帧拆屏），到点直接 Close()，不再当异常记日志。

## #48 全商人零售对话审计 + 幽灵任务闸放宽到早期状态集（2026-08-13）
**起因**：Tech Leader 报"所有商人缺台词和对话选项"。离线审计 12 商人 1706 行（工作流逐商人分析 + 脚本按插件真实语义重算）。
**审计结论**：
1. 内容 97% 都活着——开局只见问候+基础选项是**导航设计**（问候语 SetVariable 后选项逐级浮现、子话题逐层展开），不是丢失。
2. 真死 23 行（13 NPC + 10 选项），全部死于"引用 1.0 库外任务且 statuses 不含 Locked/0"：重灾区新商人A(68824651, 7 选项死 5)、Skier(6+1)、Jaeger(5)、Prapor(2)；大剧本新商人C(6864e812, 1024 行)零死亡。
3. Peacekeeper 在 dialogue.json 无 IsStart 入口 → 无按钮属数据决定；BTR 载人/护送选项挂 ServiceAvailable(战局服务条件, 菜单语境不可用属合理)。
**修法（Tech Leader 拍板选项1）**：QuestGhostGuard 的模拟从"statuses 含 Locked"放宽到"含 Locked/AvailableForStart/Started 任一"——正式服新档~前期档等价语义；救回 13 NPC + 6 选项；要求 Success/AvailableForFinish 的后期剧情行依然拦住不剧透。新商人A 剩 4 个选项按设计不救（真要求 1.0 任务完成）。
**审计工具坑两枚**：①dialogue.json 的 localization 表键**不是** Lines[].Id（是另一套 id）——按行 id 查文本会得出"中文全缺"假象，实际游戏内正常；②多代理并行判读"statuses 数组里的数字 0"口径不一（0=Locked 枚举值, 应放行），跨代理审计后要用确定性脚本按真实语义重算校准。审计脚本存 scratchpad\audit（extract_dialogs.ps1 / classify.ps1）。

## #49 放宽状态集翻车 → "每任务单一模拟状态"定版（2026-08-13）
**#48 的选项1实测翻车**：放宽到"含 Locked/AvailableForStart/Started 任一即过"后，问候消失、台词乱套。根因是我审计模型的盲区——**引擎只播"第一条通过条件的 NPC 行"，且该行的 SetVariable 驱动整棵对话树的导航**。放宽让同一任务的"未开始/进行中"互斥分支**同时**为真：复活的状态变体行插队顶掉正确问候，执行了错误的变量动作，下游选项（等正确变量的）全灭。教训：**行存活不是独立事件，改条件语义必须过"首行抢占+变量副作用"这一关**。
**对话流模拟器定谳**（scratchpad\sim.ps1 / sim2.ps1，可复用）：开场=空变量选首个存活 NPC 行→执行其 SetVariable→重算选项；BFS 点击可达性。结果：Prapor/Therapist 窄宽语义无差（健康）；**Skier 窄语义卡死在状态1**（问候池被其个人任务 68f0f5516871d33422038cd2 全杀，备胎 line13 设错导航变量→交易(需var==5)/任务(==9/==3)永不可达）；Jaeger 窄语义本来就通（5 条死行只是台词变体）。
**定版修法**：QuestGhostGuard 改为**每库外任务恒定一个模拟状态**——SimStatus 表默认 Locked，仅 Skier 个人任务指定 Started。同一任务所有条件读同一状态 ⇒ 互斥分支保持互斥，零抢跑。模拟验证：Skier(Started) 问候回正(line0)、交易+任务对话全通；其余商人不受影响。以后再遇"某商人剧本没有未开始路线"，往 SimStatus 加一行即可。

## #50 零售对话"缺台词"终局定谳：入口对话文本不存在于我们拥有的任何数据（2026-08-13）
**排查链**（全部实证）：①入口选择无误——13 个 IsStart 元素无一是任务对话（29 个 quest dialogueId 全在非 IsStart 元素里），Mechanic 入口与 N2 验证过的 MainDialogue 一致；②tarkin dialogue.json 内嵌 localization 合并后仅 2671 个 ch 键，对**全部 13 个入口元素的行覆盖率 = 0%**（入口行 id 是 2025-11 后授权的 1.0 正式版新内容，2671 键全是 2025-05 前的老行=任务对话文本）；③SPT 全局 ch.json（2.8MB）无这些行 id；④SPT 原生对话表（templates\dialogue.json, 53 元素）全是任务对话、零 IsStart；⑤**1.0 客户端资源里挖到 TestDialogues.json（_ripwork\Export\...\Resources\, 12.7MB）——与 tarkin 文件同源同缺**（92 元素/2671 键/入口 0 覆盖），证明 tarkin 的导出没有丢东西，是 1.0 架构本来就把新内容的文本挪去了**服务端全局 locale**（老内容才内嵌在元素里）。
**用户症状全解释**：入口问候/话题选项没有文本 → 不渲染或渲染键名 → "缺少开场白"；屏上唯一有中文的行都是 2671 键覆盖的**任务对话**内容 → "选项明显是某个任务里面的对话"。这不是条件门问题（#46-#49 的修复方向都只是次要因素），**是文本资产缺失，代码修不出来**。
**旧版考察结论**（Tech Leader 指示）：VisitAPI Framework 从未实现过零售回放——它的路线就是给商人写 .dlg 剧本（Mechanic 演示即例）。
**出路**：①搞 1.0 服务端全局 locale（OpenTarkov database 仓库应有——用户在项目组内）→ 客户端把行 id 键合并进 LocalizationManager 即文本齐活；②按框架本来的路线给原版商人写 .dlg 剧本；③两者混合。当前部署基线（Locked 语义+SimStatus 表+叠窗）保持不动，文本到位前入口对话无解。

## #51 阶段 N 复活：Debug DLL 反编译精确找回被删代码（2026-08-13 深夜）
**#50 撤销**：入口对话文本**没有缺失**——引擎的行文本查找是 `GetLocalizedLineDescription()` 读 **AnimationData.subtitleKeysWithParams（字幕键）**，不是行 Id！按字幕键重测：13 个入口元素文本覆盖率 **100%**（Prapor line0 = "怎么样，塔科夫还好混吗？"）。#50 的"0 覆盖"是用行 Id 当键量出来的假象。**教训：判"资产缺失"死刑前必须先核实运行时的真实查找路径。**
**战略修正（Tech Leader 点破）**：N2 点火 + Tarkin 房间 = 8-06 已验收组合（"非常完美"那次就是 Tarkin 房间跑的）；8-07 失败的只是自建原生 bundle。早间的整线摘除是误伤，白天零售直连重打了 N2 打赢的仗（台词三闸等）。
**恢复方法**：`Client\bin\Debug`/`Server\bin\Debug` 的 DLL 停在 8-08（今天只重编译过 Release）→ `ilspycmd -p -r <引用目录>` 反编译（**必须带 -r 指向 Managed/BepInEx core/SPT 目录，否则输出全是 op_Implicit/ref 残渣**）→ 摘 NativeSceneStrip 调用与 IsNativeRoom/vendors_scripts_native 分支后原样归位。仅两处人工修补：DialogueLoader 解构语法残渣 `(ref item2)`、一处 nullable 注解。
**当前基线**：N2 全套（含台词三闸 narrate 门禁版）+ Tarkin 房间 + .dlg 优先分流恢复；零售直连三件套退役（其 SimStatus 思路记 #49，Skier 剧本状态若在 narrate 下仍显单薄可移植进 NarrateQuestGhostGuard）。回归验收按 N2 清单：访问按钮→拜访→台词/选项→交易/任务→返回退出→清场。

## #52 复活首测：一次拜访全通；二次拜访音频雷 + 任务话题老遗留（2026-08-13 深夜）
**首测结果**：第一次拜访全链路 ✅（N1a 数据在位 mainDialog present=True / FiR 护栏触发 / 场景相机接管 / Vendors_Prapor 台词种子 / F9 退出链全绿+清场日志齐）。`Vendors_Scripts couldn't be loaded` 警告属预期（该公共场景只有自建 bundle 才有，8-06 验收时同样带着它跑）。
**两个问题**：
1. **任务话题点击卡台词 = #41 已知遗留**（GenerateSelectQuestLines 走 Context.QuestController，narrate 语境没接 SPT 任务书），非恢复回归，需专项：侦察 narrate controller 的 QuestController/QuestsData 装配点。
2. **二次拜访 `BetterAudio.ToggleNarrate` NRE** → HandleError ERROR 屏。Show 的复用/新建分支都会调它做音频淡变（菜单音乐↔拜访环境）；F9 清场后其内部某链（SettingsManager.Sound.Settings 链或 FadeMixerVolume 内联的 Master）为 null。修：**NarrateAudioGuard**（finalizer 吞异常记警告）——音频淡变是装饰性动作，吞掉只损失渐变效果，流程保命。若吞掉后二次拜访又露出下一颗雷（如复用分支 Move），按 N2 老打法逐颗排。

## #53 复活二测：多次拜访全通；Skier 修法移植 + 任务选项探针（2026-08-13 深夜）
**二测结果**：NarrateAudioGuard 生效（每次 `audio toggle skipped`, 不再 ERROR 屏），一场连跑 6 次拜访（Prapor×3/Therapist/Skier/Fence, F9 与返回两种退出混测）全通；Skier 的 RegisterScene 解锁也工作（`scene preset registered: Skier -> Vendors_Skier`）。BetterAudio 深挖根因（哪个链为 null）挂账不追——护栏语义足够。
**剩余两靶**：
1. Skier 对话残端（个人任务 68f0f5516871d33422038cd2 库外, Locked 模拟下问候池全灭落到状态1死路）→ **SimStatus 表移植进 NarrateQuestGhostGuard**（该任务模拟 Started; sim2.ps1 验证过问候回正+交易/任务选项可达; 每任务恒一状态不会抢跑）。
2. 任务选项不工作（#41 遗留）：静态排查到底——菜单语境 menuOperation._questController/_dialogController 在 Free 档流程里正经构造（MainMenuShowOperation L625/L646）, GenerateSelectQuestLines 走 Context.QuestController.NewQuests(traderId)（按 AvailableForStart 过滤）, 理论链路无断点 → 上**实弹探针**: NarrateDialogBuildProbe（DynamicTraderDialog 构造器 finalizer, 对话树构建异常原样抛但先记全栈）+ NarrateActionProbe（ExecuteDialogAction postfix 记每次动作类型）。下轮日志定谳。

## #54 任务对话定谳 + Skier 人设定版（2026-08-13 深夜）
**任务对话真凶（探针实锤）**：点"任务"执行的是 `DialogQuestsScreenAction quit=True`——与交易同款"退出对话+等接棒者开屏"设计。交易能通是因为菜单流程自带 DialogTradingScreenAction 订阅者（CG_ShowTraderDialogScreen 开 TraderScreenController），**QuestsScreenAction 在 0.16.9.5 里没有任何订阅者** → 点了没下文。#41 当年猜的 QuestController 断线方向是错的（QuestController 链路其实完好）。
**修法 NarrateTabs**：NarrateEntry.Visit 时对 menuOperation.DialogController 订阅 OnActionFinished，QuestsScreenAction → 官方同款开 TraderScreenController + MenuUI.TraderScreensGroup.SetMode(Tasks)。交易不碰（原生订阅者健在）。
**Skier 人设定版**：sim2 全状态扫描——Locked/AvailableForFinish/Success 三种模拟全死路（问候落 line13 残端、交易/任务不可达），**唯一能走通的是 Started**。"开场先聊任务、聊完才出菜单"是 1.0 数据只写了这一条路线的设计本身，接受现状，SimStatus=Started 维持。

## #55 任务屏接棒者收尾：必须复用菜单现成控制器（2026-08-13 深夜）
**三测结果**：NarrateTabs 生效（tasks screen opened, 从任务屏还能回到对话继续聊）; Skier Started 剧本正常跑（任务对话流+交易可达）。新症状"退出对话卡转圈"——日志里退出链内部全部走完（controller.Hide/game.Hide/清场全绿）, 卡的是回主菜单的视觉过渡。
**嫌疑与修法**：初版 NarrateTabs 照 TabRouter 姿势**新造** OfflineHealthController/AchievementsControllerClientBackend——没走菜单的 Init()/Run() 生命周期, 屏幕关闭过渡疑似挂起。原生交易订阅者(CG_ShowTraderDialogScreen)用的是 MainMenuShowOperation 的**现成实例**（public 字段 _healthController/achievementsController, HealthController/QuestController/InventoryController 属性）→ 改为完全照抄原生, 一个野生对象都不造。
**教训**: 给原生屏幕当接棒者时, 控制器实例要用宿主流程已初始化的那套, 别自己 new——TabRouter 当年 new 是因为 .dlg 语境拿不到 menuOperation, narrate 语境拿得到就该用现成的。

## #56 Skier 返回转圈定谳：悬空 SwitchDialog 目标（2026-08-14 凌晨）
**现场**：Skier 交易一圈回到对话后点"返回"无反应转圈——日志全程无 DialogQuitAction，最后一个动作是 SwitchDialogAction。
**定谳链**：①Skier 入口元素的退出链本身健全（QuitIcon 选项设 var=13/4，NPC QuitAction 行纯变量门）②但入口里有 SwitchDialog 跳转目标 **68c2afd6cfe49a2da650f405 在所有可得数据里都不存在**（tarkin 文件/SPT 原生表/SPT locale/OT database 全无；1.0 客户端 TestDialogues.json 里也只是同样的悬空引用——两文件 92 元素集合完全一致，"TestDialogues"顾名思义是样本子集，完整对话库在 1.0 服务端）③原生 `method_0` 对缺失模板直接 `DialogStorage.GetTemplate` 炸掉，异常被点击管线静默吞掉 → 对话死锁转圈。
**修法 NarrateSwitchGuard**：method_0 Prefix——TryGetTemplate 查无此模板时记警告并改道回商人 MainDialog 入口（体验=对话回开场白），主对话也没有才放行原样（保底原行为）。全局打安全：.dlg 模板全部注册在 DialogStorage，悬空引用只存在于 BSG 零售数据。
**#56 补全（同日）**：改道生效但下半个问题现形——改道时 Dialogue 域变量停在中途值, 主对话在该状态下全行灭 → 红色兜底 Back（其动作又是 SwitchDialog 主对话）→ 原地打转。修：改道同时 `SetVariableValue(template.MainVariable, 0)`（Dialog 域写 dictionary_1, GetVariableValue 首查即中）——状态机归零, 落地即开场白。教训: **改道对话必须连状态一起归零, 只换模板等于把人传送进死房间**。
**#56 终章（数据层定案）**：变量归零后仍死循环——因为悬空跳转就挂在"返回"的 NPC 告别行上（告别行动作=SwitchDialog(缺失告别子对话)+SetVariable），改道回开场白等于把"再见"变成"从头再聊"。全库审计：悬空 SwitchDialog 仅 3 处（Prapor 2 / Skier 1），全部在 NPC DialogBubble 行=告别链，语义实锤"退出"。定案改在数据层：DialogueSanitizer.FixDanglingSwitches——服务端装载时把悬空 SwitchDialog 动作原地改写成 QuitAction（删 dialogId/splitterNodeId），DialogueLoader 改两遍扫描（先收全部元素 Id 并集再修复）。客户端 NarrateSwitchGuard 留作保底。生效需重启服务端+客户端重新登录（模板是登录时从服务端拉的）。

## #57 与 4.0.13 全面交叉对比定谳：黑材质 + 大妈/枪匠缺选项（2026-08-14）
**方法**：四路并行深查（Tarkin spt-tradermod 源码在 E:\项目\VisitAPI Framework\NarrateSystem\spt-tradermod-main、Rework 场景管线、旧版对话路线、dialogue.json 数据仿真审计）。
**为什么 4.0.13 没这些问题**：①旧版对话选项 100% 来自 .dlg 脚本（DialogRunner 自绘行、OptionRowPatch 短路原生点击），从不读零售 dialogue.json——"缺选项"在结构上不可能发生；交易/任务是 openTrade/openTasks 动词反射切 TraderScreensGroup 页签。②3D 场景当年是 Tarkin 本人 mod 渲染的（不是 narrate），关键三招：Shader.Find 现场找原生 shader、PrismEffects.useExposure=true、SetActiveScene。
**黑材质定谳**：我们的 shader 快照建于任何 bundle 加载前（Resources.FindObjectsOfTypeAll 只见已加载的），build 里有但未加载的 shader（Unlit/Texture=全景半球背景、Custom/Billboard_FogSheet_Simple=雾片）查不到→保留 rip 包里变体残缺的副本→渲成黑块，每场景雾片位置不同所以"各有各的黑"；且未开曝光自适应，暗场景整体压死。**修**：SceneShaders.Fix 快照未命中时补 Shader.Find（找到即重绑+回填快照，Tarkin 同款）；SceneCamera 补 PrismEffects.useExposure=true + CC_FastVignette darkness=44；SceneLighting.Uncover 拜访时补 EnvironmentUI.EnableOverlay(false)。
**大妈/枪匠缺选项定谳**：根本不是任务门（两家入口元素零 QuestStatus 条件）！是"孤儿相识变量"——Therapist 交易/任务选项要 68c81cf8d242d0b184959530==1、Mechanic 要 68c41b22c5e8a18a3692d395==1，而全库 11.9MB 无任何 SetVariable 写它们（1.0 里由服务端 profile 同步）。SeedVariables 原本把孤儿变量全种 0（=没种，未设变量本来读 0）。**修**：RetailDialogs.KnownSeeds 表把这两个变量种 1（副作用=互斥的"初见寒暄"行消失，语义正确：SPT 玩家早就相识）。离线仿真验证：种 1 后两家交易+任务全通、两档声望(0/0.35)全通。Ragman 数据原生即通；Peacekeeper 全库零数据（无从修）。任务屏行还叠 HasNewQuests 门：商人没新任务时任务选项不出现属正常语义。

## #58 Skier 开场白反转 + 背景半球终修（2026-08-14）
**Skier 反转**：官方开场白"喂你！杵那儿干嘛？…"就在数据里（入口元素 line13），门=(主变量==0 且 68c2bf4f8c7b5c191d5ec0df==0)，动作=SwitchDialog(68c2afd6 初见剧情子对话, 缺失)——**#56 判为"告别子对话"是错的, 它是初见剧情**。5ec0df 是第三个"相识"孤儿变量: 种 1 → line13 永不触发(初见线绕过) → 开场白=line11(回头客问候) → 交易可达, 全程不需要任何任务状态模拟。仿真实锤后: KnownSeeds 加 5ec0df=1, SimStatus 撤销 Skier 的 Started 覆写(它正是"任务阶段台词顶掉开场白"的元凶)。Prapor 两处悬空=幽灵任务门 Status=[1], 默认 Locked 天然休眠。服务端 FixDanglingSwitches(悬空→Quit)与 NarrateSwitchGuard 降级为双保底(相识=1 后不应再触发)。
**背景半球终修**：#57 的 Shader.Find 补丁救不了它——bundle 已加载时 Find 返回的就是 rip 包里那个残缺副本(同名), 重绑等于没绑。定案=强制换装表 Swaps: Unlit/Texture 与 Custom/Billboard_FogSheet_Simple 一律换成原生 "Sprites/Default"(退 "UI/Default")——无光照、Cull Off、吃 _MainTex, 贴图/平铺原样保留。换装时打日志含 mainTex 名(若 <null> 则是贴图本身没 rip 出来, 另案处理)。
**#58 补正（大糊球事故）**：Sprites/Default 方案翻车——它是透明队列+不写深度, 景深后处理拿不到半球深度按无限远糊成大光斑。两个新实锤：①swap 日志证明全景贴图完好(Render_Vendors_*_180_equi 都在)②枪匠窗户蓝光(Unlit/Transparent Fogless, Tarkin 自定义 shader 的 bundle 内嵌副本)一直正常 → **bundle 内嵌自定义 shader 全都能用, 唯一死的是 Unity 从不打进 bundle 的内置 Unlit/Texture(本 build 又没有)**。终案 ToEmissive: 半球材质换原生 Standard, 贴图同时喂反照率+自发光(_EMISSION 变体在=无光照照片; 被裁剪=环境光兜底照样可见), 不透明写深度景深不糊。雾片 Billboard_FogSheet 从换装表撤销(错杀, 其 bundle 副本本可用)。
**#58 验收（2026-08-14）**：ToEmissive 终案实测通过——7 商人全逛（含 Fence/Jaeger/二访），背景全景照片全部正常渲染，对话三件套齐，逐家干净退出，日志零异常。自复活以来的五颗雷（悬空跳转/相识变量×3/内置 shader 缺失/曝光压黑/任务人设插队）全部拔除，Narrate+Tarkin 路线全链跑通。遗留观察: BetterAudio toggle NRE（护栏兜底）、Vendors_Scripts 良性警告、Jaeger 的 Particles/VolumetricSmoke 未在快照（bundle 副本自用, 画面正常）。

## #59 观感定格（2026-08-14, Tech Leader 拍板）
**决定**：不再追正式版渲染观感——正式版完整管线（后处理配置/光照底座）在 1.0 IL2CPP GameAssembly 里, 未反编译, 场景包侧永远凑不齐, 属引擎级不可复刻。回滚 #57 的三处"仿正式版"改动（PrismEffects.useExposure / CC_FastVignette / EnableOverlay), 相机恢复 N2 复活原样。**保留**: ToEmissive 背景半球修复（不保则背景全黑, Skier 港口效果 Tech Leader 认可）。**新增一刀**: Particles/VolumetricSmoke（bundle 残缺副本, 渲成黑块, 原生快照与 Shader.Find 均无）的渲染器直接禁用——Jaeger 前景黑块即它, 装饰性烟雾, 熄灭无害。已知接受项: 人物光照与正式版有差距（引擎级）、Mechanic 背景照片偏暗（素材原貌）。
**#59 附**：访问按钮对照正式版截图缩小 0.8 倍（187×32→150×26, 图标/文字等比, 字号 18→14）——用商人卡片宽度归一测得旧尺寸偏大 20~25%。

## #60 源码发布打磨（2026-08-14 晚）
**方法**：五路审查 workflow（四区域代码 + 发布阻断审计）出 76 条 → 死代码对抗复核（4 判 3 实 1 驳）→ 两工人agent + 主线并行修缮 → 双端 0 警 0 错。
**代码面**：①调试探针清场——删 ProbeScene/ProbeStorage/NarrateActionProbe/NarrateCameraProbe, NarrateDialogBuildProbe 改名 NarrateDialogBuildGuard 保留（点击管线吞异常, 它是唯一现场可见性）②九处"正常流程当警告刷"的日志归位 LogDebug（退出链踪迹/相机接管/lighting armed 等）③反编译残名清洗约 60 个（NarrateEntry/SceneLoader/SceneCamera/TalkButton/TriggerManager/VisitTrigger/服务端全家）④DialogDebug.Loader 迁 DialogFiles（9 处引用, 免得读者把加载链当调试设施）⑤死代码：QuestGates 未用参数、DialogueLoader 裸块、DialogueReplay 无用局部——"两轮 Fix 重复"复核驳回后改为删 Run 侧+ReportMisses 移到 Apply 后⑥承重注释补齐（快照先于 bundle/退出先断 Camera/method_5 白名单/OnceGate 借道 QuestStatus/背景尾缀 DSL/Jaeger=12）⑦小修：Run 的 IsValid 返回值改判定+Abort、TriggerMenu 非 merge 每帧重建加守卫、VisitArt 循环读满、Plugin 版本单一真源、Loc 文化委托空安全。
**"仅标注"未动清单（维护者定夺）**：NarrateHideGuard faulted 路径不执行 Postfix 清场/_unsubscriber 替换旧实例无人 Dispose；HandoverService 等 OnDialogChanged 重投递可能叠订阅（需实测 method_0 是否缓存实例）；QuestGate 缺失任务=0 与 Snapshot 的 -1 语义不一致；DialogueLoader 坏元素不从 knownIds 回收；StandingRouter 不校验 TraderId。
**发布面**：.gitignore（_decomp/OT/release/bin/obj/零售 dialogue.json/scenes/Memory.MD 全隔离）、LICENSE=MIT+NOTICE（场景包 bmpq MIT 署名）、README（双语, 构建/安装/免责）、csproj 参数化（EftDir/SptDir/DlgSrc 可 -p: 覆写, Deploy 加 Exists 条件外部机器静默跳过）。
**图片路由坑（自 QuestLoader 注释迁入）**：SPT 只自动伺服 SPT_Data/images, 模组图片必须 ImageRouter.AddRoute 自注册; 路由键不带扩展名（SPT 查表前把扩展名切掉）且按第一个点截断, 文件名含多余点号的图永远挂不上。

## #61 战局内自动接任务（trigger `auto` + `accept`，2026-08-15）

**四条反编译定谳（全部实证，不是推测）**：
1. **战局内的任务事务全是纯本地的**：`QuestControllerClientLocalGame.AcceptQuest` 的整个实现 = `SetConditionalStatus(quest, Started)` + 返回成功，**`runNetworkTransaction` 参数被完全无视**；FinishQuest / HandoverItem 同理（ItemManipulator 本地结算）。推论：战局中接/交/完成任务**服务端全程不知情**，靠战局结束 profile 回传才落盘；服务端 `QuestController.AcceptQuest` 的三个副作用（起始邮件 / 起始奖励 / 解锁后续任务）**一件都不会发生**，完成邮件（`SendLocalisedNpcMessageToPlayer`）同样发不出来。⚠️ 玩家战局内死亡时状态是否回滚未实测。
2. **通知链白送，但文字是空的**：`SetConditionalStatus` → `TransitionStatus` → `SetStatus(notify: **true** 硬编码)` → `OnConditionalStatusChangedEvent` → `TryNotifyConditionalStatusChanged`。前提是任务模板 `canShowNotificationsInGame: true`。但 **Started 与 Success 两个分支只设 soundType 不设 text**（text 停在 `string.Empty`——非 null，所以照样 DisplayNotification 一条空文字通知）→ 要文字得自己补一条 `NotificationQuest`，**别设 SoundType**，否则和原生那声撞成双响。
3. **商人锁定不挡任务下发**：服务端 `GetClientQuests` 的过滤判的是 `TradersInfo.ContainsKey(traderId)`，与 `Unlocked` 无关；且 **`AvailableForStart` 为空数组的任务被直接标成 AvailableForStart**。两条合起来 = "新档就静静躺在任务书里、玩家因为看不到商人界面而毫不知情"是免费的，剧情任务的隐藏起点不用另做机关。
4. `QuestTemplate.Name` 已经是 `NameLocaleKey.Localized()`，通知里直接用就自动跟随游戏语言。
5. **状态转换有合法性闸**：`TransitionStatus` 先查 `CurrentStatusTransitions.Contains(status)`，不合法只打一行 LogError 什么都不做。所以 auto 接取前任务必须已是 AvailableForStart（靠第 3 条免费拿到）。

**实装**：`trigger:` 新增两个参数——`auto`（进范围直接点火，不进交互菜单；TriggerManager 同步把 `RequireLook` 关掉，否则玩家得盯着触发点看）与 `accept <questId>`（直接接任务，不开对话）。
**踩过的坑**：auto 点用 `_fired` 保证一局一次，**但 quest 取不到时必须把 `_fired` 退回 false**——战局刚载入时 `Camera.main` 已经在了而 `QuestController` 还没就绪，一旦在那一帧点火就会把触发点永久锁死；配合 Auto 分支里补的 `_cooldown` 检查，退化成 1.5 秒重试一次，既不锁死也不刷屏。

**#61 补 A：raid 触发点"必须低头"的根因（2026-08-15 实测暴露）**——`F11` 打印的是 `player.Transform.position`（**玩家脚底**），而 `ShouldShow` 的视角锥用 `Camera.main.transform.position`（**眼睛，高约 1.6m**）。作者照抄 F11 坐标 → 触发点恒在视线下方 1.6m，近距离时视线俯角远超视角锥容差（水平 2m 处：俯角 38.7° vs 容差 `atan2(1.2, 2.56)`=25.1°；站正上方时逼近 90°），**逼玩家低头才出提示**。raid 的 `RequireLook` 又是恒 true 没有开关（`(!hideout || Free)`）→ 所有 raid 触发点通病，老的 Sandbox_high 拜访点同样中招。
**修法**：抽出 `LookPasses`，朝向**只比水平分量**（`flat.y=0` / `forward.y=0`），高低差交给 `dist` 距离门槛去挡；水平距离 <0.2m（站正上方）直接放行避免零向量取角。**遗留**：距离判断仍是三维的，那 1.6m 高度差照样吃预算——`dist 3` 实际可用水平范围只有约 2.5m，作者嫌近就把 dist 调大（`dist 4` ≈ 水平 3.7m）。

**#61 补 B：`{playerName}` 大小写敏感**——`DialogTemplateBuilder` 的替换是 `text.Replace("{playerName}", nick).Replace("{player}", nick)`，**只认这两种写法**。作者写成 `{playername}`（全小写）不会报错也不会被替换，玩家直接在台词里看到字面的 `{playername}`。首次填 SORA 开场剧情时就踩了。

## #62 旁白可以排在台词后面（`NpcAt`，2026-08-15）

**这不是"没做入口"，是模型压根表达不了**：一屏在文件里本来就是**按行序**播的（`>` 旁白行和商人那句台词混着写），
但 `DialogNode` 把它拆成了 `Narration[]` 和 `NpcText` **两个字段** —— 台词落在第几行这个信息，
**在解析那一刻就丢了**。后果：作者把 `>` 写在台词下面，读进来会被排到台词前面，
用编辑器存一次盘就**永久换位置**，全程不报错、不警告，进游戏才发现"补刀那句先播了"。

**实装**：`DialogNode.NpcAt` = 台词排在第几条旁白之后（`-1` = 没记过，按老规矩"在所有旁白之后"，
老文件零影响）；钳好的值走 `NpcSlot` 属性，**解析 / 写手 / 插件三边都按它切**，免得各算各的。
- `DialogParser`：读到台词行时 `NpcAt = Narration.Count`
- `DialogWriter`：旁白 `[0, NpcSlot)` → 台词 → 旁白 `[NpcSlot, 末尾)`
- `DialogTemplateBuilder`：播放链从写死的 `#nar0→…→#npc→#opt` 改成按 `slots` 串
  （`-1` 代表台词那一拍）。**对话 id 仍按 Narration 的下标起名**，作者换顺序不会让 id 满天飞
  —— `once` 记号是按行 id 存进 profile 的，id 一变旧存档的"已点过"就全废了。
- 节点级的 `bg` / `bgm` 原来硬挂在 `#nar0`（没旁白才挂 `#npc`），现在挂**真正的第一拍**；
  `OptionMap.Entry` 判入口同理（`NpcSlot == 0` 时第一拍就是 `#npc`）。

**版本口径**：这一批改动**两仓库同为 1.0.1**（插件 `Plugin.cs` / `Client.csproj` / `Server/ModMetadata.cs`
三处一起走，编辑器 `VisitAPI.Server.csproj` 一处）。号对齐不是巧合——`NpcAt` 这条契约要求两边同版，
号一样才看得出"这台机器上的编辑器和插件配不配套"。

**跨仓契约**：`VisitAPI.Dlg` 是**源码链进插件**的（不是引用 dll），编辑器和插件共用同一份。
所以这条改动**必须两边一起升**——旧插件（1.0.0）读到"台词后面还有旁白"的文件照样把旁白排到前面播，
表现成"编辑器里排的顺序和游戏里播的不一样"，两边看着都对、就是不一致，这种最难查。
编辑器侧 1.0.1 起生效（拍条按真实顺序画，◀ ▶ 挪拍、✕ 删拍）。

**#62 补：编辑器一度会把 `auto` / `accept` 抹掉**——#61 给 `trigger:` 加的这两个参数只有 C# 侧认，
编辑器的 `trigParse`/`trigLine` 不认，作者在触发点表单里**动任何一个字段**（哪怕只改 dist），
重拼那一行时这两样整段消失，不报错不警告。教训：`.dlg` 的每个字段都有**四个读写点**
（C# `TriggerParser`/`DialogWriter` ＋ JS `trigParse`/`trigLine`），加字段先把这四处列出来再动手。

## #63 进图计时触发点（`trigger: ... enter <秒>`，2026-08-22）

**需求**：进指定地图 N 秒后自动接取任务，不需要玩家走到某个坐标。

**做法**：`DialogTrigger` 加 `Enter`（-1 = 普通坐标触发点）；`TriggerParser` 里坐标从必填改成「只有 enter 型能省」；
`TriggerManager.Spawn` 把 `Enter >= 0` 一律按 `auto` 处理（不弹提示、不判朝向）；`VisitTrigger.ShouldShow` 加计时分支。

**坑：起表点不能用 GameWorld 生成那一刻。** 触发点是 `TriggerManager.Tick` 在 `GameWorld` 就绪时生成的，
那会儿还在读条，几秒钟直接烧完 —— 落地即触发，看不到提示。改成等 `GamePlayerOwner.MyPlayer` 非空再起表
（`VisitTrigger.EnterDue`），那才是「玩家真正可控」的时刻。

**回写**：`DialogWriter.Trigger` 对 enter 型不吐坐标，否则编辑器存一次盘就把它写成 `(0,0,0)` 的坐标点。

## #64 任务横幅：借原生通知底盘（2026-08-22 ~ 08-23）

**结论先行**：1.0 那条剧情横幅的类 `MainQuestNotificationView` / `NotificationMainQuest` / `MainQuest*` 在 **SPT 4.1.3
活体 DLL 里一个都没有**（4.1.3 只是服务端更新，客户端仍 EFT 0.16.9.40743）。但**底盘是全的**：
`BaseNotificationView`（立起/躺下 = DOTween 拉 `LayoutElement.preferredHeight` 0.3s + Animator 三参数，与 1.0 dump 逐字一致）、
`AchievementNotificationView`、`NotifierView`（工厂 + 池 + 队列 + 音效）。所以不自绘 UI，直接借。

**做法**：`VisitBanner : NotificationWithText` override `CreateView` → `viewFactory.CreateDefaultView(this)` 拿原生默认横幅的
克隆体，换底图（九宫格）即可；`QuestNotify` 用 Harmony Postfix 挂 `QuestControllerClient.TryNotifyConditionalStatusChanged`，
只认 .dlg 里出现过的任务 id。任务 JSON 的 `canShowNotificationsInGame` 设 `false` 让原生对 VisitAPI 的任务闭嘴
（那道开关是**逐任务**的，见该方法第一行 if）——SPT/EFT 自己的任务提醒零影响。

**坑 1：默认横幅是池化复用的。** `NotifierView` 的 `POOL_SIZE = 4`，`RemoveNotificationView` 里 `ReturnToPool == true` 就回池。
`Init` 每次会重设 `_icon.sprite` 和 `_background.color`，**唯独 `_background.sprite` 和 `Image.type` 不重设** ——
不在 `OnHideComplete` 里还原，我们的底图就会串到 SPT 自己的通知上去。

**坑 2：内容位移那条路是死胡同。** 想把图标+文字让开左边的尖头，试过 `_container` 上布局组的 `padding.left`
（日志确认 `group=True`、padding 确实改了）、`offsetMin`、`anchoredPosition`，**加到 300px 都纹丝不动**。
最后 Tech Leader 一句话解决：**底图整张水平翻转** —— 尖头挪到右端，左缘变干净直边，「内容让位」和「左边一条黑线」
两个问题一起消失，位移代码和配置项全删。九宫格边跟着换成 `(4, 8, 33, 8)`。

**坑 3：任务名带尾随空白会把横幅撑成三行。** 文案是「名字 + 换行 + 状态」拼的，locale 里写成 `"偶遇\n"`
就变成「名字 / 空行 / 状态」、图标按三行居中。`QuestNotify` 对任务名统一 `Trim()`（框架级保护，作者写脏了也不塌）。

**美术**：底图从 1.10.1 实机截图右下角切的（条高 61px、竖条纹周期 11px、左端尖头做成 alpha 遮罩 + 边缘抗锯齿），
最终按 Tech Leader 拍板压成**纯黑**、半透明 alpha 200/255（与 `_defaultBackgroundColor` 一致）。
**别给整条底染色**：正式服那条底色本来就是 `(24,24,24)`，状态是**靠文字颜色**区分的
（任务名 `#FFF4D2`、状态行 `#B6E5F3`，从截图上量的）。

## #65 旁白改走原生字幕框（2026-08-23）

**原生字幕框 = `EFT.UI.SubtitlesView`**（`_decomp/.../EFT/UI/SubtitlesView.cs`），唯一实例挂在
`TraderDialogScreen._subtitlesView`；4.1.3 里它和 `_dialogWindow` **都是 public**（4.0.13 当年要反射）。
`TraderDialogScreen.cs:93` 开屏就 `Show(ESubtitlesSource.Common)` 把它武装好了。

**为什么之前是商人对话框**：4.1 重做把 `>` 旁白编译成了一对合成对话（NPC 侧 `#nar<n>` + 玩家侧 `#nc<n>`「继续…」），
进了对话模板就必然被 `TraderDialogWindow.Redraw` 画进商人台词框 `_traderText`。

**做法（对话模板零改动）**：`DialogTemplateBuilder.NarrationByDialog[#nar<n> 和 #nc<n>] = 旁白文本`；
`NarrationView` 撞到旁白拍就藏 `_dialogWindow`、把文字写进 `_subtitlesView._textField`，
点击 / 空格调 `ClientDialogController.ExecuteLineByIndex(0)` 推进（就是那条被藏起来的「继续…」行）。
背景/BGM/语音/once/任务门全部原样有效 —— 它们挂在 `#nar` 上，`DialogBackground.OnDialog` 查不到 `#nc` 就 return。

**不走 `SubtitlesEvent` 事件路**：`SubtitleParams.Start/End` 是基于 `AudioSettings.dspTime` 的秒数、到点自动清行，
跟「点一下走一步」冲突，而且容易和 BSG 自己的 `Common` 通道打架。

**坑：`EventDialogWindow` 重写了 `Redraw` 且不调 base。** 它走 `RedrawAsync` 异步显示窗口，
所以**挂在 `TraderDialogWindow.Redraw` 上的 Harmony 补丁对它完全不触发**。只能每帧兜底，而且必须放
`LateUpdate`（排在 `Update` 和异步续体后面）—— 放 `Update` 会慢一帧，观感就是窗口闪 1~3 帧再变字幕。

**坑：`#nar` 也要登记。** 引擎过 `#nar<n>` 那一下是一次异步网络往返，只登记 `#nc<n>` 的话那段时间字幕会掉、
商人窗会闪出来。

**清场**：对话屏是池化的（关闭只是隐藏），`Restore()` 不把窗口显示回去，下次开原版商人对话就是一片空白。

## #66 分支记号 `set:` / `ifvar:`（2026-08-27）

**需求**：`<first_contact>` 有 A/B 两条线，`<encounter>` 要按玩家当初选的线出不同选项。选项门控原本只认任务状态，
A/B 走完任务状态一样，游戏里**没有任何东西记着玩家选了哪边**。

**语法**：选项指令 `set: 名字=整数`（记一笔）和 `ifvar: 名字=整数`（记号等于该值才显示）。名字随作者起，
`set:` 和 `ifvar:` 写一样即可；值只能整数（引擎变量本子是 `Dictionary<MongoID,int>`）；没记过 = 0。
写手顺序：… → if/ifnot → set/ifvar → standing → once/always。

**底盘 = 引擎自带的 Profile 变量链**（#32 侦察过的那套，正式服 when/once/first 的原生形态）：
- 记：`QuestGates.Actions` 给行挂原生 `DialogSetVariableAction(SaveStateData(id, value, ESaveStateType.Profile))`，
  引擎执行动作时写 `profile_0.ProfileVariables`（本地立即生效，**先于** SwitchDialog，下一屏门控读得到）。
- 门：`QuestGates.Trigger` 挂原生 `VariableValueCondition(id, value)`，读级联 Dialog→Session→Profile；
  `ProfileVariablesStorage.GetVariableValue` 缺项返回 0，不抛。
- 名字 → id：`VariableService.Id`——24 位十六进制直接用，否则 `DialogTemplateBuilder.Id("visitapi.var", 名字)`
  MD5 取前 24 位（同名永远同 id，跨商人通用）。

**持久化（两条腿）**：
- 战局内：SPT `LocationLifecycleService` 结算时 `pmcData.Variables = profile.Variables`（客户端提交档整块覆盖），
  客户端本地 ProfileVariables 已写 → 随结算落盘。
- 菜单/藏身处：客户端**不会**上传 Variables（#32：SaveDialogueState 不带值、服务端回放只认零售数据里的行），
  所以 `VariableService.Watch` 订阅 `OnExecuteLine`，命中记号行就 `POST /visitapi/variable/set`，
  服务端 `VariableRouter` 写 `pmc.Variables`（`??=` 护老档 null）。登录时随 profile/list 下发回 ProfileVariables 闭环。
  战局内也会发这一笔——被结算覆盖也是同值，无损。

**⚠️ 待实测**：战局内 `set:` 后阵亡/断线两种结束方式是否都走到结算回拷。理论上都经 LocationLifecycleService。

**编辑器同步（1.0.5）**：JS 解析/芯片/⋮ 菜单「分支记号」两格（回车生效，空=清掉）/说明页/兼容检查
`DIRECTIVE` 正则/`DlgJson.Opt.Setvar/Ifvar`；torture.dlg + probe-writer 加了回环断言。
顺手修：`enter N` 型触发点以前 `TRIG_RE` 认不出（一动表单就重拼成坐标型），触发器「任务门控」从手打文本改成任务选择器＋状态芯片。

## #67 任务"二选一"完成 + 达成即解锁商人（2026-08-27，`sora_scout`）

需求：「侦察」= 杀 Killa **或** 在 KIBA 附近杀 10 个 Scav，任一达成就可提交；可提交那一刻 SORA 自动解锁。

**引擎真相（反编译）**
- 顶层完成条件原生只有"全部达成"：`Condition.IsNecessary = _isNecessary || FirstLevel`，顶层条件永远必要；`isNecessary:false` 只对带 `parentId` 的子条件生效，而子条件只是父条件的附属小步骤。原版 24 个带 `parentId` 的任务翻遍，没有"或"。
- 判"可提交"的唯一函数：`Quest.CheckForStatusChange` → `Template.Conditions[status].TestAll(this)`（Fail 用 `TestAny`）。`TestAll` = `EarlyFinisherConditions.All(checker.Test())`。
- 击杀事件路径：`ConditionalController.CheckKillConditionCounter` → `ConditionalBook.TestConditions(Kills / Location / InZone / …)`；`InZone` 的 `zoneIds` 用地图上的任务区域，KIBA 门口现成的是灯塔任务「Provocation」的 `quest_zone_keeper6_kiba_kill`（Interchange）。
- 战局内计数/状态全是本地的，战局结束随 profile 回传；服务端交任务不复核条件（#61）。
- 商人解锁：服务端 `TraderHelper.SetTraderUnlockedState` 改 profile；实时点亮走 ws `WsProfileChangeEvent{EventType=UnlockTrader, Changes{traderId:1}}`，客户端 `NotificationUnlockTrader.ApplySingleChange` → `TraderInfo.SetUnlocked(true)` → `OnAvailabilityChanged`。原版发 TraderUnlock 奖励就是这条通道。

**做法（开关写在任务 JSON 里）**：`"visitapi": { "anyOf": true, "unlockTraderOnReady": true }`。SPT `Quest` 模型有 `[JsonExtensionData]`，自定义字段原样进库（`ExtensionData["visitapi"]` 是 `JsonElement`）。
- 服务端 `QuestReadyRouter`：`/visitapi/quest/flags`（返回带开关的任务表）+ `/visitapi/quest/ready {questId}`（按 `unlockTraderOnReady` 解锁该任务的 `traderId` 并推 UnlockTrader 通知）。
- 客户端 `AnyOfQuest`：启动时拉一次 flags；Harmony **前置**补丁 `ConditionCollection.TestAll(IConditional)`——是 anyOf 任务、且这个集合正是它的 `AvailableForFinish` 集合时，改成 `Any`。不碰计数器、不碰 Profile；任务书里另一条会一直显示未完成，属正常。
- 客户端 `QuestNotify`：本来就在监听 VisitAPI 任务状态，`AvailableForFinish` 时多发一个 `ready` 请求。

**坑**
- 服务端 `Quest.TraderId` 是非空 `MongoId`（不是 `MongoId?`），别写 `.HasValue`。
- `ConditionCollection.TestAll` 有两个重载，`[HarmonyPatch]` 要带 `typeof(IConditional)` 指明一参那个。
- 任务 JSON 只在游戏目录（编辑器写的），工作区 git 没有 → 这次同步回 `Server/db/quests/sora_scout.json`，locale 也按"游戏目录为准"合并回来，英文文案是 Claude 补的。
- 第二条原来没有 `InZone`，全图 Scav 都算；`index` 两条都是 0；补齐 `type/isNecessary/...` 字段照原版格式。
- **BinaryDimensionStore 的 `base.json` 现在 `unlockedByDefault: true`**（08-26 临时），不改回 `false` 看不到"自动解锁"效果。→ 08-27 已改回 false。

**实测（2026-08-27，Tech Leader）**：任务达成、可提交、解锁都通过。但任务详情里两条目标并列显示，玩家会以为都要做——原版**没有"或"的显示**（`QuestObjectivesView` 只有父/子缩进一种分组，全部语言包也没有"或"文案）；原版"二选一"靠的是 26 个**互斥任务**（`Fail` 条件里放 `Quest` 目标=对方、状态=Success）。Tech Leader 拍板：任务不拆，先补对话多接/多交的能力（#68）。

## #70 章节系统 P1：数据模型 + 填充逻辑（2026-08-28）

**#69 实测**：「剧情」页签出现、1.1 面板壳实例化成功（页签看不见的真凶 = 自建容器没挂 `LayoutElement`，横向布局组把它排成 0 宽）。裸壳的样子：四个状态色块全亮、`UI/MainQuests/*` 文案键裸显、列表空——都是没逻辑。

**数据模型（照 1.1 真格式，服务端零新模型）**
- 章节 = 一条任务 JSON：`"visitapi": { "chapter": true, "icon": "/files/quest/icon/xxx.png" }`，`image` = 横幅 URL，`secretQuest: true` 不进商人列表；`AvailableForFinish` 里的 `Quest` 条件（`target` = 子任务 id，最后一条 `isFinisher`）就是子任务清单——引擎原生会在子任务全 Success 时把章节任务判成可完成。
- 日记 = 1.1 的 `"notes": { "Started"/"Success"/"Fail": noteId }`（章节和子任务都可以带），正文走 locale 键 `<noteId>`。
- `/visitapi/quest/flags` 现在每条返回 `{anyOf, chapter, icon, notes}`；客户端 `QuestFlags` 统一缓存（`AnyOfQuest` 改读它）。
- `UI/MainQuests/*` 文案键补进 `db/locales/ch|en.json`（`CustomQuestService.AddQuestLocales` 对任意键 `TryAdd`，不限任务键）；prefab 里的 `LocalizedText` 组件（0.16 真类）会自动把键换成文案。
- 图片：`Server/images/quest/icon/` 走 `QuestLoader` 已有的 `/files/quest/icon/<名>` 路由（csproj Deploy 新增拷 images）；客户端 `ChapterImages` 用 `RequestHandler.GetData` 拉、`Texture2D.LoadImage` 转 Sprite、按 URL 缓存；**下载在 Task 里，回灌 UI 走 `Plugin.Update` 里的 `Pump()`**（不能在 ContinueWith 里碰 Unity 对象，老铁律）。
- 演示：`sora_chapter_1.json`「第一章：初次接触」= 初次接触 → 偶遇 → 侦察（finisher），两条日记。

**客户端（`Client/Native/ChapterUI/`）**
- `ChapterModel`：`QuestFlags.IsChapter` 的任务 → `Subs` = 它 Quest 条件的 target 从 `QuestBook.GetConditional` 取；状态推导：章节 Success→完成 / 有子任务失败→失败 / 有子任务 Started 以上→激活 / 否则未开放；`Conditions()` = 已开始子任务的可见完成条件（`IsNecessary` 分主/可选）；`Notes()` 按状态解锁。
- `MainQuestTabView.Show(QuestController)`（partial，逻辑在 `ChapterScreen*.cs`）：`_iconTemplate` / `_conditionsViewTemplate` / `_noteViewTemplate` 是 bundle 里的 prefab 资产引用，直接 `Instantiate` 进对应容器；1.1 用 Odin 字典存的状态物件按节点名找：图标里 `BackgroundsNormel/{Active,Complete,Failed}`、`StatusIcons/{Complete,Failed}`、`SelectedMarker`；横幅面板 `Status/{Unavailable,Succeeded,Failed,Active}`。目标行：标题 = `cond.FormattedDescription`，小字 = 子任务 description，勾 = `quest.IsConditionDone`，计数 = `ProgressCheckers[cond].CurrentValue/cond.value`。
- `ChapterTab` 切到「剧情」时 `Show(TasksScreen._questController)`（私有字段，Traverse 取）。
- 没做：未读徽章、关联物品、对话按钮（去找商人）、章节自动接取/AutoStart 链、战局内章节提示——P3。
- **P1 实测（08-28）**：图标/横幅/标题/「章节」文案/状态块都对；子任务查到了（不在任务书里的子任务=没接到，正常跳过）。Tech Leader 记的 UI 差异（逻辑通了再统一对正式版调）：① 「剧情」页签正式版带图标且排在「支线任务」前面；② 页签之间有分割线；③ 各区块（章节列/横幅/目标/日记）有各自的底图美术；④ 章节图标完成态变蓝+蓝勾（`BackgroundsNormel/Complete` + `StatusIcons/Complete` 已在 prefab 里，逻辑按状态点亮即可）。
- **P1 闭环（08-28 晚）**：接「初次接触」→ 章节变「激活」，目标行出现、对话完成后打勾。两个坑：
  - **"红方块 + 俄文 PM 手枪描述"幽灵**：不是任何 `TMP_Text.text`（探针扫全场景都找不到）。1.1 导出的 prefab 里 TMP 组件带着截场景时的 `textInfo`（十几条 meshInfo，顶点里还是当年那段 PM 描述），运行时 TMP 照着它补建一串 `TMP UI SubObject` 子网格把旧顶点画出来；字体材质被我们置空后关屏 `OnDisable` 还会 NRE。解：`TmpFix.Set(tmp, text)` = 赋值 → `ForceMeshUpdate(true,true)` → 销毁不在 `m_subTextObjects`（Traverse 取私有字段）里的孤儿 SubMesh；实例化时对 prefab 自带的 TMP 也过一遍，所有文本赋值统一走它。  - **章节日记永远不解锁**：章节任务本身没人会去"接"，状态一直是 `AvailableForStart`；`Notes()` 对章节任务改用推导出的 `Status`（激活=Started / 完成=Success / 失败=Fail），子任务仍用真状态。
  - prefab 里 `History` 高度 0 靠子节点撑：`LastHistoryNote`（默认关，有日记才开）/ `FullHistory`（展开按钮切换）。
  - **真正的红方块**（前两条修的是别的真问题，但红块另有其人）：`TasksPart/Description/QuestDescription(Clone)` —— 0.16 原生任务屏的"任务描述面板"，是 `_tasksPanel` 的**兄弟**节点，平时被任务列表挡着；切「剧情」只关 `_tasksPanel` 它就透出来（红块 = 它的图标位没图，俄文 = 它自带的样例描述）。`ChapterTab` 切页签时把 `Description` 一起开关。定位手段：按屏幕区域扫整个 `TasksScreen` 的 Graphic（`GetWorldCorners` + 画布相机换算），比猜节点名靠谱得多。
  - **左半边宿主换成 1.1 TasksPart（08-28，Tech Leader 拍板"方案 C"）**：往 0.16 布局树里塞 1.1 零件（页签栏）连撞三次墙——被父布局挤下一行、被内容宽压扁、被后生成的章节屏盖住，根因是"1.1 零件在 0.16 的布局里谁管谁"。改成整块 1.1 `TasksPart`（页签栏 + `Background` 底图 + `MainQuestPanel`；`SideQuestsPanel` 只留槽位，提取脚本 `prune` 剪掉子树，1.1 任务列表那一坨类不要）当宿主，0.16 的 `_tasksPanel`/`Description` `SetParent` 搬进去，原生 TasksPart/页签行 `SetActive(false)` 藏着（TasksScreen 仍引用 spawner，isOn 转发照旧）。桩：`AnimatedToggle : Toggle` / `UISpawnableToggle`（0.16 都有、同 GUID），`UIAnimatedToggleSpawner` 故意不桩。1.1 根节点自带 `LayoutElement`，`AddComponent` 第二个会被它盖掉。`Toggle.Set` 对未激活物件也发回调。打开任务页默认落「剧情」：`Show` 后 `story.isOn=false;isOn=true`（带通知才走动画）。
  - **方案 C 对位实录**：1.1 `TasksPart` 根是 `VerticalLayoutGroup`（上→下：`Background`(ignoreLayout) / `QuestTypeGroup` / `MainQuestPanel`(flexibleHeight) / `SideQuestsPanel`），**从 bundle 实例化后的子序和 YAML m_Children 不一致**（页签栏跑到最后）→ 代码里强制 `QuestTypeGroup.SetSiblingIndex(1)`。0.16 `TasksPart` 是四边拉伸 offs (12,50)-(-647,-125)，页签行锚在它顶部上方 ~55px → 我们的 part `offsetMax.y += 60` 把那条黑带盖住。0.16 `_tasksPanel` 搬进来要 `ignoreLayout` + 锚到栏下（offsetMax.y=-50）；`Description`（QuestDescription(Clone)）永远 `SetActive(false)`——它在 0.16 被列表盖着从不露面，任务展开走列表行内，搬家后列表底透明它就漏出来（就是那个"红块+俄文"）。"探针"法：`Show` 后 `WaitForEndOfFrame` 再 `GetWorldCorners` 逐子节点打日志，一次看清顺序/激活/矩形，比猜快得多。
  - **页签选中高亮哑火**：1.1 页签的选中态不是 `Toggle.graphic`，是 `AnimatedToggle`（0.16 真类）在 `onValueChanged→ToggleSilent→TriggerAnimation` 里给 Animator 发 `On/Off` 触发，`Highlighted` 片段动 `Background` 的 `m_Color` 和标签 `m_fontColor`。`.anim` 的每条曲线按"目标组件脚本 GUID"绑定（Image = uGUI dll 引用 `d3e719…/-765806418`），提取脚本只改写了 prefab 没改写 clip → 绑定失效、动画找不到目标。修：`copy_asset` 对 `.anim` 也过一遍 `Rewriter.run`。直接 `isOn=true` 即可触发（带通知路径）；`SetIsOnWithoutNotify` 不会。
  - **任务 JSON 里所有 id（条件 id / counter id / notes 的 noteId）必须是 24 位 hex**：服务端 4.1 用 `MongoId`/ObjectId 反序列化，长度不对整份文件报 `cannot parse ... ObjectId must be a 24-character hex string` 直接跳过。演练章节用 `5043a1ce90726f6a536f` + 2 位任务标记(a0/b0/c0) + 2 位序号。
  - **id 重映射事故**：把超长 id 截成 24 位时前缀撞上了章节一的日记 id（`…7c02n1`→`…7c11`），locale 键被覆盖 → 章节一显示第二章日记。教训：locale 键和任务 id 一律先算好唯一性再写，别做"截断式"重映射。
  - **未读标记（ReadState）**：1.1 的绿色 `!` = `MainQuestUnreadWarning._hidableObjects`（日记区 `_unreadHistoryWarning`、章节图标 `_unreadWarning`）和 `MainQuestTaskListView._unreadWarning`(GameObject)；已读 id 存 `BepInEx/config/VisitAPI/chapter_read.json`；鼠标进入区域就标记（`IPointerEnterHandler`，区域没 Graphic 就补 `NonDrawingGraphic` 接射线）。日记默认只显示最新一条（短日记），完整列表收起，展开按钮切换。**正式版规则（Tech Leader 校正）**：日记外层的 `!`（短日记自带的 `MainQuestNoteView._unreadWarning` + 章节图标角标）要**打开完整列表逐条悬停**才消，短日记本身不响应悬停；目标区则是整块悬停即读。
  - **目标行细节（对照正式版）**：行内 `TaskDescription` 是目标自己的一句话不是任务简介（我们没这数据→隐藏）；进度条 = `TaskInfo/QuestObjectiveTemplate/MainPart/Info/Progress`（子 `Image` 是 Filled 型，`fillAmount` 自己驱动，1.1 的 `_conditionView` 逻辑不在 0.16），只有计数类目标（`ProgressChecker.HasGetter && value>1`）才显示进度条和 x/y；`Info/Group` 是组队角标，隐藏。
  - 正式版底部是「相关物品」（`ChapterLinks` / `_linkedItemsView`）：章节涉及物品一行图标，日记展开后每条下面也挂物品图标 —— P3 填；现在 `Select()` 里把它和日记的 `_itemsView` 清空并关掉。

## #69 章节系统 P0：1.1 章节屏原壳移植（2026-08-28）

**目标**：不手搭 UI，把 1.1 正式版任务屏里的「剧情」面板（`MainQuestPanel`）原样搬进 0.16.9.5，逻辑由我们写。

**材料**：`tools\dump_46911`（Il2CppDumper，147 DummyDll）；`tools\EFT111_MenuUI_Data`（正式版只读拷的 level/sharedassets）；`tools\AssetRipper_win_x64`；SDK 工程 `E:\项目\Unity\EscapeFromTushonka-SDK`（2022.3.43f1，和正式版同引擎）。

**流水线**（全部脚本化，不用点 GUI）
1. **场景编号先核对**：1.0.6 时 MenuUIScene 是 `level34`，1.1.0 变成 `level48`（`globalgamemanagers` 里 714 条场景表的第 48 个）；**任务屏其实在 `CommonUIScene` = `level44`**（战局里也要用，不在 MenuUI）。白导过一次 19GB 的海岸线场景。
2. **AssetRipper 走 HTTP 驱动**：`AssetRipper.GUI.Free.exe --headless --port 8765` 是个本地网页服务；`POST /LoadFolder` `path=<假游戏目录>`、`POST /Export/UnityProject`（字段名小写 `path`，中文路径要自己按 UTF-8 百分号编码，curl 的 `--data-urlencode` 会按本地码页编坏）。假游戏目录 = `EscapeFromTarkov.exe`（空文件）+ `EscapeFromTarkov_Data\{level44,46,48 + 闭包 sharedassets 硬链接 + Managed\<146 DummyDll>}` → 识别成 Windows/Mono 游戏，脚本字段全解。闭包用扫 externals 字符串算（`tools/ChapterUI` 里那段），31~34 个文件。
3. **子树定位**：`Common UI/Common UI/InventoryScreen/Tasks Panel/TasksPart/MainQuestPanel`（83 GameObject / 296 组件），外带 4 个行模板 prefab（ChapterIcon / LinkedItemView / MainQuestTaskView / QuestNoteView）、12+ 张 Sprite、Bender 字体。
4. **`tools/ChapterUI/extract_chapter_ui.py`** 产出 SDK 的 `Assets/VisitAPI/ChapterUI/`（每次重跑整目录重建，**别往里放手写文件**）：
   - ugui/TMP 的 dll 引用 `{fileID: MD4(命名空间+类名), guid: dll}` → 改指 SDK 包脚本 `{11500000, 包 guid}`（Python 自带 hashlib 没有 MD4，工具里手写了一个）；
   - 0.16 里**已有**的 EFT 组件（NonDrawingGraphic / LocalizedText / ScrollRectNoDrag / HoverTrigger / DefaultUIButton…）→ **同 GUID 的空壳桩**放 Assembly-CSharp，运行时绑回游戏真类；`PixelPerfectSpriteScaler` SDK 已有，改指它的 GUID；
   - 1.1 **独有**的类（MainQuest* 视图、MaxSizeLayoutGroup、HoverReadTrigger、DialogButtonsContainer）→ 放进名叫 **`VisitAPI`** 的 asmdef，命名空间 `VisitAPI.ChapterUI`，字段名照 dump；运行时 bundle 认「程序集 VisitAPI + 同名类」→ 直接绑到插件 `VisitAPI.dll` 里的同名类，**序列化连线原样送达**（`Client/Native/ChapterUI/ChapterViews*.cs`，字段名一个字不能改）。跨程序集的引用字段在桩里一律声明成 `MonoBehaviour`，运行时按真类型赋。Odin 字典字段（状态物件表）拿不到，按节点名重找；
   - SoftMask → asmdef `Coffee.SoftMaskForUGUI` 桩，运行时绑回游戏那份 dll；
   - TMP 字体/材质引用清空，`ChapterBundle.Instantiate` 从任务屏现成文字上抄。
5. **打包**：`Assets/VisitAPI/Editor/ChapterUIBuild.cs`（菜单 Tools/VisitAPI/打包章节屏；命令行 `Unity.exe -batchmode -nographics -quit -projectPath … -executeMethod VisitAPI.Editor.ChapterUIBuild.BuildFromCli`）→ `Client/art/bundles/visitapi_chapterui.bundle`（458KB，LZ4HC）；csproj Deploy 拷到 `plugins/VisitAPI/bundles/`。
6. **插件**：`ChapterTab` 克隆「每日」toggle 本体进自建容器当「剧情」页签（克隆 spawner 节点会被 UIElement 收尾隐藏——第一版的坑），切过去实例化 bundle 里的 `MainQuestPanel` 挂到 `TasksPart` 下。

**内容来源**：OpenTarkov 抓包（`tools\OT_captures`）——10 章节 / 156 剧情任务 / 日记中文 / 94 段对话，见 Memory。

## #68 对话选项一次接/交多个任务（2026-08-27）

语法：`accept: 任务A 任务B` / `complete: 任务A 任务B`，**空格隔开**（逗号是指令分隔符，不能用）。每个 id 都过别名表。

- 共享库（编辑器仓库 `VisitAPI.Dlg`）：`DialogOption.AcceptIds/CompleteIds` 列表；`AcceptId/CompleteId` 保留成"第一个"的属性（get 取首个、set 清空后放一个），老消费点不用动。解析 `Ids()` 按空格拆、写手按空格拼。
- 插件：`QuestGates.Actions` 一个 id 一条 `DialogAcceptQuestAction` / `DialogFinishQuestAction`；自动门控每个 id 一条 `QuestGate`（子组内是"且"：全部 AvailableForStart / AvailableForFinish 才显示）。`QuestNotify.Owns`、`QuestRefresh` 改成扫列表。触发点的 `auto accept` 仍是单任务（没要求）。
- 编辑器 1.0.7：JS 解析 `qids(v).map(A)`、芯片一 id 一枚、⋮ 菜单任务行一行一个＋「再挂一个」/「摘掉这个」、`DlgJson` 拆空格、`DlgLinks.Apply` 对 accept/complete 改成"加进列表 / 只摘自己那条"、帮助表两行、torture.dlg + probe-writer 回环断言。
- 坑：模型里 `act` 仍是"一个选项一种动作"（accept 和 complete 不能同时挂在一个选项上）——编辑器前端模型的老限制，.dlg 手写两条指令解析器都收得住，前端只显示最后一条。

## #71 章节系统 P3：相关物品 / 去找商人 / 自动接取链 / 章节横幅 / dialogOnly / 编辑器章节模块（2026-08-28）

**任务 JSON 新开关**（都在 `visitapi` 里，服务端 `/visitapi/quest/flags` 一并下发，客户端 `QuestFlags` 缓存）：
- `autoStart: true`（子任务）：一变成可接就自动接（前置完成后服务端发下来 / 登录时补发都算）。
- `dialogOnly: true`：原生任务列表的「接受/完成」按钮换成「去找 X」，接交只能走对话。
- `items: [tpl…]`：相关物品；上交/找到类目标里的物品（`ConditionItem.target`）插件自动并入，不用重复写。

**相关物品区**（`ChapterUI/ChapterLinks.cs`）：底部 `ChapterLinks` 面板（prefab 里默认关着，就是 `_parentPanel`）和每条日记下面的 `LinksList` 用同一个 `LinkedItemView` 模板。
图标不搭 `ItemIconView`（要 prefab）：`Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), tpl, null)` 造个假物品 → `ItemViewFactory.LoadItemIcon(item)` 拿 `ItemIcon`
（和仓库格子同一套生成器，异步：`Sprite` 为空就 `Changed.Bind` 等回调），塞进 `_itemIconContainer` 下自建的一张 `Image`；悬停物品名走原生
`HoverTooltipArea.Init(ItemUiContext.Instance.Tooltip, item.LocalizedName(), true)`。章节底部那栏 = 章节自己的 + 进行中（Started/AvailableForFinish）子任务的物品
（1.1 `GetActiveLinks` 的意思）；日记下面 = 那条日记所属任务的物品（`Notes()` 现在带 quest）。物品也是"可读"项（`item:<tpl>`），新物品挂绿 `!`，悬停即读。

**「去找商人」按钮**（`ChapterUI/ChapterDialog.cs`）：目标行的 `DialogButtonsContainer`，三个按钮都是 0.16 真类 `DefaultUIButton`（桩同 GUID）。
哪个商人的 .dlg 里有 `complete:`/`handover:` 这条任务（可接状态则看 `accept:`）就去找谁——**只认对话选项，不认触发点/tab/traderId**：靠触发点接的任务在菜单里本来就没得找，
给个按钮反而把人带进一场空对话。点击 = `DialogOpener.TryOpen(tree, profile, quests, inventory, null)`，档案/背包在 `TasksScreen.Show` 的 Harmony Postfix 里按参数名截下来
（`ChapterTab.Profile/Inventory`）。战局内藏掉；「电台」「去现场」0.16 没有对应机制，藏掉。文案用 `SetRawText`（不走 locale 键）。
从对话回到章节屏后状态要跟着变：`ChapterLive`（挂在 MainQuestPanel 上的 MonoBehaviour）每 0.5s 比一次快照（章节+子任务状态、每条目标 done/计数），
变了就 `Show(quests, keepSelection:true)`——保住选中的章节和日记展开态；打开任务页时仍照正式版落在激活的那一章。

**自动接取链**（`Native/ChapterChain.cs`）：Harmony Postfix 挂 `QuestController.OnConditionalStatusChangedEvent`（状态变化）和 `QuestController.ManageConditional`
（任务进书：登录时整本扫一遍 + 服务端新发下来的）。规则：`autoStart` 且 AvailableForStart → `AcceptQuest`；章节任务 AvailableForFinish（引擎判定子任务全 Success）→ `FinishQuest`；
任一子任务 Started 且章节还是 AvailableForStart → 接章节。**必须推迟一帧**（协程 `yield return null`）：引擎正在派发这条任务的状态事件，回调里再改状态会套娃；
`_busy` 集合防同一任务连发两次网络事务（菜单里 AcceptQuest 是异步，下一帧状态还没变）。这条链顺带把 P1 遗留的"章节任务永远停在 AvailableForStart、
完成态只能靠手点"解决了：章节现在会真的 Started → Success，邮件/奖励照原生走；`ChapterModel.Status` 的 Active 也把章节自身状态算进去。

**章节横幅**（`Native/ChapterNotify.cs`）：`QuestNotify.Postfix` 先让它看一眼——章节任务自己的 Started/Success/Fail 出「新章节开始 / 章节完成 / 章节失败」
（VisitBanner 底盘，状态行金色 `#E2C56A`），其余状态吞掉；章节的子任务即使没写进 .dlg 也算 VisitAPI 的（`IsSub`），照常出任务横幅。
配套：演练章节 A/B 和章节二的 `canShowNotificationsInGame` 改 false（#64 规矩：横幅由 VisitAPI 出，原生闭嘴）。

**`dialogOnly` 按钮补丁**（`Native/DialogOnlyButton.cs`）：Postfix `QuestView.ShowButtonBlock`；只在 AvailableForStart / AvailableForFinish 两态出手（其他状态引擎本来就把按钮灰掉）；
找得到对话选项就把 `_button` 文案换成「去找 X」+ 点击开对话（`_backendSession.Profile` / `_questController` / `_inventoryController` 用 Traverse 取），
找不到或在战局里就 `SetActive(false)`。SORA 的「初次接触」「偶遇」已标上（触发点接、对话交 → 可接态按钮藏、可交态按钮变「去找 SORA」）。

**编辑器章节模块**（VisitAPI Editor，`quest.js` 属性面）：不另开页签——「VisitAPI 扩展」下加三个开关（章节 / 可接时自动接下 / 只能通过对话接交）+ 章节图标 URL
（`data-vf`：清空即删键）+ 「剧情日记」三格（`data-note`：正文存 locale、键是日记 id，**第一次输入才 `NEWID()`**，不渲染就往 json 塞空 notes）
+ 「相关物品」（复用 `#ipick` 选择器、⋮ 摘掉）。校验：`chapter_no_subs`（章节没有一条「完成任务」目标）/ `chapter_not_secret` / `bad_note_id` / `note_no_text` / `bad_item`。
探针改成 10 个开关 + 章节控件断言。

**没做/待定**：「电台」按钮（.dlg 没有电台语义，要做得先定头部语法）；相关物品只有 Item 一种（1.1 还有 Offer/Craft）；`_typeIcon` 留 prefab 默认；`ChapterLinks` 面板/按钮的版式没实机看过。全部待 Tech Leader 实机验收。

## #72 章节横幅换成 1.1 原件（2026-08-28 夜）

**目标**：#71 的章节横幅是借 VisitBanner（黑条+文字）；Tech Leader 要 1.1 那条真的——章节/子任务两张底图 + 开始/完成/失败三种对勾。

**素材在哪**：`MainQuestNotificationView` 不在 CommonUI/GameUI/MenuUI 三个已导出场景里（三个 .unity 连它的脚本 GUID 都搜不到）。0.16 里通知栏的家是 `PreloaderUI.NotifierView`
→ 1.1 同理在 **PreloaderUIScene = level49**（`globalgamemanagers` 场景表第 49 个）。模板是场景里 `…/Notifier/Content/MainQuestNotification`（默认关着，不是 prefab 资产），
`NotifierView._notificationTemplates` 里 7 个自定义视图之一。

**流水线**（照 #69，全程脚本，见 `tools/rip_export2.log`）：正式版只读拷 `level49` → `tools/EFT111_MenuUI_Data`；扫 externals 算闭包（35 个 .assets + 32 个 .resS/.resource ≈ 1.9 GB）
→ `os.link` 硬链接进假游戏目录 `tools/EFT111_Rip3`（同盘零拷贝）→ AssetRipper 那个还开着的 HTTP 服务（127.0.0.1:8765）：`POST /Reset` → `/LoadFolder` → `/Export/UnityProject`
到 `tools/EFT111_Export2`（5 分钟，18 GB；**别删——提取脚本每次重跑都从它取通知 prefab**；三个目录都进了 .gitignore）。

**提取脚本改成多根**：`extract_chapter_ui.py` 索引 `guid → (根, 相对路径)`，`ROOTS = [E, E2]`（E2 只在 `PreloaderUIScene.unity` 落地后才纳入，导出跑一半不会被扫进去）；
脚本 guid 按类名归一到第一次导出那份（实测两次导出的脚本 guid 相同，AssetRipper 是确定性的，归一只是保险）；同名不同 guid 的资产后来者改名 `__1`。
改完先在不含 E2 的情况下重跑，和备份逐字节比：老 5 个 prefab / Sprite / 桩一字不差（重跑天然会动 `.png.meta` 和文件夹 `.meta`——那是 Unity 导入时改写的，属噪音）。
新 prefab `MainQuestNotification` = `extract_subtree(scene2, "MainQuestNotification", "Content")`。

**桩的要点**：`VISIT_STUBS["MainQuestNotificationView"]` **必须把基类 `BaseNotificationView` 的 9 个字段一起列上**
（`_icon/_text/_layout/_canvasGroup/_container/_background/_defaultTextColor/_defaultBackgroundColor/_animator`）——prefab 里它们和派生字段在同一个组件 YAML 里，SDK 桩不声明，Unity 导入时就丢了。
运行时插件里 `VisitAPI.ChapterUI.MainQuestNotificationView : EFT.UI.BaseNotificationView`（0.16 真基类，字段同名自动对上），只多 7 个字段（`_title` 按 `TextMeshProUGUI` 接，prefab 里是它的子类 `CustomTextMeshProUGUI`）。

**运行时**（`ChapterUI/ChapterBanner.cs`，`ChapterNotify` 改走它）：`ChapterBanner : NotificationWithText`（= 1.1 的 `NotificationMainQuest`：Title / Sprite / IsChapter / Status），
`CreateView` 不走 `CreateNotificationView<T>`（0.16 的 `_notificationTemplates` 里没有我们这种），自己 `ChapterBundle.Instantiate("MainQuestNotification", notifier._container, 字体抄默认横幅)`
→ `notifier.SetupNotificationView(view)`（激活、排到最后、挂 OnHideComplete）→ `view.Init(banner)`：先换底图/对勾/标题，再调基类 `Init(Notification)`（图标/正文/底色/动画速度/立起），最后把章节图标塞进 `_icon`。
`ReturnToPool=false` → 躺下后 NotifierView 直接 Destroy。**`_container` 在 1.1 场景里指的是通知栏容器本身**（prefab 之外，导出后悬空）→ 运行时补 `view._container = notifier._container`。
`BackgroundColor` 给白，别让默认那层半透明黑压底图。bundle 缺 / 连线不全 → 退回默认横幅并打日志。章节的子任务也走这条（子任务底图），其余 VisitAPI 任务仍用黑条。

**素材对应**：`EFT_Notification_Storylain_Background_0/_1` = 章节 / 子任务底图，`blue_check` / `icon_requirement_locked` / `notification_icon_alert_green` = 三种对勾，
`Item.controller` + `Show/HideNotification.anim` = 立起/躺下（和 0.16 默认横幅同一套参数名，基类 `OnAnimationDone` 接动画事件）。bundle 现在 103 个资产、631 KB。

**没实机验**：横幅版式/字号、章节图标在 `_icon` 槽里的比例、状态行颜色、动画事件是否按预期回到 `OnAnimationDone`。

## #73 1.1 的剧情音效（2026-08-28 夜）

**1.1 多了 6 个 UI 音效枚举**（`EUISoundType` 44~49）：`MainQuestChapterStarted / MainQuestChapterFinished / MainQuestChapterFailed / MainQuestTaskFinished / MainQuestTaskFailed / MainQuestIconClick`。
对应表在 `Resources/audio/UISoundsWrapper.asset`（导出后按 `_soundType → _sound` guid 对回 AudioClip 名）：
44 `story_quest_chapter_start`、45 `story_quest_chapter_end`、**46 = 普通 `quest_failed`**（没有专用的"章节失败"音）、47 `story_quest_task_done_and_reward`、48 `story_quest_task_failed`、49 `story_click`。
0.16 的 `GUISounds.PlayUISound` 就是 `PlaySound(clip)`（UI 音源 + UI 混音组），所以自定义片段直接 `Singleton<GUISounds>.Instance.PlaySound(clip)` 就和原生 UI 音效同一条路。

**音频怎么拿**：AssetRipper 这个导出配置下 AudioClip 只落成 600 字节的 YAML（`m_Resource` 全空），**没有声音数据**。真数据在 `resources.resource`（正式版目录，只读）：
用"长度前缀的名字"在 `resources.assets` 里搜到 AudioClip 对象 → 按 2022 的字段顺序往后读出 `m_Resource {source, offset, size}`（`tools/ChapterUI` 这段代码在会话里，思路：name 后对齐 4、5 个 int/float、3 个 bool、对齐、string、对齐、2 个 u64、int）
→ 按 offset/size 从 `resources.resource` 切出 FSB5 → `pip install fsb5`：PCM16 的四段 `rebuild_sample` 直接给 WAV；`story_quest_chapter_end` 是 **Vorbis**，python-fsb5 要 libogg/libvorbis——`pip install pyogg` 自带 `libogg.dll/libvorbis.dll`，
把工作目录切到 pyogg 包目录再跑（`load_lib` 只在 cwd 找 `lib<name>.dll`）就能吐 OGG。产物在 `tools/ChapterUI/audio/`（4 wav + 1 ogg，共 1 MB）。python-fsb5 的 `mode` 枚举：2=PCM16、15=VORBIS（别按 0 起数）。

**进 bundle**：`extract_chapter_ui.py` 把 `tools/ChapterUI/audio/*` 拷进 SDK 的 `ChapterUI/AudioClip/`（Unity 导入即 AudioClip，不需要 .meta），`ChapterUIBuild` 多收一个 `AudioClip` 目录；运行时 `ChapterBundle.Clip(名字)`。bundle 现在 108 个资产、990 KB。

**运行时**：`ChapterBanner.Clip`（AudioClip）+ Harmony Prefix 拦 `NotifierView.PlaySound(Notification)`：是带片段的章节横幅就 `GUISounds.PlaySound(clip)` 并跳过原生那声（原生按 `SoundType` 放，不拦就是双响）。
`ChapterNotify.Show` 照 1.1 的表选片段（**表见下面 #73 补**）。章节屏点章节图标 → `story_click`（`MainQuestIconClick`）。

**没实机验**：音量是否和原生 UI 音效一致（走的是同一个音源，理论上一致）；`story_quest_chapter_end.ogg` 是 python-fsb5 重建的 Ogg 流，Unity 导入无报错但没听过。

**#73 补：1.1 到底什么时候放哪一声（反汇编定谳，2026-08-28 夜）**。Tech Leader 实机反馈"任务开始那声还是旧版的"；先排除：5 段普通任务音 + 通用通知音 `notification_exp` 在 1.1 和 0.16 里**字节一样**，
所以答案只能在代码里。IL2CPP 只有签名没方法体 → 用 capstone 直接反汇编 `tools/GameAssembly.dll`（**代码在 `il2cpp` 段（VA 0x550000，文件偏移 = RVA−0x1600），不在 `.text`**——前三轮全扫错段、零命中，白费半小时）。
路线：`NotificationMainQuest` 的 `SoundType` 是 `Nullable<EUISoundType>`，按 8 字节整体写 `[obj+0x1c]`，立即数搜不到；改搜谁调了它的构造函数 → 1.1 新类 `QuestNotificationManager`：
`NotifyConditionalStatusChanged(Quest)` 按 `IsMainQuest` 分流到 `DisplayMainQuestNotification(Quest)`（RVA 0x1B626C0），里面 `switch(status-2)` 的跳转表（0x1B62A20，6 项）解出来是：

| 状态 | 章节（QuestChapter） | 子任务（MainQuest） |
|---|---|---|
| Started | 出通知，`MainQuestChapterStarted`（story_quest_chapter_start） | **不出通知、没声音** |
| AvailableForFinish | 不出 | 出通知（按 Success 样式），`MainQuestTaskFinished`（story_quest_task_done_and_reward） |
| Success | `MainQuestChapterFinished`（story_quest_chapter_end） | **不出通知** |
| Fail / FailRestartable / MarkedAsFailed | `MainQuestChapterFailed`（= 普通 quest_failed） | `MainQuestTaskFailed`（story_quest_task_failed） |

另：`StartMissionController` 从剧情页手动开一章时也发 `MainQuestChapterStarted`；章节图标点击 `PlayUISound(MainQuestIconClick)` 直呼（RVA 0x3B84D01）。
**我们的取舍**：子任务 Started / Success 的横幅保留（Tech Leader 认过的两条叠放效果），但 `ChapterBanner.Silent=true` 一声不吭（Harmony Prefix 直接跳过 `NotifierView.PlaySound`）；
子任务「达成要求」改放 `task_done_and_reward`（1.1 就是条件一达成就当"任务完成"报）。想完全照 1.1 把子任务开始/完成的横幅也去掉：`QuestNotify` 里对 `IsSub` 的 Started/Success 直接 return 即可。
**方法论**：以后凡是"1.1 在某时刻做什么"吃不准，别猜——dump 给 RVA，capstone 反汇编，找立即数/跳转表，半小时内能定谳。

## #74 剧情任务不进支线列表 + `autoFinish`（2026-08-28 夜）

**目标**（Tech Leader 拍板"改成纯 1.1 的剧情线"后的第 2 条）：1.1 的任务屏分 剧情/支线/每日 三页，剧情任务（章节 + 子任务）只住「剧情」页，
支线列表和商人的任务页里都没有它们。我们之前靠 `dialogOnly` 把列表按钮换成「去找商人」，现在直接不让它们进列表。

**两处拦截**（`Native/StoryList.cs`，BepInEx 配置 `Chapter.HideStoryQuestsInLists` 可关）：
- 支线列表：`TasksPanel.ShowQuests(filter)` 的 filter 就是 `TasksScreen.IsRegularQuest`（static，`!(quest is DailyQuest)`）→ Postfix 把剧情任务判 false。
- 商人任务页 + "X/Y 任务"计数：都看 `Quest.IsVisible` → 在它的 getter 上 Postfix。查过 `IsVisible` 的本义只是 AvailableAfter 倒计时到没到（`Quest.method_7`），
  任务进不进任务书、`ManageConditional`/状态事件都不看它（`QuestBook.Add` 之后 `AwaitVisible` 只在不可见时等一个 task 再 Remove/Add，我们返回 false 它就什么都不做），所以改它不碰接/交/自动链。
- "剧情任务"的名单：`QuestFlags.IsStory(id)` —— 章节任务一进任务书（`ChapterChain` 挂的 `ManageConditional` 钩子）就把章节 id + 它目标里的 `ConditionQuest.target` 登记进静态集合，
  不用服务端多发字段、也不用在过滤器里拿 QuestController。

**`visitapi.autoFinish`**：子任务不进列表后没有「完成」按钮可点，接交只剩对话 `complete:` 和自动链——`autoFinish` 就是自动链的另一半：
一变 AvailableForFinish 就 `FinishQuest`（走 `ChapterChain.Run("finish")`，和章节任务自动交同一条路）。1.1 的剧情任务本来就是达成即完成不用交，所以纯 1.1 的剧情线 = 子任务全部 `autoStart + autoFinish`，对话只负责讲故事。
演练 A/B 都标上了（A 还标了 `autoStart`：新档登录即自动开跑整章，看横幅和剧情页就行）。服务端 flags 路由、编辑器属性面（1.0.9 多一个开关「达成即自动交」）同步。

**`dialogOnly` 的去留**：对不在章节里的 VisitAPI 任务仍有用（列表里换成「去找 X」），章节子任务已经不在列表里，标不标无所谓。

**没实机验**：① 支线列表/商人任务页里演练 A/B 和章节二都不见了 ② 新档登录后演练章节自动跑完（横幅：新章节开始 → 达成 → 章节完成）③ 关掉配置项后它们又回到列表。

## #75 目标行三个小按钮全从触发点反推（贴 1.1 第 3 条缩水版，2026-08-28 夜）

**侦察结论（Tech Leader 实地看了正式服）**：1.1 的"电台" = 藏身处情报中心三级上的无线电台模型，"Kerman" = 情报中心二级的笔记本——都是**藏身处里的对话触发点**，
和我们的 `trigger: hideout` 一回事；信箱里的 REPLY / VISIT 只是"提醒你去按那个东西"的入口（任务模板 `mailSettings`：fromTraderId / entryPoint / traderMessageLocaleKey / dialogId，
客户端 `DialogInvitationMessagesStorage` 按商人存，任务行的三个小按钮查的也是它）。它多的只是呈现：镜头推到道具、对话在笔记本屏幕上演（`DialogLaptopPresenter` / `DialogRadioPresenter`）。
**拍板**：`radio:` 新语法作废；镜头拉近不做（要改藏身处 bundle 才能让笔记本显示画面，图片/视频够用）。

**做法**（`ChapterUI/ChapterDialog.cs`，零新语法）：目标行的三个按钮（VisitAtLobby / ReplyByRadio / VisitAtLocation）全从 .dlg 反推——
- 「去找 X」：哪个商人的 .dlg 有 `complete:`/`handover:` 这条任务的选项（现状，可点，开对话）；
- 「去现场：地图」：`trigger: raid 地图 … if 本任务=当前状态` → 用 VisitAtLocation 槽位，地图 id 直接 `.Localized()`（游戏文案里 `Sandbox`→中心区、`Interchange`→立交桥、`bigmap`→海关，键就是 id）；
- 「去藏身处」：`trigger: hideout … if 本任务=当前状态` → 用 ReplyByRadio 槽位（图标本来就是电台，正好）。
后两个是提示型：`Interactable=false`，只告诉你去哪（1.1 的"去现场"也只是个提示 + "You must visit the trader on the location"）。只认带 `if` 门控到本任务的触发点，通用拜访点（SORA 的 `拜访 SORA`）不算。
战局内：「去找 X」藏，两个提示照出。

**没实机验**：三个按钮并排时的版式（1.1 prefab 里本来就是横排三个）；灰掉的提示按钮观感。SORA 现成剧本里「初次接触」Started 时应出「去现场：中心区」，「偶遇」Started 时应出「去现场：立交桥」。

## #76 收尾：剧情页界面文案随 DLL 走 + 1.2.0 打包清单（2026-08-28 夜）

**洞**：`UI/MainQuests/*`（「剧情」「主要目标」「相关物品」…11 条）一直躺在 SORA 的 `db/locales/ch|en.json` 里，靠 `CustomQuestService.CreateQuest` 顺带 TryAdd 进全局文案表——
发布包不带任何 locale，而且**作者一条任务都没写时 CreateQuest 根本不会跑**，新装的人剧情页全是裸键。
**修**：这 11 条搬到 `Server/ui/{ch,en}.json`，csproj `<EmbeddedResource Include="ui\*.json" />` 嵌进 DLL；`Server/UiLocales.cs`（IOnLoad，PostLoad）启动时读嵌入资源，
走 SPT 自己那条路：`LocaleTable.Global[lang]`（`LazyLoad<GlobalLocaleDictionary>`）`.AddTransformer(dict => TryAdd…)`——和 `AddQuestLocales` 一模一样，LazyLoad 真读文件时才合并。
资源名 `<root>.ui.<lang>.json`，按倒数第二段取语言。SORA 的 locale 文件和 `examples/chapter/locales` 里这批键都删了（框架的字不该让作者抄）。
`ISptLogger<T>` 在 `SPTarkov.Common.Models.Logging`（不是 Core.Models.Utils，少这个 using 一个错）。

**1.2.0 包**（照 1.1.0 的目录树 + 新增 bundle）：
`BepInEx/plugins/VisitAPI/VisitAPI.dll`、`BepInEx/plugins/VisitAPI/bundles/visitapi_chapterui.bundle`（**新**：章节屏 + 通知横幅 + 5 段音效，108 资产 990 KB）、
`BepInEx/config/VisitAPI/{backgrounds,audio}/`（空）、`SPT_Runtime/user/mods/VisitAPI-Server/VisitAPI-Server.dll`、`…/db/dialogues/dialogue.json`（零售对话 12 MB）、`…/db/{quests,locales}/`（空）、`…/images/quest/icon/`（空）。
不带 examples（仓库里有）、不带任何剧本/任务/文案。版本号 1.2.0 四处：Plugin.cs / Client csproj / ModMetadata.cs / Server csproj。
其余收尾同批：BinaryDimensionStore `unlockedByDefault` 改回 false（源码+部署）、工作区 SORA .dlg 从 D:\EFT 同步回来、README 中英补「章节系统」一节、`examples/chapter/` 一章两任务可跑示例。

## #77 打包前全面审查（2026-08-28 夜，八路 code-review + 亲手裁决）

**改了的（都编译过）**
- `QuestFlags.Prefetch` 从"Awake 时一次 ContinueWith"改成协程：服务端没起来每 5 秒重试（最多 12 次），解析挪到主线程带 try/catch（以前解析异常被吞、整季无声失效）；成功后若任务书已在（`ChapterChain.Controller`）把章节补登记一遍。
- 剧情任务名单挪进 `QuestFlags`（`ChapterOf` / `SubsOf` / `IsStory`）：`ChapterNotify.IsSub`、`ChapterChain` 接章节、`StoryList` 全查这张表，不再每次 `ChapterModel.All` 扫整本任务书。`ChapterChain` 接章节的判断改成"章节先到或子任务先到都接得住"。
- `QuestNotify`：作者没关 `canShowNotificationsInGame` 的任务（侦察）原生自己会报，我们不再叠一条（以前双响）；章节任务的 `unlockTraderOnReady` 上报以前被章节横幅的 return 吃掉，现在先报再分流。
- `ChapterImages` 改协程 + 同 URL 只下载一次（以前每个 Image 各发一次、多余的 Texture 泄漏）+ 失败不记缓存；PNG→Sprite 解码和 `VisitArt` 共用一份（`VisitArt.Decode`），`Plugin.Update` 里的 `Pump` 没了。
- `ChapterLinks`：展示用假物品按模板缓存（以前每次重画都 `CreateItem`）；`ItemIcon.Changed.Bind` 返回的是退订委托，图标到手就退（以前往全局缓存件上无限叠订阅）；清容器时放过模板本身。
- `ChapterLive` 快照只看屏上已建的章节模型 + 任务书条数（以前每 0.5 秒重扫整本）。`TmpFix` 的反射字段缓存成静态。
- 章节屏：图标用 `(模型, 视图)` 列表管（以前靠 `"Chapter:"+id` 起名再按名字找）；Unity 对象的 `?.` 全部改成显式判断（`Toggle` 小助手）；`FillList` 清容器时放过模板。`HoverReadTrigger`（prefab 自带）直接干活，私有 `HoverRead` 类删了。
- `ChapterTab`：`QuestController` 从 `TasksScreen.Show` 的参数截（不再 Traverse 反射私有字段）；`SpawnedObject` 还没生成时不炸；「剧情」页签文案直接给字不走 locale 键（其它语言会露键）。
- `.dlg` 加载：`DialogLoader.TraderIds` 按目录 mtime 缓存列表；`DialogFiles.All()` 一处提供"所有能解析的 .dlg"，`QuestNotify.Owns` / `ChapterDialog` / `TriggerManager` 四处同款表达式合并。
- `VisitHttp.Post`：好感 / 变量 / 任务达成上报三处一样的"发个 POST 只记失败"合并。
- `VisitTrigger`：`Auto` 改成从 `Data` 派生（以前 TriggerManager 另存一份）；任务控制器取法收成一个属性；坐标型 `auto` 点等 `MyPlayer` 就位再点火（以前读条阶段就开对话、失败后永久锁死）；自动接取前看状态（不是 AvailableForStart 直接跳过，省一条引擎 LogError）；找不到任务只重试 20 次（以前每 1.5 秒刷一条警告刷整局）。
- `DialogTemplateBuilder.Register` 54 行 → 32 行：旁白/台词/「继续…」拍子的登记抽成 `Beats`（纯搬运，一行没改）。
- 服务端：`UiLocales` 中文给 ch、**其它所有语言兜底英文**（以前只有 ch/en 有键）；`QuestReadyRouter` 开关名只写一次（先算对象再过滤，加新开关一处改）；`ModGuid` 恢复成 1.0.0 发布时的 `com.sora.visitapi.server`（1.1.0 开发期不知何故改成了和插件同名，服务端模组身份靠它，别再动）；`en.json` 补上「偶遇」缺的那条目标文案。

**审查提的、核实后不改的（别再翻案）**
- "战局内触发的对话直接 `complete:` 会因非法转换静默失败"——**误报**：`QuestControllerClientLocalGame.FinishQuest` 自己先把状态推到 AvailableForFinish 再交（反编译第 70-73 行）。
- "每次换节点往对话对象上叠 `OnExecuteLine` 订阅、好感会加两次"——**不成立**：`BaseTraderDialogController.method_0` 每次 `new DynamicTraderDialog`，对象不复用，订阅随对象一起没（顺带了结 #60 里"待实测"那条）。
- `MapMatches` 双向子串（`Sandbox` 也命中 `Sandbox_high`）：Ground Zero 低/高本来就同一张图，是故意的。
- `NarrationView` 全屏点击推进旁白：#65 的设计，Tech Leader 认过；改成只认字幕区要动原生窗口的异步重绘，不值。
- `VariableService._byLine` 和原生 `DialogSetVariableAction` 并存：项目铁律"行级自定义行为一律行 id 查表 + OnExecuteLine"，不改。
- 源码里的中文注释：这是 Rework 的写法（用户是 C# 小白，注释是给他看的），旧项目"无注释"那条约定不适用。
- 把 prefab 节点名（`BackgroundsNormel` 等）换成序列化字段、`ChapterTab` 的 +82/-50 像素常量换成读 RectTransform、`TmpFix` 改在提取时清 textInfo——都要重导 bundle 再实机验，1.2.0 不动，记在这儿。
- 演练章节留在 `Server/db`：只部署到本机 SPT（发布包 db/quests 是空的），`examples/chapter/` 那份才是给作者的。


## #78 剧情"失败还能继续" + 触发点 finish/fail（2026-08-29）

**起因**：Tech Leader 的故事线要"走到 Skyside 正门 → 初见开始，同时『通过地下撤离』这条目标当场作废，故事继续"。
拿 1.1 正式版截图定的口径：「塔科夫之旅」章节里有一条 **「(已失败) 了解逃出塔科夫的方法」红叉目标**，
而章节右上角**照样是「完成」**。所以"某条目标失败"是剧情手段，不是章节失败。

**改了四处**
- `TriggerParser` / `DialogTrigger` / `DialogWriter`：触发点新增 **`finish <任务>`** 和 **`fail <任务>`**，
  可与 `accept` 同时写。`VisitTrigger.Fire()` 里三者都执行完就 return（有任何一个就不开对话）；
  没接过的任务先 `SetConditionalStatus(Started)` 再迁移，否则引擎不认这个状态跳变。
- `ChapterScreen2.FillList`：目标行按 1.1 的五态画（`MainQuestTaskView.EConditionStatus` =
  Active/Completed/Incomplete/**Failed**/**Skipped**，记号 prefab 里本来就全有）：
  任务失败 → 红叉 + 标题前缀 `(UI/MainQuests/FailedTask)`；任务 Success → 它的目标一律算完成；
  章节已收尾而这条既没完成也没失败 → 减号（正式版「确认前往实验室的路线」就是这个样子）。
  失败/跳过的行不再显示进度条。
- `ChapterModel.Status`：**删掉"任一子任务失败 → 整章失败"**。章节的成败只看章节任务自己。
- 编辑器：章节页子任务 ⋮ 加「失败也算过」= 把章节那条 Quest 条件的 `status` 从 `[4]` 换成 `[4,5,6]`。
  ⚠️ **不改这个，剧情里被作废的子任务会让整章永远交不掉**（引擎要求每条 Quest 条件都满足）。

**坑**
- `"UI/MainQuests/FailedTask".Localized()` 要 `using EFT;`（`ChapterScreen2.cs` 原来只 using 了 `EFT.Quests`）。
- 触发点只能"发任务"这件事以前没人注意——因为演示剧本全靠对话里的 `complete:`。
  目标行的打勾读的是 `IsConditionDone(cond)`，**任务状态改了不等于目标打勾**，所以才补了"Success → 全算完成"这一条。


## #79 剧情线实机联调：一晚上的五个坑（2026-08-29）

Tech Leader 拿一条真剧情线（进中心区 30 秒 → 章节开始 → 走到 Skyside 门口完成「初见」→ 自动接「对峙」→ 对话完成）
从头跑，把章节系统剩下的洞全踩出来了。**每一条都不是"想得到"的，全靠日志和存档实证。**

**① `autoStart` 会在登录那一刻就把任务塞给玩家**
现象："我人都没进中心区任务就开始了"。`ChapterChain` 挂在 `ManageConditional` 上，
而**登录时任务书刚建好，没有前置的子任务当场就是"可接"** → 自动接下 → 章节跟着开始 → 两条横幅。
**改法**：子任务的 `autoStart` 加一道闸 `ChapterOpen()` —— 所属章节还没 Started 就先别接；
章节自己不属于任何章节，不受这道闸，所以"想让整章自动开始就把 autoStart 标在章节上"。
章节一开门再主动点名放行那些等着的子任务（它们不会再收到状态变化事件）。

**② 引擎不认 `Started → Success`**
触发点 `finish` 打完日志写着 `-> Success（实际变成 Started）`。`TryExecuteTransition` 和 `SetConditionalStatus` 都推不动。
**改法**：先 `SetConditionalStatus(AvailableForFinish)`，再走 `qc.FinishQuest(runNetworkTransaction: true)`（协程里、推迟一帧，同 ChapterChain 铁律）。
那才是真交任务——发奖励、发邮件、同步服务端；`SetConditionalStatus` 只是本地改个数字，出战局就没了。

**③ 横幅比图标先到**
`ChapterNotify` 取的是 `ChapterImages.Cached(icon)`，而图标是打开剧情页时才懒加载的。
触发点在进图那一刻就弹横幅 → 缓存空 → 左边那个大图标退回默认的黄色对勾，不是作者的 LOGO。
**改法**：`ChapterImages.Preload()`，flags 一到手就把所有章节图标拉下来。

**④ 章节图标的完成/失败角标被拉成巨无霸**
`ChapterFinishedIcon` 的 sprite 只有 **30×27**，而 prefab 里承载它的 `StatusIcons/Complete` 是**满铺 100×100、preserveAspect=0、scale 1**。
**1.1 的 prefab 一模一样** —— 说明正式版是用代码摆的（dump 只有签名没有方法体）。
照 Tech Leader 的正式服截图复刻：`SetNativeSize()` + 贴右下角内缩 4px（隔壁 `SelectedMarker` 就是 15×15 贴右上内缩 5，同一套惯例）。
⚠️ **凡是 prefab 里"满铺 + 不保持比例"却塞了一张小 sprite 的节点，都要怀疑正式版是代码摆的。**

**⑤ 未开放的章节不该上榜**
玩家还没接到章节，剧情页就已经列出一个空壳（横幅在、目标区全白、标着「未开放」），既像坏了又剧透。
`ChapterModel.All` 默认过滤掉 `Unavailable`；`_noTasksWarning` 之外再把横幅/目标/日记/物品整块收起来
（不收的话 prefab 的空壳横幅还挂在那儿）。想看全貌：`Chapter.ShowUnstartedChapters = true`。

**诊断用的日志（留着，Info 级）**：`[trigger] <地图>: 生成 N 个触发点` /
`[trigger] 距触发点 4.2m（需要 ≤3m）@ (x,y,z)`（走进 5×dist 内每 2 秒一次）/ `[trigger] 触发：accept/finish/fail/node` /
`[trigger] <id> -> <状态>`。**这一晚所有结论都是这几行给的** —— 坐标型触发点现场什么都不会发生，
没有距离日志就只能靠猜。BepInEx 默认不打 Debug 级，所以这几条特意用 Info。

**顺带纠正一个认知**：`complete:` 不写 `always` 时，`QuestGates` 会自动补一条「任务必须已达成」的门控。
剧情任务的目标多半是占位的 `VisitPlace`（永远达不成）→ **那个选项玩家永远看不到**。剧情对话里的 `complete:` 一律要 `always`。


## #80 发布前对账：`ifitems` + 四个"两处实现只改了一处"（2026-08-29 晚）

发布 1.2.0 之前把插件和编辑器两边源码通读交叉对账了一遍。功能是齐的
（`.dlg` 的每个指令、文件头、触发点的每个参数、任务的 `visitapi.*` 九个键，两边一一对得上；
仓库和 `D:\EFT` 两份 SORA 剧本过解析器 0 警告、回写逐字节一致），问题全出在**接缝**上。

### `ifitems`（补记，客户端 1.2.0）

`- 东西给你 | handover: job1, ifitems` —— **背包里有这条任务要交的东西时**这个选项才显示。
外部作者 abbi/mia 提的需求。`ItemsGate : DialogCondition`，`Test()` 走引擎自己的
`GetItemsForCondition`（和真正上交时 `HandoverService` 同一条路），所以"界面上看得到"
和"点进去有东西选"永远一致。**宽松口径**：有一件就算，不要求凑齐剩余数量
（严格的话"先交 3 个、剩下 2 个下次再交"就做不了）。它是**追加**的一条门控，
不替换 `handover:` 自动补的「任务处于进行中」。

### 四个洞（都是同一类病：一件事在两处实现，只改了一处）

**① `QuestNotify.Owns()` 漏了触发点的 `finish` / `fail`**
`Owns()` 判"这条任务是不是 VisitAPI 的"，只看了触发点的 `AcceptId` / `IfQuestId` ——
#78 新加的 `FinishId` / `FailId` 没跟上。后果：一条**普通任务**（不是章节也不是子任务）
只被触发点判完成/判失败、而作者按惯例关了 `canShowNotificationsInGame`
→ 状态变了**一声不响，什么横幅都没有**。

**② `ifitems` 挂在 `complete:` 选项上时，那个选项永远不出现**
兜底链原来是 `IfItemsId ?? HandoverId ?? CompleteId`。可 `complete:` 能点的时候
任务已经是「可提交」= 物品条件全做完了 → `ItemsGate` 找不到未完成的 `ConditionItem` → 恒 false。
症状正是最难查的那一类："选项莫名其妙不出现"。**去掉 `?? o.CompleteId`**：
指不到任务就干脆不加这道门。

**③ `unlockTraderOnReady` 对"不在任何 .dlg 里的任务"是死的**
`QuestNotify.Postfix` 的 `if (!chapter && !sub && !Owns(id)) return;` 挡在 `ReportReady()` 前面。
`sora_scout` 因为写进了 .dlg 才能用；别人拿这个开关做"从商人任务列表接的任务，达成就解锁隐藏商人"
**永远不会解锁**。根子在服务端：`/visitapi/quest/flags` 算了 `unlock` 却**没下发**，客户端无从判断。
**改法**：flags 里带上 `unlock`（顺带把 `icon` 补进那道 `Where` 过滤 ——
只写了图标没写别的开关的任务原来会被整条丢掉），`Postfix` 里把上报提到 `Owns()` 前面、按开关决定报不报。
上报也不再依赖 `NotificationManager` 已实例化——解锁商人和横幅是两码事。

**④ 一屏选项全被门控挡掉时没有兜底出口**
4.0.13 有明确的死锁保护（"所有选项都被门控掉则改为全部显示"，理由就是对话不能用 ESC 关），
1.x 重写后这层没了：`DialogTemplateBuilder` 只在**建的时候一条选项都没写**才补「（结束）」，
运行时全被挡掉是另一回事。翻反编译确认引擎自己有兜底
（`DynamicTraderDialog.CheckEmptyLines` → 生成一条红色 "Back"），所以不会真卡死，但
⚠️ 那条路上是 `Singleton<GlobalConfiguration>.Instance.TradersSettings[Context.Trader.Id]`
**字典裸索引**（同一个文件里别处都用 `TryGetValue`），自定义商人不在那张表里就是异常；
而且它还带 `Debug.LogError` + 红字，观感很差。
**改法**：建行时记一个 `open`（有没有一条 trigger 为 null 的行），整屏没有就补「（结束）」。
这条同时把老的 `rows.Count == 0` 分支包含进去了；作者留了 `always` 出口的节点一个字都不会多出来。

### 顺带清掉的死代码

`DialogOpener.TryOpen(tree, out error)`（无调用方，`TryOpenPlayer` 的 `scene` 参数随之收掉）、
`ChapterBundle.Sprite()`；编辑器侧 `ModWear.AllIds` / `QuestImages.NameOk` / `QuestImages.Ref`
（"文件名带点号"那道防呆前端 `quest.js` 自己有一份，C# 这份没人调）。
把两边所有 public 方法逐个扫过引用，剩下报"未被引用"的全是接口实现（Unity 消息 / Newtonsoft 转换器 / ILayoutElement）。

### 版本口径

1.2.1 / 1.2.2 从未公开发布过，**全部并回 1.2.0**：客户端、服务端 csproj、`ModMetadata`、
编辑器一律 1.2.0。csproj 顶部原来的 "1.2.1 / 1.2.2" 两段注释改标成「1.2.0 之二 / 之三」。


## #81 构建把作者的游戏内容回滚了一代（2026-08-30）

**现象**：作者在编辑器里打开任务库，章节名、描述、日记、目标文案**成片消失**，13～19 条校验警告；
而游戏里（服务端早就起着）显示的还是正确的文案。

**真凶**：`Server\VisitAPI-Server.csproj` 的 Deploy 目标里这两行——

```xml
<ItemGroup><DbFiles Include="db\**\*.json" /></ItemGroup>
<Copy SourceFiles="@(DbFiles)" DestinationFolder="$(SptDir)\user\mods\VisitAPI-Server\db\%(RecursiveDir)" />
```

本意是"顺手把演示数据装上"，实际是**每次 `dotnet build` 都把仓库里那份更旧的 db 原样盖到游戏目录**。
作者用编辑器是在**游戏目录里现写**的，所以一次为了改 BUG 的构建，就把当晚写的东西整个回滚了一代。
不报错、不提示，唯一的线索是时间戳：被覆盖的文件 mtime 会变成**仓库那份源文件的 mtime**
（MSBuild 的 `Copy` 保源时间戳），而目录 mtime 是构建那一刻。

**为什么没被立刻发现**：服务端只在启动时读一次 db，游戏里显示的还是内存里的旧表；
`.dlg` 也毫发无损（插件的 Deploy 只碰 `plugins\VisitAPI`，不碰 `config\VisitAPI`）。
**看着一切正常，只有编辑器在说话** —— 因为只有它是直接读盘的。

**救回来靠的是编辑器自己的 `.bak`**（`QuestStore` / `LocaleStore` 保存前 `File.Copy(path, path+".bak", true)`）。
⚠️ 但 `.bak` 只有一层，而且**下一次保存就会被当前这份坏的覆盖掉** ——
发现这类事故的第一件事是**先把 `.bak` 复制走，并且别按保存**。
`.bak` 是"保存前"的快照，所以最后一次保存的增量必然救不回来（这次丢了一条日记正文）。

**改法**：Deploy 只部署 DLL，`db` 和 `images` 两条 Copy 全删。演示内容住在 `examples\`，要装的人自己拷。
**更根本的一条**：`Server\db\` 本来就不该住在仓库里 —— 它从 1.0.0 那次提交起就带着作者自己的剧本
（`90726f6a656374536f726132.dlg` 还直接躺在仓库根目录），既污染开源仓库，又给这种"构建覆盖作者数据"
留了弹药。仓库里只该有空目录 + `.gitkeep`。

**教训**：**构建产物往用户的工作目录里写非产物文件，就是一颗定时炸弹。**
判断标准很简单——那个目标目录里有没有"用户自己会改的东西"。有，就一个字节都别写。
