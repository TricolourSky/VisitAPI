<!-- One topic per PR. Keep game files, decompiled code and extracted assets out of the diff. -->
<!-- 一个 PR 只做一件事。游戏文件、反编译代码、提取出来的资源不能进 diff。 -->

## What / 改了什么

<!-- What changes and why. Link the issue if there is one. -->
<!-- 改了什么、为什么。有 issue 就链上。 -->

## How it was tested / 怎么测的

- SPT version / SPT 版本:
- Fika installed / 是否装了 Fika: yes / no
- Steps run in game / 实机步骤:
- Relevant log lines / 相关日志行:

```
(paste BepInEx/LogOutput.log lines tagged [narrate] [dlg] [quest] [chapter/...] [trigger])
```

## Checklist / 自检

- [ ] Client and Server both build with `-p:SkipDeploy=true`, 0 warnings, 0 errors
- [ ] No change to the `.dlg` grammar or the dialogue id scheme (or an issue approving it is linked)
- [ ] No renamed / removed fields under `Client/ChapterUI/Contract/` or `Client/Native111/`
- [ ] New Harmony patch classes are registered in `Client/Core/Patches.cs`
- [ ] No diagnostic hotkeys added
- [ ] No game-derived files in the diff
