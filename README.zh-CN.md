# Deadlimit

**面向 Deadlock 模组美术人员的工具集。**

Deadlimit 将分散的 Deadlock 角色模组制作流程整合为一套面向美术人员的工作流：获取游戏原始资源、制作模型与纹理、为 Reduced CSDK 准备项目、在创作工具中持续迭代、构建 VPK，并在零售版 Deadlock 中测试。

它会处理大量重复的 Source 2 技术工作，包括从 VPK 中查找资源、手动修改资源路径、在不同工具链之间搬运文件，以及每次迭代后重新搭建相同的项目结构。

**免费 · 开源 · Windows**

[English](README.md) · [Русский](README.ru.md) · [简体中文](README.zh-CN.md) · [Português (Brasil)](README.pt-BR.md)

---

## 下载

### 面向美术人员

**[下载 `Install-Deadlimit.cmd`](https://github.com/downlimit/Deadlimit/raw/refs/heads/main/Install-Deadlimit.cmd)**

将安装程序放到你希望保存 Deadlimit 的位置，然后运行它。

Deadlimit 会在安装程序旁创建一个 `Deadlimit` 文件夹，并安装到该文件夹中。

```text
D:\Tools\Install-Deadlimit.cmd
D:\Tools\Deadlimit\
```

你不需要了解 Git，也不需要手动配置 .NET SDK。如果系统中缺少 Git for Windows 或 .NET 10 SDK，安装程序会显示缺少的组件，并在通过 WinGet 安装之前请求你的确认。

早期版本的 Deadlimit 尚未进行数字签名，因此 Windows SmartScreen 可能会显示“未知发布者”警告。请仅运行从官方 `downlimit/Deadlimit` 仓库下载的安装程序。

### 面向开发者和贡献者

如果你希望修改 Deadlimit 本身、提交修复或贡献新功能，请克隆仓库：

```powershell
git clone https://github.com/downlimit/Deadlimit.git
cd Deadlimit
.\DeadlimitManager.cmd
```

开发环境需要 .NET 10 SDK。贡献流程请参阅 [CONTRIBUTING.md](CONTRIBUTING.md)。

---

# Deadlimit Manager

**Deadlimit Manager** 是主要桌面应用，也是整个工作流的中心。

### 项目

每个模组在 Deadlimit Manager 中都是一个独立项目。Manager 会将项目保存在项目库中，并记录所选角色、源文件、已生成的创作内容、发布槽位和当前工作流状态。

### 获取原始资源

选择一个 Deadlock 角色后，Manager 会从当前安装的零售版游戏数据中提取受支持的原始资源，作为制作模组的工作基础。

这样无需手动在 VPK、模型路径、材质和相关资源依赖中查找所需文件。

### 为 CSDK 准备项目

Manager 会将美术人员自己的源文件转换为 Reduced CSDK 的可编辑创作工作区。

它会处理文件放置、已知的导出器/路径修复、模型准备、材质框架、纹理绑定，以及其他原本需要手工编辑文件的 Source 2 专用转换。

准备后的项目仍然是可编辑的创作阶段。你可以在 CSDK/ModelDoc 中打开它，继续调整材质和着色器、保存修改并持续迭代，而不是把整个流程变成不可见的一键转换。

### Live Sync

工作时可以保持 CSDK 打开。

Deadlimit Manager 会监控受支持的项目变化，并自动同步到已经准备好的 CSDK 项目中。DMX、纹理和 Vertex Color 的变化无需重复完整的准备/复制流程即可更新。结构变化或材质引用变化会触发所需的完整准备，同时保持 CSDK 打开。

### Build & Test

当项目准备好进行游戏内测试时，**Build & Test** 会处理发布侧流程。

Deadlimit Manager 会准备最新项目状态、编译发生变化的 Source 2 资源、在编译后恢复角色所需的动画绑定、验证输出结果、将 addon 打包为 VPK，并部署到已配置的本地 Deadlock addons 槽位。

CSDK 创作阶段会保持干净：动画绑定修复发生在编译之后，因此美术人员仍可以继续使用 CSDK 进行 ModelDoc 和材质编辑，再进行最终测试构建。

### 导入并修复现有 VPK

Deadlimit Manager 也可以将现有的 `pak##_dir.vpk` 导入为项目。

导入的已编译内容会被保留，不会重新经过常规创作编译流程。在 **Build & Test** 期间，Deadlimit Manager 可以将角色动画绑定与当前零售版 Deadlock 模型进行比较，修复过时或缺失的绑定，重新构建 VPK、验证结果，并部署回已采用的发布槽位。

此修复流程有意保持较窄的范围：它针对的是动画绑定相关的损坏，而不是宣称可以通用修复任何模组问题。

### 工具链管理

Manager 将 Deadlock 模组制作所需的外部工具链集中管理。

它可以定位并验证 Deadlock、管理 Reduced CSDK 和 DeadlockTools、检查受支持工具的状态，并在特定流程需要时调用 DepotDownloader 等辅助工具。

---

# Deadlimit Scripts

**Deadlimit Scripts** 是用于模型制作阶段的 DCC 端工具。

当前随项目提供的是基于 MAXScript 的实现。Blender 支持计划作为同一 Deadlimit Scripts 产品的一部分提供，而不是拆分为独立工具。

### Bone Tools

Valve DMX 骨架在导入后并不总是适合直接进行制作。

Bone Tools 可以根据层级关系调整骨骼的可视长度和粗细，在不改变绑定结构的情况下翻转显示几何体，并恢复被意外转换为 Editable Poly 的兼容骨骼，同时保留节点身份、层级、动画以及 Skin 引用。

### Vertex Color

Deadlimit Scripts 让 Vertex Color 在模型进入游戏之前更容易制作和验证。

你可以在对象颜色与 Vertex Color 之间传递颜色、切换视口显示、在多个网格之间传递 Vertex Color/材质/调色板数据，并在受支持的操作中保留现有修改器堆栈。

为了在引擎侧预览，Deadlimit Manager 可以准备一个在 CSDK 中直接显示 Vertex Color 的材质。

如果 DMX 导出器丢失 Vertex Color，**Export Vertex Color FBX** 会写出配套的 `*_vertexcolor.fbx`。在 Prepare 阶段，Manager 可以自动检测该 sidecar 文件，并把颜色数据重新传回对应的 DMX 网格。

### Inner Lineart

**Inner Lineart** 将 Deadlock 的扩张背面轮廓行为转化为角色设计内部图形线条的制作工具。

选择需要的内部边，设置线宽后，Deadlimit Scripts 会生成具有所需绕序和法线方向的独立线稿几何体。生成结果可以在适用时保留源 UV、Vertex Color、材质 ID、变换和 Skin。

这样可以把内部图形线条直接作为模型设计的一部分，而不是只能在角色外轮廓上使用 Deadlock 的线稿效果。

---

# Deadlimit Shade

**开发中。**

**Deadlimit Shade** 是工具集里面向 Substance 3D Painter 的纹理制作部分。

它的目标是让 Painter 成为适用于 Deadlock 角色材质的有效预览环境，避免纹理美术人员只能通过通用 PBR 视口判断结果，并等到进入 Source 2 后才看到实际效果。

当前原型已经包含面向 Deadlock 的 Painter 着色器、角色配置、轮廓预览工具、零售版材质/纹理检查辅助工具，以及可以把 Deadlimit Shade 预览设置应用到兼容 Painter 项目的停靠面板。

目标工作流：

```text
Substance 3D Painter
        ↓
Deadlimit Shade
        ↓
Deadlimit Manager
        ↓
Deadlock
```

目标是提供实用的创作一致性，而不是声称在 Painter 内能够逐像素复现 Source 2。

---

## 工作流

```text
Deadlock 零售版资源
        ↓
Deadlimit Manager
        ↓
DCC + Deadlimit Scripts
        ↓
Substance 3D Painter + Deadlimit Shade
        ↓
Deadlimit Manager
Prepare / Live Sync / Build & Test
        ↓
Reduced CSDK
        ↓
VPK
        ↓
Deadlock 零售版
```

Deadlimit Shade 仍处于开发阶段，对当前模型制作流程来说是可选组件。

---

## 项目状态

Deadlimit 会持续跟随不断变化的 Deadlock / Source 2 生态进行开发。

目前支持 Windows。当前随项目提供的 Deadlimit Scripts 基于 MAXScript，Blender 支持已列入计划，Deadlimit Shade 正在积极开发。

游戏和外部工具的更新可能会要求工作流进行相应修改。准确的已测试版本和当前支持状态请参阅 [COMPATIBILITY.md](COMPATIBILITY.md)。

---

## 帮助

- [兼容性](COMPATIBILITY.md)
- [更新日志](CHANGELOG.md)
- [支持](SUPPORT.md)
- [报告错误或请求功能](https://github.com/downlimit/Deadlimit/issues)

---

## 开发

Deadlimit 是开源项目。开发、贡献、DCO 和 Pull Request 要求记录在 [CONTRIBUTING.md](CONTRIBUTING.md) 中。

---

## 许可证与独立性

Deadlimit 源代码依据 [MIT License](LICENSE) 发布。依赖项和外部工具声明请参阅 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，私下报告安全漏洞的方式请参阅 [SECURITY.md](SECURITY.md)。

Deadlimit 会与用户自行安装的第三方工具和本地游戏内容协作，但不会分发这些内容。Deadlimit 是独立的社区项目，与 Valve、Autodesk、Adobe、Wall Worm 或它可以调用的其他工具的维护者不存在隶属、赞助、背书或批准关系。
