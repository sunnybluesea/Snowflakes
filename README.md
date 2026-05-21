# Snowflakes 🌊❄️

Windows 雪花屏保，双版本 — 冰晶版 & 海洋版。

Windows snowflake screensavers — two editions: Ice Crystal & Ocean.

---

## 版本 / Editions

### Snowflakes（冰晶版 / Ice Crystal）

雪花缓缓飘落，背景为毛玻璃效果的桌面壁纸。六角冰晶带有精致花纹，雪落到底部逐渐堆积。支持多显示器。

Snow falls gently over a frosted-glass desktop wallpaper. Hexagonal ice crystals with delicate patterns, snow accumulates at the bottom. Multi-monitor support.

### SnowflakesOcean（海洋版 / Ocean）

夜幕星空下，雪花落入大海。闪烁的星星、随机划过的流星、温柔起伏的海浪、波光粼粼的水面、菱形星光点缀海面、雪花落水产生涟漪。支持多显示器。

Snow falls into a dark ocean under a starry night sky. Twinkling stars, random meteors, gentle rolling waves, shimmering water surface, diamond sparkles on the ocean, ripples when snow hits water. Multi-monitor support.

---

## 安装 / Installation

1. 下载 `Snowflakes.scr` 或 `SnowflakesOcean.scr`
2. 右键 → **安装**，或复制到 `C:\Windows\System32\`
3. 在 Windows 屏保设置中选择即可

---

1. Download `Snowflakes.scr` or `SnowflakesOcean.scr`
2. Right-click → **Install**, or copy to `C:\Windows\System32\`
3. Select in Windows Screensaver Settings

---

## 编译 / Build

需要 .NET Framework 4.x 和以下系统引用：

Requires .NET Framework 4.x with system references:

```bash
csc /target:winexe /out:Snowflakes.scr Snowflakes.cs \
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll

csc /target:winexe /out:SnowflakesOcean.scr SnowflakesOcean.cs \
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll
```

---

## 特性 / Features

- 多显示器支持 / Multi-monitor support
- 显示器热插拔自动退出 / Auto-exit on display config change
- 鼠标/键盘退出 / Mouse/keyboard exit
- 预览模式（显示设置小窗） / Preview mode (Display Settings mini-window)
- 纯 GDI+ 绘制，无外部依赖 / Pure GDI+ drawing, zero external dependencies
