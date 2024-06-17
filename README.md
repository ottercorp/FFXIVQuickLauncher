# XIVLauncher [![Actions Status](https://img.shields.io/github/actions/workflow/status/ottercorp/FFXIVQuickLauncher/ci-workflow.yml?branch=CN)](https://github.com/ottercorp/FFXIVQuickLauncher/actions) [![Discord Shield](https://discordapp.com/api/guilds/581875019861328007/widget.png?style=shield)](https://discord.gg/HqvSXkGj) [![Crowdin](https://badges.crowdin.net/ffxivquicklauncher/localized.svg)](https://crowdin.com/project/ffxivquicklauncher) [![GitHub all releases](https://img.shields.io/github/downloads/ottercorp/FFXIVQuickLauncher/total)](https://github.com/ottercorp/FFXIVQuickLauncher/releases/latest) [![GitHub release (latest by date)](https://img.shields.io/github/v/release/ottercorp/FFXIVQuickLauncher)](https://github.com/ottercorp/FFXIVQuickLauncher/releases/latest) <a href="https://github.com/ottercorp/FFXIVQuickLauncher/releases"><img src="https://github.com/ottercorp/FFXIVQuickLauncher/raw/CN/src/XIVLauncher/Resources/logo.png" alt="XL logo" width="100" align="right"/></a>

XIVLauncher（简写为 XL）是用于最终幻想14的一个多功能启动器，包含各种可用的附加组件和游戏增强功能。

<p align="center">
  <a href="https://github.com/goatcorp/FFXIVQuickLauncher/releases">
    <img src="https://raw.githubusercontent.com/goatcorp/FFXIVQuickLauncher/master/misc/screenshot.png" alt="drawing" width="500"/>
  </a>
</p>

## 为什么要使用 XIVLauncher

FF14 原版启动器已经很老了，这导致它运行缓慢且不支持保存密码。 这个项目旨在解决以上的问题，并在游戏中添加一些能够提高游戏体验的功能，例如：

* 自动登录
* 快速更新
* Discord Rich Presence
* 快捷市场价格查询
* 聊天信息过滤
* Discord 消息转发
* Discord 通知（副本匹配、雇员销售等）

## 如何安装

[在 GitHub Release 页面下载最新的「Setup.exe」](https://github.com/ottercorp/FFXIVQuickLauncher/releases/latest)并运行 XIVLauncher 将会启动并添加至您的开始菜单。 如果您想要卸载 XL，您可以使用 Windows 应用程序菜单或者右键开始菜单中的 XIVLauncher。

⚠ <b>注意！</b> 如果您在安装过程中遇到错误或启动器无法正常工作，请先确保您已在<b>杀毒软件</b>中排除了 XIVLauncher。 许多付费杀毒软件会将 XIVLauncher 误认为病毒。 您可以自行在 VirusTotal 上查看病毒检测报告。 如果您需要任何帮助，请[查阅常见问题](https://goatcorp.github.io/faq/xl_troubleshooting#q-how-do-i-whitelist-xivlauncher-and-dalamud-so-my-antivirus-leaves-them-alone)或加入我们的 Discord 服务器。

## 进入游戏黑屏

XIVLauncher 默认会在启动游戏的同时开启插件框架，如果遇到启动游戏卡在黑屏的状况可以在设置界面给框架注入设置 5000ms 左右的延迟来解决。如果您不需要插件，也可以直接关闭插件框架。

## 如何安装插件
XIVLauncher 支持在游戏内安装由 __第三方开发者__ 制作的 __插件、扩展__。

您可以通过点击游戏系统菜单的「Dalamud Plugins」按钮来打开插件菜单。 您也可以在聊天框输入 `/xlplugins` 命令来达到同样的效果。<br>如果显示命令不存在，或者插件菜单没有出现，请检查并确保您已经在启动器设置里启用了游戏内组件功能。

## 寻求帮助
[请先查阅常见问题](https://ottercorp.github.io/faq/)，您通常可以在该处找到需要的答案。<br>您也可以加入我们的 [QQ频道](https://qun.qq.com/qqweb/qunpro/share?_wv=3&_wwv=128&inviteCode=CZtWN&from=181074&biz=ka&shareSource=5) 并在和谐友爱的社区中提问。

## 这安全吗
我们付出了很大的努力来确保 XIVLauncher 对所有人都是安全无害的。<br>如果您想要了解更多的信息，请查阅[我们对此作出的答复](https://ottercorp.github.io/faq/xl_troubleshooting#q-%E4%BD%BF%E7%94%A8-xivlauncher-dalamud-%E5%92%8C%E5%85%B6%E4%B8%AD%E7%9A%84%E6%8F%92%E4%BB%B6%E5%AE%89%E5%85%A8%E5%90%97)。

<br>
<br>

## 插件 API

您可以使用各种来自社区的插件来改善您的游戏体验。 详细的插件列表可以在[这里](https://ottercorp.github.io/DalamudPlugins/plugins)找到。 <br>如果您有意开发自己的游戏插件，请查阅我们的[API文档](https://goatcorp.github.io/Dalamud/api/index.html)和[示例插件](https://github.com/goatcorp/SamplePlugin)。

如果您希望为插件 API 本身作出贡献，请前往[此页](https://github.com/goatcorp/Dalamud)。<br>在此特别感谢 Mino 提供的注入基础！

### 这属于作弊吗

我们不认为是－我们的官方插件开发指南中写着：<br>

请确保您的插件不会以下列的方式直接与游戏服务器交互： <br>第一、*规格外*，比如允许玩家做出某些不可能由普通玩家做出的操作。 <br>第二、*自动化*，比如在没有玩家直接操作下向服务器轮询数据或发出请求，这可能会造成不必要的服务器负担或暴露该玩家正在使用 XIVLauncher。 <br>第三、*绕过付费墙*，比如允许玩家使用尚未购买的付费表情动作。

我们认为这为开发者提供了 SE 无法实现的 __改进游戏功能的自由__ ，同时禁止了会给其他平台玩家带来 __不公平优势__ 的插件。

<br>
<br>

## 免责声明
XIVLauncher 并不符合 Square Enix 的服务条款。 我们已经尽力地确保使用 XIVLauncher 对所有人来说都是安全的，且目前还没有玩家因此被封禁，但我们不能否认它存在的可能性。<br>您可以在[此处](https://goatcorp.github.io/faq/xl_troubleshooting#q-are-xivlauncher-dalamud-and-dalamud-plugins-safe-to-use)查到有关的信息。

## 特别鸣谢
<a href="https://www.jetbrains.com/community/opensource/#support">
   <img src="https://resources.jetbrains.com/storage/products/company/brand/logos/jb_beam.png" alt="JetBrains" width="200px" height="200px"><br/>
   <p>JetBrains OSS Sponsorship</p>
</a>


##### Final Fantasy XIV © 2010-2021 SQUARE ENIX CO., LTD. 保留所有权利。 我们不以任何形式附属于 SQUARE ENIX CO。
