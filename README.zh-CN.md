# FileFlow

[![CI](https://github.com/taozhihaoo/file-flow/actions/workflows/ci.yml/badge.svg)](https://github.com/taozhihaoo/file-flow/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6.svg)](#环境要求)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

[English](README.md) · **简体中文**

Windows 本地文件与文档管理工具：把文件夹扫描进可检索的资料库，用标签整理文件，并通过后台任务队列
执行带完整校验的批量操作（重命名、移动、复制、删除、改扩展名）。

使用 C# / .NET 8 / WPF（MVVM）与 SQLite 实现。它不是演示用的界面壳子，而是一个小而完整的桌面应用：
没有云端、没有遥测、不访问网络。

## 界面截图

| | |
|---|---|
| ![概览](docs/screenshots/zh/dashboard.png) | ![文件](docs/screenshots/zh/files.png) |
| ![任务](docs/screenshots/zh/tasks.png) | ![设置](docs/screenshots/zh/settings.png) |

*截图由运行中的应用直接渲染生成。英文截图位于 [`docs/screenshots/en`](docs/screenshots/en)。
中文截图需要用同一条命令按中文界面再生成一次（见[重新生成截图](#重新生成截图)）：

```
FileFlow.App.exe --lang zh-CN --capture-scan demo-data --capture docs\screenshots\zh
```

## 功能特性

- **文件夹扫描** —— 选择一个或多个文件夹递归（或不递归）扫描，实时显示进度、已处理数量、错误数量，
  支持取消。单个不可读的文件或目录会被记录，不会中断整次扫描。
- **文件资料库** —— 每个扫描到的文件都记录在本地 SQLite 中（路径、文件名、扩展名、大小、创建/修改/
  索引时间），`full_path` 唯一。磁盘上不存在的文件不会被静默删除，而是标记为 `is_missing`；重新扫描
  或「校验磁盘」会重新对齐资料库与真实磁盘。
- **搜索与筛选** —— 按文件名/路径关键词搜索（如 `invoice`、`.pdf`），并支持扩展名、大小区间
  （可写 `10 MB`）、修改时间区间、标签、仅丢失文件等组合筛选。筛选输入做了防抖处理，不会每敲一个
  字符就查询一次数据库。
- **标签** —— 标签名称唯一且大小写不敏感，可新建、重命名、删除，并对选中的文件批量添加或移除。
  一个文件可挂任意数量标签，标签筛选是搜索的一部分。
- **带预览的批量操作** —— 重命名、移动、复制、删除到回收站（或永久删除）、修改扩展名、添加/移除标签。
  每个高风险操作都会先展示完整的 **操作前 → 操作后预览**，逐文件校验（非法文件名、批次内重复目标、
  与现有文件冲突），有冲突的文件跳过并报告。
- **重命名规则** —— 前缀、后缀、查找替换（大小写不敏感）、补零连续编号，可任意组合；规则变化时预览
  实时刷新。
- **任务队列** —— 批量操作以后台任务执行：同一时刻只跑一个任务，任务内部有界并发（可配置 1–16，
  默认 4）。任务可取消，每完成一项立即持久化进度，失败项可作为新任务重试。任务状态：`排队中`、
  `执行中`、`已完成`、`部分失败`、`失败`、`已取消`、`已中断`。
- **操作历史** —— 每次扫描、任务、导入、导出都会写入历史记录。重命名/移动任务会在元数据中保存
  旧路径 → 新路径映射，为将来的撤销功能打基础（撤销本身**尚未实现**）。
- **CSV 导入导出** —— 任意搜索结果可导出为 CSV
  （`file_name, full_path, extension, size_bytes, modified_at, tags`，UTF-8 带 BOM）。导入会校验表头，
  跳过并报告非法行及其行号，同时避免文件内部以及与资料库之间的重复。只处理元数据，从不读取或上传
  文件内容。
- **设置** —— 扫描默认项、默认导出目录、最大并发数、破坏性操作确认、回收站或永久删除、日志级别、
  **界面语言**。全部存储在 `%LOCALAPPDATA%\FileFlow` 下的 SQLite 中，支持一键恢复默认。
- **概览面板** —— 已索引文件数、资料库体积、标签数、丢失文件数、活动任务、含失败的操作、近期任务与
  近期扫描。
- **中英双语界面** —— 英文与简体中文，可在设置页即时切换（无需重启），默认英文。
- **截图模式** —— `--capture <目录>` 会把每个页面渲染成 PNG，用于保持文档截图（两种语言）与界面同步。
- **日志** —— 基于 `Microsoft.Extensions.Logging` 的自研滚动文件日志
  （`%LOCALAPPDATA%\FileFlow\logs\fileflow-yyyyMMdd.log`），级别可配，消息带任务上下文。

## 架构

清晰分层 + 依赖倒置，依赖只向内：

```
FileFlow.App（WPF，MVVM）
    视图 + 视图模型（CommunityToolkit.Mvvm）、本地化服务、对话框
        |
FileFlow.Core（不依赖 WPF、SQLite，也不直接访问文件系统）
    领域模型、接口、业务规则
    重命名/移动规划器、文件名与路径校验、扫描器、状态机、CSV、字符串目录
        |
FileFlow.Infrastructure
    SQLite（Microsoft.Data.Sqlite，手写参数化 SQL）
    仓储、数据库迁移
    WindowsFileSystem（IFileSystem）、回收站 Shell API
    文件服务、任务队列、导入导出、设置、文件日志
```

代码中强制遵守的规则：

- 视图不接触 SQLite；视图模型不直接调用文件 API，只用 Core 的接口。
- Core **不依赖** WPF 或 SQLite，也不使用 `System.IO.File`；所有真实文件操作都在 Infrastructure 的
  `IFileSystem` 之后。
- 所有 SQL 均参数化（不做字符串拼接），数据访问集中在仓储类中。
- 业务逻辑只通过 `ISettingsService` 读取配置，从不读环境变量。
- 错误被分类（`ErrorKind`：FileAccess、FileNotFound、PathValidation、OperationConflict、
  Persistence、Cancelled），在 UI 层翻译为可读文案，技术细节写入日志。
- 所有面向用户的文案集中在 Core 的 `AppStrings`，因此译文可通过单元测试校验键一致性与占位符兼容性。

### 技术选型

| 组成 | 选择 | 原因 |
|---|---|---|
| 运行时 | .NET 8（LTS） | 长期支持版本 |
| UI | WPF + XAML，MVVM 使用 CommunityToolkit.Mvvm | Windows 桌面原生，命令/属性源码生成 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 组合根统一装配 |
| 日志 | Microsoft.Extensions.Logging + 自研文件提供器 | 不引入第二套日志框架 |
| 数据库 | Microsoft.Data.Sqlite | 本地、免服务，不用 EF Core，手写参数化 SQL |
| 测试 | xUnit | 快速、可重复 |

直接 NuGet 依赖：`CommunityToolkit.Mvvm`、`Microsoft.Extensions.DependencyInjection`、
`Microsoft.Extensions.Logging`、`Microsoft.Data.Sqlite`（Infrastructure 另有
`Microsoft.Extensions.Logging.Abstractions`）。仅此而已。

## 环境要求

- Windows 10（1809+）或 Windows 11，x64
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（框架依赖版需要；自包含版不需要）
- 从源码构建：.NET 8 SDK

## 安装

下载或自行构建发布包，运行 `FileFlow.App.exe`。首次启动会创建 `%LOCALAPPDATA%\FileFlow\`
（数据库与日志目录）—— 无需安装程序，不写注册表，不需要管理员权限。

便携运行（数据目录放在 exe 旁边而不是 AppData）：

```
FileFlow.App.exe --datadir D:\FileFlowData
```

### 命令行参数

| 参数 | 作用 |
|---|---|
| `--datadir <路径>` | 使用便携数据目录，替代 `%LOCALAPPDATA%\FileFlow` |
| `--lang <en\|zh-CN>` | 本次运行覆盖界面语言 |
| `--capture <目录>` | 把每个页面渲染为 PNG 后退出 |
| `--capture-scan <目录>` | 先索引该目录（与 `--capture` 配合使用） |

## 从源码运行

```
dotnet build
dotnet test
dotnet run --project src/FileFlow.App
```

## 演示数据

生成约 100 个虚构文件（发票、文档、报告、图片、项目、归档，不含任何真实个人数据）：

```
powershell -ExecutionPolicy Bypass -File scripts\create-demo-data.ps1
```

然后在 FileFlow 中：**文件 → 浏览 → 选择 `demo-data` → 扫描**。

## 重新生成截图

应用可以自己渲染页面，这样文档截图始终与界面一致，并且同一次构建就能产出两种语言的版本：

```
# 英文
FileFlow.App.exe --lang en --capture-scan demo-data --capture docs\screenshots\en

# 中文
FileFlow.App.exe --lang zh-CN --capture-scan demo-data --capture docs\screenshots\zh
```

每次运行会生成 `dashboard.png`、`files.png`、`tasks.png`、`tags.png`、`history.png`、
`settings.png`。CI 工作流也把这两条命令做成了手动任务
（*Actions → CI → Run workflow*），结果以 `screenshots` 工件的形式上传。

## 本地化

- 所有文案位于 `src/FileFlow.Core/Localization/AppStrings.cs`（英文 + 简体中文）。
- XAML 通过 `{loc:T Key}` 绑定；代码使用 `Loc.Instance["Key"]` / `Loc.Instance.F("Key", args)`。
- 在 **设置 → 外观** 中切换语言会立即刷新所有文案，并写入设置表持久化。
- `LocalizationTests` 会在两种语言不一致（缺键、空值、`{0}` 占位符不兼容）时让构建失败。

新增语言：在 `AppStrings` 中增加一本字典，在 `AppLanguages.All` 中登记语言代码，并扩展
`DisplayNames.Language`。

## 数据库

SQLite 位于 `%LOCALAPPDATA%\FileFlow\fileflow.db`（WAL 模式，外键开启）。表结构由一个小巧、
幂等、基于版本表的迁移器在每次启动时创建/更新。

| 表 | 用途 |
|---|---|
| `files` | 已索引文件，`full_path` 唯一，`is_missing` 标记 |
| `tags` | 标签名（大小写不敏感且唯一） |
| `file_tags` | 多对多关联，级联删除 |
| `tasks` | 队列条目：类型、状态、计数、时间戳、JSON 元数据 |
| `task_items` | 单项状态、源/目标路径、错误类型与消息 |
| `operation_history` | 每次操作/扫描的历史记录 |
| `settings` | 键值形式的应用设置（JSON） |
| `schema_version` | 已应用的迁移版本 |

在文件名、扩展名、修改时间、丢失标记上建有索引，搜索尽量走索引。

## 文件操作

- 重命名/移动/复制/删除逐项执行：校验 → 磁盘执行 → 更新数据库行 → 记录进度。单个文件失败不会中断
  批次，任务最终为 `已完成`、`部分失败`（有成功有失败）或 `失败`（全部失败）。
- 按路径加锁，避免同一文件上的重命名/移动/删除/复制互相竞争（键大小写不敏感）。
- **不提供跨文件系统与数据库的原子性** —— Windows 文件系统与 SQLite 无法共享事务。若文件操作成功而
  数据库更新失败，该项会记录为失败并给出恢复提示，重新扫描即可对齐资料库。这是明确记录的行为，不是
  隐式的"尽力而为"。
- 删除默认走 Windows 回收站（Shell API，`FOF_ALLOWUNDO`），设置中可改为永久删除。没有安全擦除、
  没有绕过删除限制、没有权限绕过 —— 完全遵循 Windows ACL。
- 关键系统目录（`C:\Windows`、`C:\Program Files*`、`C:\ProgramData`）下的批量操作会额外警告。

## 任务队列

同一时刻只运行一个任务；任务内部通过 `Parallel.ForEachAsync` 有界并发（设置：1–16，默认 4）。
每完成一项立即持久化（任务计数 + 单项记录），因此崩溃不会留下"半截"批次：下次启动会把处于
`执行中` 的任务标记为 `已中断`，仍在 `排队中` 的任务会自动继续。**重试失败项** 只会把失败项作为
新任务重新入队。暂停功能未实现（取消已实现）。

## 搜索与标签

关键词匹配文件名与路径（`LIKE` 并对通配符转义）；扩展名/大小/时间/标签筛选与其组合。标签名大小写
不敏感且唯一，通过 `file_tags` 建立多对多关系并级联删除。

## 导入 / 导出

导出把当前搜索结果写为 RFC-4180 CSV。导入会校验表头，跳过并报告非法行（行号 + 原因），在文件内部与
资料库之间去重，并创建/关联 `tags` 列中的标签。已存在的记录会更新导入进来的大小与修改时间；创建时间
保持原值，因为 CSV 导出并不携带它。

## 配置

所有设置都存放在 `settings` 表中，启动时一次性加载进 `ISettingsService`；修改会经过校验、范围钳制并
立即生效（例如日志级别、界面语言）。未设置的项使用默认值。

## 测试

238 个自动化 xUnit 测试（全部确定性 —— 内存假文件系统、真实 `TempDirectory` 夹具与一次性 SQLite
数据库；绝不触碰用户真实的文件系统与资料库）：

- **Core（137）** —— 文件名/路径校验、受保护路径识别、重命名/移动/复制规划（规则、重复、冲突、
  仅大小写不同的重命名）、任务状态机流转、CSV 解析与写出往返、体积格式化、扫描器行为（递归、隐藏
  文件、取消、进度、错误隔离）、以及翻译目录一致性（键对齐、空值、占位符兼容）。
- **Infrastructure（101）** —— 数据库迁移（幂等性、数据保留、外键级联）、全部仓储（CRUD、搜索
  筛选、分页、标签关系、丢失标记）、Windows 文件系统映射（被占用/只读/不存在的文件）、文件服务
  （校验、仅大小写重命名、并发保护）、基于真实 SQLite + 临时文件的端到端任务队列测试（重命名/移动/
  复制/删除/标签任务、部分失败 → `部分失败`、确定性取消、重试失败项、崩溃恢复 → `已中断`、排队任务
  续跑、撤销元数据、并行创建同名标签竞争）、CSV 导入导出（往返、带行号的非法行、重复处理、表头
  校验）、设置往返与校验、按键互斥锁。

```
dotnet test -c Release
```

## Windows 构建

```
# 框架依赖（目标机器需要 .NET 8 桌面运行时）
dotnet publish src/FileFlow.App -c Release -r win-x64 --self-contained false

# 自包含（无需运行时）
dotnet publish src/FileFlow.App -c Release -r win-x64 --self-contained true
```

两者都会生成 `FileFlow.App.exe`。

## 持续集成

`.github/workflows/ci.yml` 在 `windows-latest` 上对每次推送到 `main` 以及每个 PR 执行：

1. 还原（带 NuGet 缓存）→ Release 构建（`TreatWarningsAsErrors`）→ `dotnet test`（输出 TRX 并收集
   代码覆盖率）；
2. 上传测试结果与 `win-x64` 发布工件；
3. 手动任务（`workflow_dispatch`）重新生成中英文截图并作为工件上传。

## 安全与隐私

威胁模型与漏洞上报方式见 [SECURITY.md](SECURITY.md)（英文）。要点：

- FileFlow 只操作**本地文件系统，从不上传文件内容** —— 无云端、无遥测、无外部 API 调用、完全不联网。
- 日志包含路径与任务计数，绝不包含文件内容、密钥或凭据。
- 破坏性操作必须经过预览 + 确认；受保护的系统目录会额外警告。
- 业务逻辑中不读取 `Environment.GetEnvironmentVariable`，配置集中管理。
- `bin/`、`obj/`、`.vs/`、`*.db`、`logs/`、`demo-data/` 与用户数据目录不会进入版本库（见 `.gitignore`）。

## 已知限制

- 仅支持 Windows（WPF、回收站 Shell API）。
- 单用户本地资料库：无云同步、无身份认证、无多用户、不支持网络存储、不监听外部改动（需重新扫描刷新）。
- 不做全文内容索引，常规扫描也不计算内容哈希（表结构中预留了可空的 `content_hash` 列）。
- 无法提供跨文件系统与数据库的事务；一致性通过重新扫描对齐（见「文件操作」）。
- 撤销功能只做了数据准备（保存路径映射），尚未实现。
- 回收站删除没有自动化测试覆盖（会触碰真实回收站）；永久删除路径有覆盖。
- 非 ASCII/超长路径等边界情况依赖 .NET/Windows 的标准行为（已开启长路径感知清单）；UNC/网络路径会被
  校验拒绝。

## 项目结构

```
FileFlow.sln
src/
  FileFlow.Core/                 领域模型、接口、规划器、校验、扫描器、CSV、状态机、字符串目录
  FileFlow.Infrastructure/       SQLite（迁移、仓储）、WindowsFileSystem、回收站、
                                 文件服务、任务队列、导入导出、设置、文件日志
  FileFlow.App/                  WPF：视图、视图模型、对话框、DI 组合、主题、本地化、截图服务
tests/
  FileFlow.Core.Tests/           校验器、规划器、状态机、CSV、扫描器、本地化
  FileFlow.Infrastructure.Tests/ 数据库、仓储、文件服务、任务队列、导入导出、设置
scripts/
  create-demo-data.ps1           生成约 100 个虚构演示文件
docs/screenshots/en|zh/          各语言的界面截图
```

## 许可证

[MIT](LICENSE)
