# STS2 Mods

《杀戮尖塔 2》个人 mod 合集。

## Mod 列表

### QuickPlay

优化卡牌和药水操作，减少点击、告别拖拽。

| 操作 | 行为 |
|------|------|
| 点攻击牌，只有 1 个敌人 | 直接出牌，不用拖拽 |
| 点攻击牌（不拖拽），多个敌人 | 自动攻击上一次的目标 |
| 拖拽攻击牌 | 正常选目标 |
| 防御 / 技能 / 能力牌 | 点击即出 |
| 点"使用"药水，单一目标 | 直接用药，跳过选目标 |

[查看详情 →](Mods/QuickPlay/)

---

## 安装

1. 从 [Releases](https://github.com/ManSuii/sts2mode/releases) 下载对应 mod 的 zip
2. 解压到 `[Steam 目录]/Slay the Spire 2/mods/<Mod名>/`
3. 启动游戏，在 Mods 菜单中启用

## 从源码构建

```bash
.\Scripts\build.ps1 <Mod名>
```

示例：

```bash
.\Scripts\build.ps1 QuickPlay
```

## 项目结构

```
sts2mode/
├── Mods/              ← 各 mod 源码
│   ├── QuickPlay/
│   │   ├── ModEntry.cs
│   │   ├── Patches.cs
│   │   ├── mod_manifest.json
│   │   └── QuickPlay.csproj
│   └── ...             ← 后续新 mod 放这里
├── Scripts/
│   └── build.ps1      ← 构建脚本
├── Libs/              ← 基础库（RitsuLib 等）
├── Docs/              ← 设计文档
├── Releases/          ← 打包文件
└── README.zh-CN.md
```
