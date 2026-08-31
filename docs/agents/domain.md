# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root, or
- **`CONTEXT-MAP.md`** at the repo root if it exists — it points at one `CONTEXT.md` per context. Read each one relevant to the topic.
- **`docs/adr/`** — read ADRs that touch the area you're about to work in. In multi-context repos, also check `src/<context>/docs/adr/` for context-scoped decisions.

If any of these files don't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates them lazily when terms or decisions actually get resolved.

## File structure

This repo is **single-context**:

```
/
├── CONTEXT.md
├── docs/adr/
│   ├── 0001-....md
│   └── 0002-....md
├── src/            ← .NET（Domain / Application / Solver / Api / Shell）
└── frontend/       ← Vue SPA
```

`src/` 與 `frontend/` 是同一個領域的兩個層，不是兩個 bounded context——
醫師、班別、硬性/軟性限制、求解 job 這些詞彙前後端共用。
因此**只有一份 `CONTEXT.md`**。替兩邊各寫一份會讓同一個名詞長出兩份會漂移的定義，
而 `api-contract.yaml` 存在的全部意義就是逼兩邊講同一種語言。

For reference, a multi-context repo (presence of `CONTEXT-MAP.md` at the root) would look like:

```
/
├── CONTEXT-MAP.md
├── docs/adr/                          ← system-wide decisions
└── src/
    ├── ordering/
    │   ├── CONTEXT.md
    │   └── docs/adr/                  ← context-specific decisions
    └── billing/
        ├── CONTEXT.md
        └── docs/adr/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

排班領域特別容易漂移的一組詞：**班別 / 班次 / shift**、**值班 / 值勤 / on-call**、
**硬性限制 / 硬約束 / hard constraint**。`CONTEXT.md` 建立後請以它為準，
C# 型別名稱與 TypeScript 型別名稱都要對得上 `api-contract.yaml` 的 schema 名稱。

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders) — but worth reopening because…_

## 與 `docs/ARCHITECTURE.md` 的關係

`docs/ARCHITECTURE.md` 是**技術選型**的決策紀錄（已定案，第 2 節列出所有已否決的選項），
不是領域文件。它先於 ADR 存在，兩者不衝突：

- 要改**技術棧**（runtime、殼、求解器、橋接方式、發佈形態）→ 先讀 `ARCHITECTURE.md` §2，
  多數選項已被評估並否決過。
- 要記錄**領域決策**（約束怎麼建模、排班規則怎麼取捨）→ 寫成 `docs/adr/` 底下的 ADR。
