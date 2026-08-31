# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Notes for this repo

五個標籤已建在 GitHub 上（`gh label list`）。標籤字串與角色名稱相同，沒有做任何改名。

判斷 `ready-for-agent` vs `ready-for-human` 時，本專案有兩類工作幾乎一定是
`ready-for-human`：

- **需要在真實 WebView2 Fixed Version runtime 或乾淨 Windows 上驗證的**
  （見 `docs/ARCHITECTURE.md` §7、§8）——agent 沒有那個環境。
- **CP-SAT 建模的取捨**——約束該設成硬性還是軟性、目標函數怎麼加權，
  是需要跟案主確認的領域判斷，不是規格問題。
