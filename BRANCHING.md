# 分支管理规范(GitFlow-lite)

本仓库采用轻量版 GitFlow。核心:**两条长期分支 + 三类临时分支**。

## 分支模型

| 分支 | 作用 | 规则 |
|---|---|---|
| `main` | 稳定发布版本 | 只接收 `release/*`、`hotfix/*` 的合并;每次发布打 tag;**不在 main 上直接开发** |
| `develop` | 日常开发集成主线 | 功能完成后合并到这里;日常工作的基准分支 |
| `feature/<名字>` | 新功能 | 从 `develop` 切出 → 完成后合回 `develop` |
| `release/<版本>` | 发布准备(测试、改版本号) | 从 `develop` 切出 → 合并到 `main` 和 `develop`,在 main 打 tag |
| `hotfix/<名字>` | 线上紧急修复 | 从 `main` 切出 → 合并到 `main` 和 `develop`,打 tag |

## 命名约定

- 功能:`feature/event-system`、`feature/object-pool`
- 发布:`release/v0.2.0`
- 修复:`hotfix/null-ref-on-init`
- 标签:`v0.1.0`、`v0.2.0`(语义化版本 主.次.修订)

## 日常用法(纯 git 命令)

### 1) 开发一个新功能
```bash
git checkout develop
git pull
git checkout -b feature/xxx
# …开发、git add、git commit…
git checkout develop
git merge --no-ff feature/xxx     # --no-ff 保留分支轨迹
git branch -d feature/xxx
git push origin develop
```

### 2) 发布一个版本
```bash
git checkout develop && git pull
git checkout -b release/v0.2.0
# 改版本号、最后测试、修小 bug 并提交
git checkout main
git merge --no-ff release/v0.2.0
git tag -a v0.2.0 -m "release v0.2.0"
git checkout develop
git merge --no-ff release/v0.2.0
git branch -d release/v0.2.0
git push origin main develop --tags
```

### 3) 线上紧急修复
```bash
git checkout main && git pull
git checkout -b hotfix/xxx
# …修复并提交…
git checkout main
git merge --no-ff hotfix/xxx
git tag -a v0.2.1 -m "hotfix xxx"
git checkout develop
git merge --no-ff hotfix/xxx
git branch -d hotfix/xxx
git push origin main develop --tags
```

## Unity 注意事项

- **`.meta` 文件必须与对应资源一起提交**,别漏(漏了会导致引用丢失)。
- 框架代码尽量按模块拆分到独立目录 + 独立 asmdef(assembly definition),便于复用与单测。
- `Library/`、`Temp/`、`Logs/` 等已被 `.gitignore` 排除,**不要强行 `git add`**。

## 远程仓库

- `origin` = `git@8.138.80.200:gitadmin/EasyFrameWork.git`(你的私有 Gitea,SSH 免密)
- 网页:http://8.138.80.200:3300/gitadmin/EasyFrameWork
