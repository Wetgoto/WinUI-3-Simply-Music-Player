# 上传到 GitHub

这个文件夹已经是整理好的仓库内容：只包含源码、图标、截图、构建脚本和说明文档。
`Music.exe`、`.runtime`、`data`、`config` 里的个人播放数据、`bin/obj` 等都不会进入仓库。

下面命令在 PowerShell 中执行，把尖括号里的内容替换成你自己的。

## 1. 安装 Git

这台电脑目前没有检测到 Git，先安装（安装后重新打开终端）：

```powershell
winget install --id Git.Git -e --source winget
```

验证：

```powershell
git --version
```

不想用命令行的话，也可以安装 [GitHub Desktop](https://desktop.github.com/)，用它直接 “Add existing repository” 选择本文件夹，再点 Publish repository。

## 2. 在 GitHub 创建空仓库

1. 打开 <https://github.com/new>。
2. Repository name 填例如 `Music-Player`。
3. 选择 **Public**（或 Private）。
4. **不要**勾选 “Add a README file”“Add .gitignore”“Choose a license”，保持空仓库。
5. 点击 **Create repository**，复制页面上的仓库地址（HTTPS 形式）。

## 3. 初始化并推送

在 PowerShell 中进入本文件夹（把路径换成你实际存放的位置）：

```powershell
cd "<本文件夹的完整路径>\Music-Player-GitHub"
```

然后依次执行：

```powershell
git init
git add .
git commit -m "Initial commit: WinUI 3 local music player"
git branch -M main
git remote add origin https://github.com/<你的用户名>/<仓库名>.git
git push -u origin main
```

第一次 `git push` 会弹出浏览器要求登录 GitHub，按提示授权即可。
如果弹出的是账号密码输入框，密码处要填 **Personal Access Token**（GitHub 设置 → Developer settings → Personal access tokens），不是登录密码。

推送前可以先确认暂存内容，应该只有源码和文档、没有 exe 或用户数据：

```powershell
git status
git ls-files
```

## 4. 如果创建仓库时勾选了 README

GitHub 会先有一个提交，直接 push 会被拒绝。先合并远端内容再推送：

```powershell
git pull --rebase origin main
git push -u origin main
```

## 5. 发布可运行版本（Releases）

`Music.exe` 约 86 MB，**不要提交到仓库**。把它作为 Release 附件上传：

1. 在仓库页面点击右侧 **Releases** → **Draft a new release**。
2. Tag 填 `v1.0.0`，标题填 `Music v1.0.0`。
3. 把打包好的 `Music-Player-Release-win-x64.zip` 拖到附件区域。
4. 写一段更新说明，点击 **Publish release**。

README 里的 `[Releases](../../releases)` 链接会自动指向这个页面。

## 6. 可选：完善仓库信息

在仓库页面右上角 **About** 里可以填写：

- Description：`WinUI 3 本地音乐播放器，常驻播放条 / 自定义播放列表 / 全窗口播放页`
- Topics：`winui3`、`windows-app-sdk`、`dotnet`、`music-player`、`csharp`
- Website：留空即可

仓库名、描述和 License 都可以随时在 Settings 里修改。

## 常见问题

| 问题 | 处理 |
| --- | --- |
| `git` 不是内部或外部命令 | Git 没装或没进 PATH，重新安装并重开终端 |
| `remote origin already exists` | 执行 `git remote set-url origin <新地址>` |
| `failed to push some refs` | 远端有提交，先 `git pull --rebase origin main` |
| 不小心把 `Music.exe` 加进暂存区 | `git rm --cached Music/Music.exe`，它已被 `.gitignore` 忽略 |
| 想改提交信息 | `git commit --amend -m "新信息"`（尚未推送时） |
| 想撤销最近一次提交但保留文件 | `git reset --soft HEAD~1` |

## 仓库里不该出现的东西

`.gitignore` 已经覆盖这些路径；如果以后新增了别的产物，也建议加进去：

```text
bin/  obj/  .runtime/  logs/  Music.exe
config/playlist.json  config/playlists.json  config/recent.json
data/
```
