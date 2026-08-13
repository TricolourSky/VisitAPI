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
