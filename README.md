# win11-ip-switcher

轻量级 Windows 11 IP 一键切换工具（**自动获取 DHCP ↔ 手动静态 IP**），支持系统托盘常驻与开机静默自启。

A lightweight Windows 11 system tray utility to toggle between **Automatic (DHCP)** and **Manual (Static IPv4)** with one click.

---

## ✨ 功能特性

- **一键切换 IP 模式**：在“自动获取 (DHCP)”与“手动配置 (静态 IP)”之间快速切换，完整同步 Windows 11 系统网络设置。
- **参数自动记忆**：静态 IP、子网掩码、默认网关和 DNS 保存至本地 `config.json`，下次直接一键复用。
- **智能网卡选择**：优先选中已连接、带默认网关的物理网卡（自动靠后排列虚拟网卡）。
- **系统托盘常驻**：点击关闭按钮隐藏至右下角托盘；左键开关窗口，右键直接切换 IP 模式或退出。
- **开机静默自启**：支持开机自动以管理员权限静默驻留托盘，无弹窗打扰。

---

## 🛠️ 编译与运行

无需安装第三方依赖，直接调用系统自带 .NET Framework 编译器生成单文件 `IpSwitcher.exe`：

```bat
build.bat
```

运行自检测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\RunTests.ps1
```

---

## 📂 项目结构

- `IpSwitcher.cs` — 主程序源码
- `build.bat` — 编译脚本
- `tests/RunTests.ps1` — 自动化测试脚本
