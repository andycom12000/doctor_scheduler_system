// Claude Design canvas 的資料綁定與示範資料。{{ }} 與 <sc-for> 的來源。

class Component extends DCLogic {
  // hover 直接改 DOM，不觸發任何 re-render
  hl(name) {
    const mx = document.getElementById('nsp-mx'), bd = document.getElementById('nsp-bd');
    if (mx) mx.querySelectorAll('[data-nm]').forEach(el => {
      const on = !name || el.dataset.nm === name;
      el.style.opacity = on ? '' : '.15';
      el.style.outline = name && el.dataset.nm === name ? '1.5px solid var(--color-text)' : '';
      el.style.outlineOffset = '-1.5px';
    });
    const dp = document.getElementById('nsp-dp');
    if (dp) dp.querySelectorAll('[data-nm]').forEach(el => {
      const on = name && el.dataset.nm === name;
      el.style.boxShadow = on ? 'inset 0 0 0 1px var(--color-text)' : '';
    });
    if (bd) bd.querySelectorAll('[data-row]').forEach(el => {
      const on = !name || el.dataset.row === name;
      el.style.opacity = on ? '' : '.4';
      el.style.background = name && el.dataset.row === name ? 'color-mix(in srgb,var(--color-accent) 16%,transparent)' : '';
    });
  }

  // 年月切換：左右箭頭快切，點標題開月份面板
  // API: GET /schedules?year=&month= → 切換後重新載入該月班表（草稿或已發布）
  stepYm(n) {
    this.setState(s => {
      const ym = s.ym || { y: 2026, m: 9 };
      let y = ym.y, mm = ym.m + n;
      if (mm < 1) { mm = 12; y--; } if (mm > 12) { mm = 1; y++; }
      return { ym: { y, m: mm }, ymYear: y };
    });
  }

  ymVals() {
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const ym = (this.state && this.state.ym) || { y: 2026, m: 9 };
    const open = !!(this.state && this.state.ymOpen);
    const py = (this.state && this.state.ymYear) || ym.y;
    return {
      ymLabel: `${ym.y} 年 ${ym.m} 月班表`,
      ymPrev: () => this.stepYm(-1), ymNext: () => this.stepYm(1),
      ymToggle: () => this.setState(s => ({ ymOpen: !s.ymOpen, ymYear: (s.ym || ym).y })),
      ymLabelS: `display:flex;align-items:center;gap:6px;padding:3px 8px;cursor:pointer;font:600 20px/1.2 'Barlow Condensed','Noto Sans TC',sans-serif;border:1px solid ${open ? 'var(--color-accent)' : 'transparent'};${open ? 'background:color-mix(in srgb,var(--color-accent) 8%,transparent);' : ''}`,
      ymPanelS: `display:${open ? 'block' : 'none'};position:absolute;top:34px;left:0;z-index:20;width:236px;padding:11px 12px 12px;background:var(--color-bg);border:1px solid var(--color-accent);box-shadow:var(--shadow-md)`,
      ymYearLabel: String(py),
      ymYearPrev: () => this.setState({ ymYear: py - 1 }),
      ymYearNext: () => this.setState({ ymYear: py + 1 }),
      ymThisMonth: () => this.setState({ ym: { y: 2026, m: 9 }, ymYear: 2026, ymOpen: false }),
      ymMonths: Array.from({ length: 12 }, (_, i) => {
        const mm = i + 1, sel = py === ym.y && mm === ym.m;
        return {
          t: mm + ' 月',
          s: `height:30px;display:flex;align-items:center;justify-content:center;cursor:pointer;font:600 12.5px/1 'Barlow Condensed','Noto Sans TC',sans-serif;border:1px solid ${sel ? 'var(--color-accent)' : dim(12)};${sel ? 'background:var(--color-accent);color:var(--color-bg);' : ''}`,
          on: () => this.setState({ ym: { y: py, m: mm }, ymOpen: false })
        };
      })
    };
  }

  // SCREEN 01 的拉線註解隨分頁換：共用 4＋4 支，分頁專屬 2 支
  annsFor(view) {
    const L = (top, k, routes, items) => ({ cls: 'an anL', pos: `top:${top}px`, k, routes, items });
    const R = (top, k, routes, items) => ({ cls: 'an anR', pos: `top:${top}px`, k, routes, items });
    const tabName = { mx: '區域 × 日', dp: '日 × 人', dd: '單日詳表' }[view];
    if (view === 'dd') {
      return {
        annTab: tabName,
        anns: [
          L(12, '讀取', ['GET /api/schedules/{ym}/days/{date}'], ['本頁主資料：單日 5 區的指派人與該人月負載', '「前一日／次一日」換 date 重取']),
          L(168, '讀取', ['GET /api/calendars/{year}'], ['判定該日為平日／假日／國定假日', '決定標題下方「1 班 n 點」']),
          R(40, '寫入', ['PATCH /api/schedules/{ym}/duties'], ['在單日列表上直接換人', '已發布仍可改，改完重新發布']),
          R(196, '讀取', ['GET /api/schedules/{ym}/candidates'], ['空缺列的可指派人員建議（資格＋額度＋間隔）']),
          R(332, '讀取', ['GET /api/schedules/{ym}/violations'], ['空缺列的標示：只帶 code 與 severity', '渲染方式由前端決定'])
        ]
      };
    }
    const common = [
      L(12, '讀取', ['GET /api/schedules'], ['有值班表的月份清單（年月切換器）']),
      L(92, '讀取', ['GET /api/schedules/{ym}'], ['單月完整值班表：狀態、revision、每格指派', '單一科部，沒有 unitId']),
      L(196, '讀取', ['GET /api/staff'], ['人員名冊 33 人一次全帶，依身分組排列', '編輯介面見 SCREEN 06 人員維護']),
      L(320, '讀取', ['GET /api/calendars/{year}', 'GET /api/blocked-days/{ym}'], ['表頭星期與假日底色；補班日視為平日', '已登記的不可排班日＝斜紋格，求解輸入']),
      R(62, '寫入', ['PATCH /api/schedules/{ym}/duties', 'POST /api/schedules/{ym}/duties/swap'], [
        view === 'mx' ? '點「區域 × 日」格改指派人員' : '點「日 × 人」格改指派區域',
        '兩格對調 · 臨時換人是常態'
      ]),
      R(240, '動作', ['POST /api/schedules/{ym}/validate', 'POST /api/solver-jobs'], ['工具列「驗證約束」', '工具列「重新求解」→ 只回 jobId']),
      R(400, '動作', ['GET /api/schedules/{ym}/export', 'POST /api/schedules/{ym}/publish'], ['匯出 Excel', '「重新發布」會重算月結轉；發布不等於鎖定']),
      R(560, '讀取', ['GET /api/schedules/{ym}/violations'], [
        '每筆只帶 code 與 severity，不帶 display',
        view === 'mx' ? '右側違規側欄；底色／斜紋／外框由前端決定' : '衝突格底色與右側空缺欄'
      ])
    ];
    const perTab = {
      mx: [
        L(452, '讀取', ['GET /api/schedules/{ym}/point-board'], ['額度點數看板：已排／上限、剩餘額度、假日班數', '公平性點數為選配欄位，停用時整欄隱藏']),
        L(566, '讀取', ['GET /api/schedules/{ym}/point-board'], ['右側身分組容量利用率由同一份看板彙總'])
      ],
      dp: [
        L(452, '讀取', ['GET /api/schedules/{ym}/point-board'], ['表格底部兩列：每人班數與額度點數', '欄位順序依 4 個身分組']),
        L(566, '讀取', ['GET /api/schedules/{ym}/vacancies'], ['右側「空缺」欄的未填補區域代號'])
      ],
      dd: []
    };
    return { anns: common.concat(perTab[view]), annTab: tabName };
  }

  setStaff(i) { this.setState({ staffSel: i }); }

  setBrush(b) { this.setState({ brush: b }); }

  paint(name, j) {
    this.setState(s => {
      const reg = Object.assign({}, s.reg || this.nbSeed());
      const key = name + '-' + j, b = s.brush || 'on';
      if (b === 'clear' || reg[key]) delete reg[key]; else reg[key] = 1;
      return { reg };
    });
  }

  // SCREEN 01 值班表：5 區 × 33 人，不可排班日沿用 SCREEN 05 的登記
  s01Model() {
    const P = this.props || {};
    const ym = (this.state && this.state.ym) || { y: 2026, m: 9 };
    const key = `${P.pointWeekday || 1}|${P.pointHoliday || 2}|${P.quotaCap || 12}|${ym.y}-${ym.m}`;
    const G = globalThis;
    if (!G.__s01 || G.__s01Key !== key) { G.__s01 = this.buildS01(ym); G.__s01Key = key; }
    return G.__s01;
  }

  buildS01(ym) {
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const P = this.props || {};
    const wD = Math.max(1, P.pointWeekday ?? 1), wH = Math.max(1, P.pointHoliday ?? 2);
    const cap = Math.max(2, P.quotaCap ?? 12), fairOn = P.fairnessPointOn === true;
    const restGap = 3, npDayCap = 20, npMaxConsec = 6;
    const MM = String(ym.m).padStart(2, '0');
    const wdn = ['日', '一', '二', '三', '四', '五', '六'];
    const nD = new Date(ym.y, ym.m, 0).getDate(), dow0 = new Date(ym.y, ym.m - 1, 1).getDay();
    const cw = 26, chh = 24, dpw = 23, dph = 21;
    const a2days = [];
    for (let i = 0; i < nD; i++) {
      const dw = (dow0 + i) % 7, he = dw === 0 || dw === 6;
      a2days.push({
        dd: String(i + 1).padStart(2, '0'), wd: wdn[dw], he, p: he ? wH : wD,
        th: `width:${cw}px;flex:none;padding:3px 0 4px;text-align:center;${he ? `background:${dim(7)};` : ''}`,
        wdS: `display:block;font:600 9px/1 'Barlow Condensed',sans-serif;color:${dim(50)}`,
        ddS: "display:block;font:600 11px/1.35 'Barlow Condensed',sans-serif",
        pS: `display:block;${mono};font-size:8px;color:${he ? 'var(--color-accent-700)' : dim(32)}`
      });
    }
    const GT = [
      { tint: 'color-mix(in srgb,var(--color-accent) 12%,transparent)', fg: 'var(--color-accent-800)' },
      { tint: 'color-mix(in srgb,var(--color-accent) 30%,transparent)', fg: 'var(--color-accent-900)' },
      { tint: 'var(--color-accent)', fg: 'var(--color-bg)' },
      { tint: dim(12), fg: dim(70) }
    ];
    const GNAME = ['G1 · PGY1 PGY2 R1 打工R', 'G2 · R2 R3', 'G3 · R4 R5 R6', 'G4 · NP'];
    const people = this.nbRoster().people.map(p => Object.assign({}, p, { pts: 0, hol: 0, dutyDays: [], last: -99, lastArea: null, streak: 0, byArea: {} }));
    const byName = {};
    people.forEach((p, i) => { p.idx = i; p.ab = p.name.slice(0, 2); byName[p.name] = p; });
    const blocked = this.nbSeed();
    const isBlocked = (p, j) => !!blocked[p.name + '-' + j];

    const AREAS = [{ id: 'A', t: 'WARD' }, { id: 'B', t: 'WARD' }, { id: 'C', t: 'WARD' }, { id: 'ICU', t: 'ICU' }, { id: '總值', t: 'CHIEF' }];
    const elig = (p, t) => t === 'WARD' ? true : t === 'ICU' ? p.icu : p.chief;
    const order = ['總值', 'ICU', 'A', 'B', 'C'].map(id => AREAS.find(a => a.id === id));
    const assign = {};
    for (let j = 0; j < nD; j++) {
      const used = {}, he = a2days[j].he, w = he ? wH : wD;
      order.forEach(a => {
        const cand = people.filter(p => {
          if (!elig(p, a.t) || used[p.name] || isBlocked(p, j)) return false;
          if (p.g === 3) return p.dutyDays.length < npDayCap && !(p.last === j - 1 && p.streak >= npMaxConsec);
          return j - p.last >= restGap && p.pts + w <= cap;
        });
        cand.sort((x, y) => {
          const np = (x.g === 3 ? 1 : 0) - (y.g === 3 ? 1 : 0);                       // S5 NP 盡量不用
          if (np) return np;
          if (a.t === 'ICU') { const pf = (x.g === 1 ? 0 : 1) - (y.g === 1 ? 0 : 1); if (pf) return pf; }  // S3
          if (a.t === 'WARD') { const pf = (x.g === 2 ? 1 : 0) - (y.g === 2 ? 1 : 0); if (pf) return pf; } // S4 資深留給總值
          if (a.t === 'CHIEF' && he) { const nh = x.hol - y.hol; if (nh) return nh; }
          const rem = (cap - y.pts) - (cap - x.pts);                                   // S1 剩餘額度多的先排
          if (rem) return rem;
          const same = ((x.lastArea === a.id) ? 0 : 1) - ((y.lastArea === a.id) ? 0 : 1); // S2 同區延續
          if (same) return same;
          return x.idx - y.idx;
        });
        const pick = cand[0] || null;
        if (pick) {
          used[pick.name] = 1;
          if (pick.g !== 3) pick.pts += w;
          pick.streak = pick.last === j - 1 ? pick.streak + 1 : 1;
          pick.last = j; pick.lastArea = a.id;
          if (he) pick.hol++;
          pick.dutyDays.push({ j, area: a.id, he });
          pick.byArea[a.id] = (pick.byArea[a.id] || 0) + 1;
        }
        assign[a.id + '-' + j] = pick ? pick.name : null;
      });
    }
    // 已發布仍可逐格修改：兩處臨時換人，正是違規清單的來源
    const edits = [];
    const jb = Math.min(nD - 2, 19);
    const pB = people.find(p => isBlocked(p, jb) && p.g <= 1 && assign['A-' + jb] !== p.name);
    if (pB) { assign['A-' + jb] = pB.name; edits.push({ code: 'H5_BLOCKED_DAY', sev: 'error', who: `${pB.name} · A ${MM}/${String(jb + 1).padStart(2, '0')}`, title: '排到本人已登記的不可排班日' }); }
    const jg = Math.min(nD - 1, 20);
    const pG = people.find(p => p.g === 1 && p.dutyDays.some(d => d.j === jg - 1) && !isBlocked(p, jg));
    if (pG) { assign['B-' + jg] = pG.name; edits.push({ code: 'H4_MIN_GAP', sev: 'error', who: `${pG.name} · B ${MM}/${String(jg + 1).padStart(2, '0')}`, title: '值休休值：與前一日的值班相隔不足 3 天' }); }

    const cellS = (g, he) => `width:${cw}px;height:${chh}px;flex:none;display:flex;align-items:center;justify-content:center;font:500 9.5px/1 "Noto Sans TC",sans-serif;background:${GT[g].tint};color:${GT[g].fg};border-right:1px solid ${he ? dim(20) : dim(7)};border-bottom:1px solid ${dim(7)};`;
    const vacS = `width:${cw}px;height:${chh}px;flex:none;display:flex;align-items:center;justify-content:center;${mono};font-size:9.5px;color:var(--color-accent-900);background:repeating-linear-gradient(45deg,var(--color-accent-300) 0 3px,transparent 3px 6px);border:1.5px solid var(--color-accent)`;
    const TYPES = [
      { c: 'WARD', n: '一般病房', ids: ['A', 'B', 'C'], meta: '3 區 · 每區每日 1 人 · 全部身分可值', g: 0 },
      { c: 'ICU', n: 'ICU', ids: ['ICU'], meta: '1 區 · 每日 1 人 · R2 以上可值 · S3 優先 R2/R3', g: 1 },
      { c: 'CHIEF', n: '總值', ids: ['總值'], meta: '1 區 · 每日 1 人 · 僅 R4~R6（13 人）· S4 優先', g: 2 }
    ];
    const a2groups = TYPES.map(t => ({
      code: t.c, name: t.n, meta: t.meta,
      badge: `${mono};padding:2px 6px;background:${GT[t.g].tint};color:${GT[t.g].fg}`,
      rows: t.ids.map(id => {
        let filled = 0;
        const cells = a2days.map((d, j) => {
          const nm = assign[id + '-' + j];
          if (!nm) return { t: '缺', nm: '', s: vacS };
          filled++;
          return { t: byName[nm].ab, nm, s: cellS(byName[nm].g, d.he) };
        });
        return { id, cells, stat: filled + '/' + nD, statS: `width:44px;flex:none;text-align:right;${mono};font-size:10px;color:${filled < nD ? 'var(--color-accent)' : dim(45)}` };
      })
    }));
    const fill = a2days.map((d, j) => {
      const miss = AREAS.filter(a => !assign[a.id + '-' + j]).length;
      return {
        t: String(AREAS.length - miss),
        s: `width:${cw}px;height:18px;flex:none;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;${miss ? 'background:var(--color-accent-200);color:var(--color-accent-900);' : `color:${dim(40)};`}border-right:1px solid ${dim(6)}`
      };
    });
    const GLABEL = [['G1', '一般病房'], ['G2', '一般病房 · ICU'], ['G3', '全部含總值'], ['G4', 'NP · 一般病房']];
    const a2legend = GLABEL.map((g, i) => ({
      t: g[0], label: g[1],
      s: `${mono};font-size:9px;padding:3px 6px;background:${GT[i].tint};color:${GT[i].fg}`
    }));

    const boardCols = [0, 1, 2, 3].map(gi => ({
      label: GNAME[gi],
      hdS: `${mono};font-size:9.5px;letter-spacing:.06em;padding:3px 5px;margin-bottom:2px;background:${GT[gi].tint};color:${GT[gi].fg}`,
      rows: people.filter(p => p.g === gi).map(p => {
        const np = p.g === 3;
        const pct = np ? Math.min(100, Math.round(p.dutyDays.length / npDayCap * 100)) : Math.min(100, Math.round(p.pts / cap * 100));
        return {
          name: p.name, rank: p.rank, hol: String(p.hol), fair: '—',
          pts: np ? p.dutyDays.length + '/' + npDayCap + '天' : p.pts + '/' + cap,
          left: np ? '—' : '餘' + (cap - p.pts),
          on: () => this.hl(p.name), off: () => this.hl(null),
          rkS: `${mono};font-size:9px;padding:2px 4px;flex:none;width:34px;text-align:center;background:${GT[p.g].tint};color:${GT[p.g].fg}`,
          leftS: `${mono};font-size:9.5px;width:30px;text-align:right;flex:none;color:${!np && cap - p.pts <= 1 ? 'var(--color-accent-800)' : dim(50)}`,
          bar: `height:5px;width:${pct}%;background:${pct >= 95 ? 'repeating-linear-gradient(45deg,var(--color-accent-800) 0 3px,var(--color-accent-400) 3px 6px)' : 'var(--color-accent)'}`
        };
      })
    }));
    const util = [0, 1, 2, 3].map(gi => {
      const ps = people.filter(p => p.g === gi);
      if (gi === 3) {
        const u = ps.reduce((s, p) => s + p.dutyDays.length, 0), c = ps.length * npDayCap;
        const pct = Math.round(u / c * 100);
        return { lv: 'G4 NP', txt: `${u} / ${c} 天（H6 每月 20 天）`, pct: pct + '%', s: `height:4px;width:${pct}%;background:${dim(45)}` };
      }
      const u = ps.reduce((s, p) => s + p.pts, 0), c = ps.length * cap;
      const pct = Math.round(u / c * 100);
      return { lv: 'G' + (gi + 1), txt: `${u} / ${c} 點`, pct: pct + '%', s: `height:4px;width:${pct}%;background:${pct >= 92 ? 'var(--color-accent-800)' : 'var(--color-accent)'}` };
    });

    const vacList = [];
    AREAS.forEach(a => a2days.forEach((d, j) => { if (!assign[a.id + '-' + j]) vacList.push(`${a.id} ${MM}/${d.dd}`); }));
    const overCap = people.filter(p => p.g !== 3 && p.pts > cap);
    const rawAlerts = [];
    if (vacList.length) rawAlerts.push({ code: 'H1_AREA_COVERAGE', sev: 'error', title: `${vacList.length} 個區域-日沒有合格且可用的人`, who: vacList.slice(0, 4).join('、') + (vacList.length > 4 ? ' 等' : '') });
    edits.forEach(v => rawAlerts.push(v));
    if (overCap.length) rawAlerts.push({ code: 'H3_QUOTA_CAP', sev: 'error', title: `${overCap.length} 人超過額度點數上限`, who: overCap.slice(0, 3).map(p => `${p.name} ${p.pts}/${cap}`).join('、') });
    const a2alerts = rawAlerts.map(v => ({
      kind: v.sev === 'error' ? 'error' : 'warn', code: v.code, title: v.title, who: v.who,
      badge: `${mono};font-size:9px;padding:3px 5px;flex:none;${v.sev === 'error' ? 'background:var(--color-accent);color:var(--color-bg)' : `background:var(--color-accent-200);color:var(--color-accent-900)`}`
    }));

    // 日 × 人：列＝日期，欄＝人員依身分組
    const dpEmp = p => p.rank;
    const dpGroups = [0, 1, 2, 3].map(gi => {
      const n = people.filter(p => p.g === gi).length;
      return {
        label: `${GNAME[gi].split(' · ')[0]} · ${n} 人`,
        s: `width:${n * dpw}px;flex:none;text-align:center;font:600 10px/1 'Barlow Condensed',sans-serif;letter-spacing:.1em;padding:5px 0;background:${GT[gi].tint};color:${GT[gi].fg};border-right:1px solid var(--color-bg)`
      };
    });
    const dpHead = people.map(p => ({
      name: p.name, rank: p.rank,
      on: () => this.hl(p.name), off: () => this.hl(null),
      s: `width:${dpw}px;flex:none;height:86px;display:flex;align-items:center;justify-content:center;border-right:1px solid ${dim(8)};border-bottom:1px solid ${dim(8)}`,
      nS: 'writing-mode:vertical-rl;text-orientation:upright;font:500 11px/1 "Noto Sans TC",sans-serif;letter-spacing:.06em',
      eS: `width:${dpw}px;flex:none;height:46px;display:flex;align-items:center;justify-content:center;border-right:1px solid ${dim(8)};border-bottom:1px solid ${dim(14)};writing-mode:vertical-rl;font:600 9px/1 'Barlow Condensed',sans-serif;color:${dim(58)};letter-spacing:.04em`
    }));
    const shortArea = { 'A': 'A', 'B': 'B', 'C': 'C', 'ICU': 'I', '總值': '總' };
    const dutyOf = {};
    people.forEach(p => { dutyOf[p.name] = {}; });
    AREAS.forEach(a => a2days.forEach((d, j) => { const nm = assign[a.id + '-' + j]; if (nm) dutyOf[nm][j] = a.id; }));
    const dpRows = a2days.map((d, j) => {
      const vac = AREAS.filter(a => !assign[a.id + '-' + j]).map(a => a.id);
      return {
        d: String(j + 1), wd: d.wd,
        labS: `width:44px;flex:none;height:${dph}px;display:flex;align-items:center;gap:4px;padding-left:5px;${d.he ? `background:${dim(9)};` : ''}border-right:1px solid ${dim(14)};border-bottom:1px solid ${dim(7)};font:600 11px/1 'Barlow Condensed',sans-serif`,
        wdS: `font:400 8.5px/1 "Noto Sans TC",sans-serif;color:${dim(45)}`,
        cells: people.map(p => {
          const area = dutyOf[p.name][j];
          const bl = isBlocked(p, j);
          const conflict = area && bl;
          const base = `width:${dpw}px;flex:none;height:${dph}px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;border-right:1px solid ${dim(7)};border-bottom:1px solid ${dim(7)};`;
          const s = conflict
            ? `${base}background:repeating-linear-gradient(45deg,var(--color-accent-800) 0 2px,var(--color-accent-400) 2px 5px);color:var(--color-bg);box-shadow:inset 0 0 0 1.5px var(--color-accent-900)`
            : area
              ? `${base}background:${d.he ? 'var(--color-accent-300)' : 'var(--color-accent-100)'};color:var(--color-accent-900)`
              : bl
                ? `${base}background:repeating-linear-gradient(45deg,${dim(22)} 0 1px,transparent 1px 4px);color:${dim(0)}`
                : `${base}${d.he ? `background:${dim(9)};` : ''}color:${dim(0)}`;
          return { t: area ? shortArea[area] : '', nm: p.name, s };
        }),
        vac: vac.length ? vac.join(' ') : '',
        vacS: `width:56px;flex:none;height:${dph}px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;border-left:1px solid ${dim(14)};border-bottom:1px solid ${dim(7)};${vac.length ? 'background:repeating-linear-gradient(45deg,var(--color-accent-300) 0 3px,transparent 3px 6px);color:var(--color-accent-900)' : `color:${dim(30)}`}`
      };
    });
    const dpFoot = people.map(p => ({
      n: String(p.dutyDays.length),
      pt: p.g === 3 ? '—' : String(p.pts),
      s: `width:${dpw}px;flex:none;height:19px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;border-right:1px solid ${dim(7)};border-top:1px solid ${dim(14)};color:${dim(62)}`,
      pS: `width:${dpw}px;flex:none;height:17px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;border-right:1px solid ${dim(7)};color:${p.g !== 3 && p.pts >= cap ? 'var(--color-accent-800)' : dim(45)}`
    }));

    const ddOf = j => {
      const d = a2days[j], w = d.he ? wH : wD;
      const rows = AREAS.map(a => {
        const nm = assign[a.id + '-' + j], p = nm ? byName[nm] : null;
        const type = a.t === 'WARD' ? '一般病房' : a.t === 'ICU' ? 'ICU' : '總值';
        return {
          area: a.id, type,
          rank: p ? p.rank : '—', name: p ? p.name : '空缺',
          rkS: `${mono};font-size:9.5px;padding:3px 5px;flex:none;width:42px;text-align:center;background:${p ? GT[p.g].tint : dim(8)};color:${p ? GT[p.g].fg : dim(45)}`,
          load: p ? (p.g === 3 ? `本月 ${p.dutyDays.length}/${npDayCap} 天 · 假日 ${p.hol} 班` : `額度 ${p.pts}/${cap} 點 · 餘 ${cap - p.pts} · 假日 ${p.hol} 班`) : '無合格且可用的人',
          s: `display:flex;align-items:center;gap:10px;padding:9px 11px;border:1px solid ${p ? 'var(--color-divider)' : 'var(--color-accent)'};${p ? '' : 'background:repeating-linear-gradient(45deg,color-mix(in srgb,var(--color-accent) 12%,transparent) 0 4px,transparent 4px 8px);'}`
        };
      });
      const nvac = rows.filter(r => r.name === '空缺').length;
      return {
        label: `${ym.y} / ${MM} / ${d.dd}（星期${d.wd}）`,
        sub: `${d.he ? '假日' : '平日'} · 1 班 ${w} 點 · 5 區各 1 人`,
        stat: nvac ? `${nvac} 區空缺` : '全數填補',
        statS: `${mono};padding:4px 9px;${nvac ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`,
        rows
      };
    };
    return {
      a2days, a2groups, fill, a2legend, boardCols, util, a2alerts, dpGroups, dpHead, dpRows, dpFoot,
      ddOf, restGap: String(restGap), nD,
      areaCount: String(AREAS.length), peopleCount: String(people.length),
      nDays: String(nD), assignN: String(AREAS.length * nD),
      alertTag: rawAlerts.length ? `${rawAlerts.filter(v => v.sev === 'error').length} error` : '無違規',
      qHoN: String(wH), fairOn,
      capNote: `額度上限暫以 ${cap} 點代入，正式值見 SCREEN 02 的身分設定`,
      fairNote: fairOn ? '公平性點數（實驗性）：查表值待案主提供，此處以「—」佔位。' : '公平性點數（S7）預設停用，整欄隱藏；啟用後成為看板的次要欄位。',
      fairNoteS: `font-size:10.5px;line-height:1.5;color:${dim(50)};margin-top:8px`
    };
  }

  s01Vals() {
    const m = this.s01Model();
    const day = Math.max(0, Math.min(m.nD - 1, (this.state && this.state.day != null) ? this.state.day : 12));
    const out = Object.assign({}, m, { dd: m.ddOf(day) });
    delete out.ddOf; delete out.nD;
    return out;
  }

  // SCREEN 02 區域／身分／點數規則、SCREEN 03 資格矩陣與約束（CONSTRAINT_DEFAULTS.md）
  s23Vals() {
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const GT = [
      { tint: 'color-mix(in srgb,var(--color-accent) 12%,transparent)', fg: 'var(--color-accent-800)' },
      { tint: 'color-mix(in srgb,var(--color-accent) 30%,transparent)', fg: 'var(--color-accent-900)' },
      { tint: 'var(--color-accent)', fg: 'var(--color-bg)' },
      { tint: dim(10), fg: dim(65) }
    ];
    const badge = g => `${mono};font-size:10px;padding:3px 6px;display:inline-block;background:${GT[g].tint};color:${GT[g].fg}`;

    const s2Areas = [
      { code: 'WARD', name: '一般病房', areas: ['A', 'B', 'C'], note: '每日 3 人 · 每區 1 人', g: 0 },
      { code: 'ICU', name: 'ICU', areas: ['ICU'], note: '每日 1 人', g: 1 },
      { code: 'CHIEF', name: '總值', areas: ['總值'], note: '每日 1 人 · 僅 R4~R6 可值', g: 2 }
    ].map(a => ({ code: a.code, name: a.name, areas: a.areas, note: a.note, badgeS: badge(a.g) }));

    const RK = [
      ['PGY1', 0], ['PGY2', 0], ['R1', 0], ['打工R', 0],
      ['R2', 1], ['R3', 1],
      ['R4', 2], ['R5', 2], ['R6', 2], ['NP', 3]
    ];
    const ableOf = g => g === 2 ? '一般病房 · ICU · 總值' : g === 1 ? '一般病房 · ICU' : '一般病房';
    const s2Ranks = RK.map(r => {
      const code = r[0], g = r[1], np = code === 'NP', r6 = code === 'R6';
      return {
        code, grp: 'G' + (g + 1), able: ableOf(g),
        cap: np ? '不計' : '待填',
        capS: `${mono};font-size:11px;padding:3px 7px;${np ? `border:1px solid ${dim(14)};color:${dim(55)}` : `background:${dim(7)};color:${dim(72)}`}`,
        ptType: np ? '不計' : '待確認',
        badgeS: badge(g),
        ov: r6 ? '待填' : '',
        ovWrapS: r6 ? 'display:flex;align-items:center;gap:6px' : 'display:none',
        ovTagS: `${mono};font-size:9.5px;padding:2px 5px;background:var(--color-accent);color:var(--color-bg)`,
        ovDashS: r6 ? 'display:none' : `color:${dim(30)}`
      };
    });

    const fairRows = [['平日', '平日'], ['平日', '假日'], ['假日', '平日'], ['假日', '假日']];
    const s2Fair = ['TYPE A', 'TYPE B'].map(label => ({
      label, rows: fairRows.map(r => ({ d0: r[0], d1: r[1], v: '待填' }))
    }));

    const s3Types = [{ c: 'WARD', n: '一般病房 · 3 區' }, { c: 'ICU', n: 'ICU · 1 區' }, { c: 'CHIEF', n: '總值 · 1 區' }];
    const cellS = ok => `flex:1;height:22px;display:flex;align-items:center;justify-content:center;${mono};font-size:11px;border:1px solid ${ok ? 'var(--color-accent)' : dim(12)};${ok ? 'background:var(--color-accent);color:var(--color-bg)' : `color:${dim(28)}`}`;
    const s3Elig = RK.map(r => {
      const g = r[1];
      const ok = [true, g === 1 || g === 2, g === 2];
      return {
        code: r[0], badgeS: `${badge(g)};width:58px;flex:none;text-align:center`,
        cells: ok.map(v => ({ t: v ? '✓' : '✗', s: cellS(v) }))
      };
    });

    const primS = `${mono};font-size:9.5px;padding:2px 6px;flex:none;background:${dim(8)};color:${dim(66)}`;
    const s3Hard = [
      ['H1', '每日每區恰好 1 人', 'H1_AREA_COVERAGE', 'ExactCount', '全體', '—'],
      ['H2', '身分資格', 'H2_ELIGIBILITY', 'Eligible', '全體', '—'],
      ['H3', '額度點數上限', 'H3_QUOTA_CAP', 'Budget', 'NP 豁免 · quota_point', '—'],
      ['H4', '值休休值', 'H4_MIN_GAP', 'MinGap', 'NP 豁免', 'days 3'],
      ['H5', '不可排班日', 'H5_BLOCKED_DAY', 'Forbidden', '全體', '—'],
      ['H6', 'NP 每月天數上限', 'H6_NP_MONTHLY_DAYS', 'Budget', 'NP · duty_day', 'cap 20'],
      ['H7', 'NP 最多連六', 'H7_NP_MAX_CONSECUTIVE', 'MaxConsecutive', 'NP', 'days 6']
    ].map(h => ({ id: h[0], name: h[1], code: h[2], prim: h[3], scope: h[4], params: h[5], primS }));

    const dirTag = d => d
      ? `${mono};font-size:9.5px;padding:2px 6px;flex:none;width:34px;text-align:center;${d === '優先' ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(18)};color:${dim(62)}`}`
      : 'display:none';
    const s3Soft = [
      ['S1', '額度點數組內公平', 'S1_QUOTA_FAIRNESS', 'NP 豁免 · quota_point', '', '100'],
      ['S2', '同區延續', 'S2_AREA_CONSISTENCY', 'NP 豁免', '', '40'],
      ['S3', 'R2/R3 優先 ICU', 'S3_R2R3_PREFER_ICU', 'R2 R3 · ICU', '優先', '50'],
      ['S4', 'R4~R6 優先總值', 'S4_R4R6_PREFER_CHIEF', 'R4 R5 R6 · 總值', '優先', '50'],
      ['S5', 'NP 盡量不用', 'S5_NP_LAST_RESORT', 'NP', '避開', '60'],
      ['S6', 'NP 避開假日', 'S6_NP_AVOID_HOLIDAY', 'NP · 日類 假日', '避開', '30'],
      ['S7', '公平性點數組內公平', 'S7_FAIRNESS_POINT', 'NP 豁免 · fairness_point', '', '0']
    ].map(s => ({ id: s[0], name: s[1], code: s[2], scope: s[3], dir: s[4], dirS: dirTag(s[4]), w: s[5] }));

    const P = this.props || {};
    return {
      s2Areas, s2Ranks, s2Fair, s3Types, s3Elig, s3Hard, s3Soft,
      qWd: String(P.pointWeekday ?? 1) + ' 點', qHo: String(P.pointHoliday ?? 2) + ' 點',
      satBonus: '待填', satWindow: '待填'
    };
  }

  // SCREEN 05 名冊：33 人、10 身分、4 身分組（CONTEXT.md）；資格由資格矩陣直接給定
  nbRoster() {
    if (this.__roster) return this.__roster;
    const RK = [['PGY1', 0, 4], ['PGY2', 0, 3], ['R1', 0, 3], ['打工R', 0, 1],
                ['R2', 1, 4], ['R3', 1, 3],
                ['R4', 2, 5], ['R5', 2, 4], ['R6', 2, 4], ['NP', 3, 2]];
    const SUR = '陳林黃張李王吳劉蔡楊許鄭謝洪郭曾邱廖賴徐周葉蘇莊呂江何蕭羅高潘簡方'.split('');
    const GIV = ['彥廷','孟儒','宜靜','家豪','于萱','冠宇','品瑄','柏翰','思妤','宗翰','雅涵','允中','昱翔','詩涵','建宏','怡君','政霖','舒涵','秉澄','語彤','俊霖','念慈','子齊','若瑄','奕辰','嘉玲','世勳','佩璇','振宇','曉薇','孟軒','雨柔','宸睿'];
    let i = 0; const people = [];
    RK.forEach(r => {
      for (let k = 0; k < r[2]; k++) {
        people.push({
          name: SUR[i] + GIV[i], emp: 'D' + String(1400 + i * 7), rank: r[0], g: r[1],
          ward: true, icu: r[1] === 1 || r[1] === 2, chief: r[1] === 2
        });
        i++;
      }
    });
    this.__roster = { people };
    return this.__roster;
  }

  nbSeed() {
    if (this.__nbSeed) return this.__nbSeed;
    const people = this.nbRoster().people;
    const rnd = s => Math.abs((Math.sin(s * 12.9898) * 43758.5453) % 1);
    const reg = {};
    people.forEach((p, i) => {
      if (i % 9 === 5) return;
      const target = i % 17 === 3 ? 17 : 2 + Math.floor(rnd(i + 1) * 11);
      let put = 0, guard = 0;
      while (put < target && guard < 300) {
        const j = Math.floor(rnd((i + 1) * 31 + guard * 7) * 31);
        guard++;
        if (reg[p.name + '-' + j]) continue;
        reg[p.name + '-' + j] = 1; put++;
      }
    });
    people.filter(p => p.chief).slice(0, 11).forEach(p => { reg[p.name + '-12'] = 1; });
    this.__nbSeed = reg;
    return reg;
  }

  // SCREEN 05 不可排班日登記：GET · PUT · DELETE /api/blocked-days/{ym} ＋ /feasibility
  nbVals() {
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const sel = (this.state && this.state.ym) || { y: 2026, m: 9 };
    const ny = sel.m === 12 ? sel.y + 1 : sel.y, nm = sel.m === 12 ? 1 : sel.m + 1;
    const NM = String(nm).padStart(2, '0');
    const P = this.props || {};
    const cap = Math.max(1, P.blockedCap ?? 16);
    const cw = 23, ch = 21;
    const nD = new Date(ny, nm, 0).getDate(), dow0 = new Date(ny, nm - 1, 1).getDay();
    const wdn = ['日', '一', '二', '三', '四', '五', '六'];
    const days = [];
    for (let i = 0; i < nD; i++) {
      const dw = (dow0 + i) % 7;
      days.push({ dd: String(i + 1).padStart(2, '0'), wd: wdn[dw], he: dw === 0 || dw === 6 });
    }
    const people = this.nbRoster().people;
    const reg = (this.state && this.state.reg) || this.nbSeed();
    const brush = (this.state && this.state.brush) || 'on';
    const brushS = b => `flex:none;padding:6px 12px;font:600 12px/1 'Barlow Condensed','Noto Sans TC',sans-serif;cursor:pointer;user-select:none;border:1px solid ${brush === b ? 'var(--color-accent)' : dim(16)};border-left-width:${b === 'on' ? '1px' : '0'};${brush === b ? 'background:var(--color-accent);color:var(--color-bg);' : ''}`;

    const GT = [
      { tint: 'color-mix(in srgb,var(--color-accent) 12%,transparent)', fg: 'var(--color-accent-800)' },
      { tint: 'color-mix(in srgb,var(--color-accent) 30%,transparent)', fg: 'var(--color-accent-900)' },
      { tint: 'var(--color-accent)', fg: 'var(--color-bg)' },
      { tint: dim(10), fg: dim(65) }
    ];
    const GMETA = [
      { label: '身分組 1', sub: 'PGY1 · PGY2 · R1 · 打工R', capNote: '可值：一般病房' },
      { label: '身分組 2', sub: 'R2 · R3', capNote: '可值：一般病房 · ICU' },
      { label: '身分組 3', sub: 'R4 · R5 · R6', capNote: '可值：一般病房 · ICU · 總值' },
      { label: '身分組 4', sub: 'NP', capNote: '可值：一般病房 · 額度不計' }
    ];

    const count = {}, blocked = {};
    for (let j = 0; j < nD; j++) blocked[j] = 0;
    const allRows = people.map(p => {
      let n = 0;
      const cells = days.map((d, j) => {
        const v = !!reg[p.name + '-' + j];
        if (v) { n++; blocked[j]++; }
        const base = `width:${cw}px;flex:none;height:${ch}px;display:flex;align-items:center;justify-content:center;cursor:pointer;${mono};font-size:9px;border-right:1px solid ${dim(7)};border-bottom:1px solid ${dim(7)};`;
        const s = v
          ? `${base}background:var(--color-accent);color:var(--color-bg)`
          : `${base}${d.he ? `background:${dim(9)};` : ''}color:${dim(0)}`;
        return { t: v ? '×' : '', s, on: () => this.paint(p.name, j) };
      });
      count[p.name] = n;
      const over = n > cap;
      return {
        name: p.name, rank: p.rank, emp: p.emp, cells,
        n: String(n), left: String(Math.max(0, cap - n)),
        rkS: `${mono};font-size:9px;padding:2px 4px;flex:none;background:${GT[p.g].tint};color:${GT[p.g].fg}`,
        nameS: 'font:500 11px/1 "Noto Sans TC",sans-serif;width:56px;flex:none',
        empS: `font:600 8.5px/1 ui-monospace,Menlo,monospace;color:${dim(45)};width:38px;flex:none`,
        nS: `width:34px;flex:none;height:${ch}px;display:flex;align-items:center;justify-content:center;${mono};font-size:9.5px;border-left:1px solid ${dim(14)};border-bottom:1px solid ${dim(7)};${over ? 'background:var(--color-accent);color:var(--color-bg)' : `color:${dim(60)}`}`,
        leftS: `width:34px;flex:none;height:${ch}px;display:flex;align-items:center;justify-content:center;${mono};font-size:9.5px;border-bottom:1px solid ${dim(7)};${over ? 'color:var(--color-accent);' : `color:${dim(45)};`}`
      };
    });
    const nbGroups = GMETA.map((meta, gi) => ({
      label: meta.label, sub: meta.sub, capNote: meta.capNote,
      rows: people.map((p, i) => ({ g: p.g, r: allRows[i] })).filter(x => x.g === gi).map(x => x.r),
      hdS: `display:flex;align-items:center;gap:8px;height:20px;padding-left:4px;margin-top:${gi ? '5px' : '0'};background:${dim(4)};border-bottom:1px solid ${dim(12)}`,
      labS: `${mono};font-size:10px;letter-spacing:.1em;padding:2px 5px;background:${GT[gi].tint};color:${GT[gi].fg}`,
      subS: `font:500 10.5px/1 "Noto Sans TC",sans-serif;color:${dim(60)}`,
      capS: `font-size:10px;color:${dim(42)};margin-left:auto;padding-right:8px`
    }));

    const availOf = (j, fn) => people.filter(p => fn(p) && !reg[p.name + '-' + j]).length;
    const per = days.map((d, j) => {
      const c = availOf(j, p => p.chief), ic = availOf(j, p => p.icu), al = availOf(j, () => true);
      const tag = c === 0 ? '總值無人可用' : c <= 2 ? '總值吃緊' : ic <= 2 ? '總值＋ICU 吃緊' : al <= 5 ? '全區吃緊' : '尚有餘裕';
      const risk = c === 0 ? 3 : c <= 2 ? 2 : (ic <= 2 || al <= 5) ? 1 : 0;
      return { j, d, c, ic, al, tag, risk };
    });
    const nbHead = days.map(d => ({
      dd: d.dd, wd: d.wd,
      s: `width:${cw}px;flex:none;padding:3px 0 4px;text-align:center;${d.he ? `background:${dim(7)};` : ''}border-bottom:1px solid ${dim(14)}`,
      wdS: `display:block;font:600 8.5px/1 'Barlow Condensed',sans-serif;color:${dim(50)}`,
      ddS: "display:block;font:600 11px/1.3 'Barlow Condensed',sans-serif"
    }));
    const nbFoot = per.map(x => ({
      n: String(blocked[x.j]), chief: String(x.c),
      s: `width:${cw}px;flex:none;height:19px;display:flex;align-items:center;justify-content:center;${mono};font-size:9.5px;border-right:1px solid ${dim(7)};border-top:1px solid ${dim(14)};${x.risk >= 2 ? 'background:var(--color-accent);color:var(--color-bg)' : ''}`,
      cS: `width:${cw}px;flex:none;height:17px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;border-right:1px solid ${dim(7)};${x.risk >= 2 ? 'color:var(--color-accent-900);background:color-mix(in srgb,var(--color-accent) 16%,transparent)' : `color:${dim(48)}`}`
    }));
    const nbRisk = per.slice().sort((a, b) => (b.risk - a.risk) || (a.c - b.c) || (a.ic - b.ic)).slice(0, 3).map(x => ({
      date: NM + '/' + x.d.dd, wd: '星期' + x.d.wd, tag: x.tag,
      detail: `總值可用 ${x.c} / 13 人 · 總值＋ICU ${x.ic} / 20 人 · 全區 ${x.al} / 33 人（需 5 人）`,
      s: `display:flex;align-items:center;gap:8px;padding:6px 8px;border:1px solid ${x.risk >= 2 ? 'var(--color-accent)' : dim(12)};${x.risk >= 2 ? 'background:color-mix(in srgb,var(--color-accent) 8%,transparent);' : ''}`,
      tagS: `${mono};margin-left:auto;font-size:10px;padding:2px 5px;${x.risk >= 2 ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`
    }));

    const supply = fn => people.filter(fn).reduce((s, p) => s + (nD - (count[p.name] || 0)), 0);
    const LZ = [
      { label: '總值', sub: 'R4~R6 · 13 人', d: nD * 1, s: supply(p => p.chief) },
      { label: '總值 ＋ ICU', sub: 'R2 以上 · 20 人', d: nD * 2, s: supply(p => p.icu) },
      { label: '全部 5 區', sub: '全員 33 人', d: nD * 5, s: supply(() => true) }
    ];
    const nbLayers = LZ.map(l => {
      const r = l.s / l.d, tight = r < 1.6;
      return {
        label: l.label, sub: l.sub, ratio: r.toFixed(1) + '×',
        note: `需求 ${l.d} 人日 · 可用供給 ${l.s} 人日`,
        ratioS: `${mono};margin-left:auto;font-size:11.5px;color:${tight ? 'var(--color-accent-800)' : dim(55)}`,
        barS: `height:6px;width:${Math.min(100, Math.round(l.d / l.s * 100))}%;background:${tight ? 'var(--color-accent)' : dim(38)}`
      };
    });
    const lowSupply = supply(p => !p.icu), wardDemand = nD * 3, lowR = lowSupply / wardDemand;
    const wardTight = lowR < 1.5;
    const nbWardNote = wardTight
      ? `一般病房 vs 低年級＋NP 剩餘供給 ${lowR.toFixed(1)}×：吃緊，資深會被拉進一般病房，S3「R2/R3 優先 ICU」會被犧牲`
      : `一般病房 vs 低年級＋NP 剩餘供給 ${lowR.toFixed(1)}×：目前寬鬆，S3「R2/R3 優先 ICU」可望滿足`;

    const over = people.filter(p => (count[p.name] || 0) > cap);
    const none = people.filter(p => !(count[p.name] > 0));
    const totalN = people.reduce((s, p) => s + (count[p.name] || 0), 0);
    return {
      nbGroups, nbHead, nbFoot, nbRisk, nbLayers, nbWardNote,
      nbWardS: `margin-top:9px;padding:7px 8px;font-size:10.5px;line-height:1.5;border:1px solid ${wardTight ? 'var(--color-accent)' : dim(12)};${wardTight ? 'background:color-mix(in srgb,var(--color-accent) 8%,transparent);color:var(--color-accent-900);' : `color:${dim(58)};`}`,
      nbTitle: `${ny} 年 ${nm} 月 · 不可排班日登記`,
      nbBrushOn: brushS('on'), nbBrushClear: brushS('clear'),
      onBrushOn: () => this.setBrush('on'), onBrushClear: () => this.setBrush('clear'),
      nbCap: String(cap),
      nbTotalN: String(totalN),
      nbTotalNote: `${people.length} 人中 ${people.length - none.length} 人已登記 · 平均 ${(totalN / people.length).toFixed(1)} 天 · 上限 ${cap} 天`,
      nbOverN: String(over.length),
      nbOverList: over.length ? over.slice(0, 4).map(p => `${p.name} ${count[p.name]} 天`).join('、') + (over.length > 4 ? ' 等' : '') + '（登記時 PUT 回 409，須先清除）' : '無人超過上限',
      nbNoneN: String(none.length),
      nbNoneList: none.length ? none.slice(0, 6).map(p => p.name).join('、') + (none.length > 6 ? ' 等' : '') + '——無通知機制，求解前逐一確認' : '全員皆已登記',
      nbOverS: `${mono};padding:3px 8px;margin-left:auto;${over.length ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`
    };
  }

  // SCREEN 06 人員維護：GET /api/staff（33 人全帶）＋ PATCH/DELETE /api/staff/{id}
  staffVals() {
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const GT = [
      { tint: 'color-mix(in srgb,var(--color-accent) 12%,transparent)', fg: 'var(--color-accent-800)' },
      { tint: 'color-mix(in srgb,var(--color-accent) 30%,transparent)', fg: 'var(--color-accent-900)' },
      { tint: 'var(--color-accent)', fg: 'var(--color-bg)' },
      { tint: dim(12), fg: dim(70) }
    ];
    const ableOf = g => g === 2 ? '一般病房 · ICU · 總值' : g === 1 ? '一般病房 · ICU' : '一般病房';
    const people = this.nbRoster().people;
    const m = this.s01Model();
    const loadOf = {};
    (m.boardCols || []).forEach(c => (c.rows || []).forEach(r => { loadOf[r.name] = r; }));
    const sel = (this.state && this.state.staffSel != null) ? this.state.staffSel : 20;
    const offIdx = { 6: 1, 27: 1 };   // 兩位停用中
    const statusOf = i => offIdx[i] ? '停用' : '在職';
    const staffRows = people.map((p, i) => {
      const on = i === sel, st = statusOf(i);
      return {
        name: p.name, emp: p.emp, rank: p.rank, grp: 'G' + (p.g + 1), able: ableOf(p.g), st,
        on: () => this.setStaff(i),
        s: `display:flex;align-items:center;gap:10px;padding:6px 9px;cursor:pointer;border:1px solid ${on ? 'var(--color-accent)' : 'transparent'};border-bottom-color:${on ? 'var(--color-accent)' : dim(8)};${on ? 'background:color-mix(in srgb,var(--color-accent) 8%,transparent);' : ''}${st === '停用' ? 'opacity:.55;' : ''}`,
        rkS: `${mono};font-size:9.5px;padding:3px 5px;flex:none;width:40px;text-align:center;background:${GT[p.g].tint};color:${GT[p.g].fg}`,
        stS: `${mono};font-size:9.5px;padding:2px 6px;flex:none;margin-left:auto;${st === '在職' ? `border:1px solid ${dim(14)};color:${dim(55)}` : 'background:var(--color-accent-200);color:var(--color-accent-900)'}`
      };
    });
    const p = people[sel] || {};
    const st = statusOf(sel);
    const load = loadOf[p.name];
    const segS = on => `flex:1;text-align:center;padding:7px 0;font:600 12px/1 'Barlow Condensed','Noto Sans TC',sans-serif;cursor:pointer;border:1px solid ${on ? 'var(--color-accent)' : dim(14)};${on ? 'background:var(--color-accent);color:var(--color-bg);' : ''}`;
    const hasDuty = load && load.pts && !String(load.pts).startsWith('0');
    return {
      staffRows,
      staffTotal: `共 ${people.length} 人 · 在職 ${people.length - 2} · 停用 2 · 無分頁`,
      stName: p.name || '', stEmp: p.emp || '', stRank: p.rank || '',
      stGrp: 'G' + ((p.g || 0) + 1),
      stLoad: load ? `本月 ${load.pts}${load.left === '—' ? '' : ' · ' + load.left} · 假日 ${load.hol} 班` : '本月尚無值班',
      stTitle: `${p.name || ''}　${p.emp || ''}`,
      stOnDuty: segS(st === '在職'), stOff: segS(st === '停用'),
      stAbleChips: ableOf(p.g || 0).split(' · ').map(c => ({
        c, s: `${mono};font-size:11px;padding:5px 9px;border:1px solid var(--color-accent);color:var(--color-accent-800);background:color-mix(in srgb,var(--color-accent) 10%,transparent)`
      })),
      stStatusBtn: st === '在職' ? '停用此人員' : '恢復在職',
      stHint: st === '在職' ? '停用後不再納入求解，歷史值班表保留。狀態變更即時生效，沒有生效日。' : '此人員目前不納入求解。',
      stDelNote: hasDuty ? '已有值班紀錄，只能停用；DELETE 會回 409。' : '本月尚無值班紀錄，可硬刪（DELETE /api/staff/{id}）。',
      stDelS: `font-size:10.5px;line-height:1.5;padding:6px 8px;border:1px solid ${hasDuty ? dim(12) : 'var(--color-accent)'};color:${hasDuty ? dim(55) : 'var(--color-accent-900)'}`
    };
  }

  // SCREEN 04 三份變體：同一份輸入、不同權重乘數（ADR-0003），指標與熱圖就地算
  s04Vals() {
    const P = this.props || {};
    const ym = (this.state && this.state.ym) || { y: 2026, m: 9 };
    const key = `${P.pointWeekday || 1}|${P.pointHoliday || 2}|${P.quotaCap || 12}|${P.fairnessPointOn}|${ym.y}-${ym.m}`;
    const G = globalThis;
    if (!G.__s04 || G.__s04Key !== key) { G.__s04 = this.buildS04(ym); G.__s04Key = key; }
    return G.__s04;
  }

  buildS04(ym) {
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const P = this.props || {};
    const wD = Math.max(1, P.pointWeekday ?? 1), wH = Math.max(1, P.pointHoliday ?? 2);
    const cap = Math.max(2, P.quotaCap ?? 12), fairOn = P.fairnessPointOn === true;
    const restGap = 3, npDayCap = 20;
    const nD = new Date(ym.y, ym.m, 0).getDate(), dow0 = new Date(ym.y, ym.m - 1, 1).getDay();
    const he = j => { const dw = (dow0 + j) % 7; return dw === 0 || dw === 6; };
    const roster = this.nbRoster().people;
    const blocked = this.nbSeed();
    const AREAS = [{ id: 'A', t: 'WARD' }, { id: 'B', t: 'WARD' }, { id: 'C', t: 'WARD' }, { id: 'ICU', t: 'ICU' }, { id: '總值', t: 'CHIEF' }];
    const order = ['總值', 'ICU', 'A', 'B', 'C'].map(id => AREAS.find(a => a.id === id));
    const elig = (p, t) => t === 'WARD' ? true : t === 'ICU' ? p.icu : p.chief;

    const solve = (m1, m2) => {
      const people = roster.map((p, i) => Object.assign({}, p, { idx: i, pts: 0, hol: 0, days: 0, last: -99, lastArea: null, byArea: {} }));
      const assign = {};
      for (let j = 0; j < nD; j++) {
        const used = {}, w = he(j) ? wH : wD;
        order.forEach(a => {
          const cand = people.filter(p => {
            if (!elig(p, a.t) || used[p.name] || blocked[p.name + '-' + j]) return false;
            if (p.g === 3) return p.days < npDayCap;
            return j - p.last >= restGap && p.pts + w <= cap;
          });
          let best = null, bestC = Infinity;
          cand.forEach(p => {
            let c = 0;
            if (p.g === 3) c += 600;                                        // S5
            if (a.t === 'ICU' && p.g !== 1) c += 500;                        // S3
            if (a.t === 'WARD' && p.g === 2) c += 500;                       // S4：資深留給總值
            if (p.g === 3 && he(j)) c += 300;                                // S6
            c += (cap - p.pts) * -100 * m1;                                  // S1 剩餘額度
            c += (p.lastArea === a.id ? 0 : 40) * m2;                        // S2 同區延續
            c += p.idx * 0.01;
            if (c < bestC) { bestC = c; best = p; }
          });
          if (best) {
            used[best.name] = 1;
            if (best.g !== 3) best.pts += w;
            best.last = j; best.lastArea = a.id; best.days++;
            if (he(j)) best.hol++;
            best.byArea[a.id] = (best.byArea[a.id] || 0) + 1;
          }
          assign[a.id + '-' + j] = best ? best.name : null;
        });
      }
      const byName = {};
      people.forEach(p => byName[p.name] = p);
      let vac = 0;
      AREAS.forEach(a => { for (let j = 0; j < nD; j++) if (!assign[a.id + '-' + j]) vac++; });
      let fairGap = 0;
      [0, 1, 2].forEach(gi => {
        const rem = people.filter(p => p.g === gi && p.days > 0).map(p => cap - p.pts);
        if (rem.length) fairGap = Math.max(fairGap, Math.max.apply(null, rem) - Math.min.apply(null, rem));
      });
      const cross = people.reduce((s, p) => {
        const mx = Object.keys(p.byArea).reduce((a, k) => Math.max(a, p.byArea[k]), 0);
        return s + (p.days - mx);
      }, 0);
      let icuOk = 0, icuN = 0;
      for (let j = 0; j < nD; j++) { const nm = assign['ICU-' + j]; if (nm) { icuN++; if (byName[nm].g === 1) icuOk++; } }
      const prefPct = icuN ? Math.round(icuOk / icuN * 100) : 0;
      const GC = ['color-mix(in srgb,var(--color-accent) 14%,transparent)', 'color-mix(in srgb,var(--color-accent) 34%,transparent)', 'var(--color-accent-700)', dim(16)];
      const hm = [];
      AREAS.forEach(a => {
        for (let j = 0; j < nD; j++) {
          const nm = assign[a.id + '-' + j];
          hm.push(nm
            ? `background:${GC[byName[nm].g]};border:.5px solid ${dim(6)}`
            : `background:repeating-linear-gradient(45deg,var(--color-accent-300) 0 2px,transparent 2px 4px);border:.5px solid var(--color-accent)`);
        }
      });
      return { vac, fairGap, cross, prefPct, hm };
    };

    const defs = [
      { name: '變體 A', id: 'v-a', stance: '重視公平', m1: 1.5, m2: 0.5, secs: '4.2s', mults: ['S1 ×1.5', 'S2 ×0.5'] },
      { name: '變體 B', id: 'v-b', stance: '重視延續性', m1: 0.5, m2: 1.5, secs: '6.8s', mults: ['S1 ×0.5', 'S2 ×1.5'] },
      { name: '變體 C', id: 'v-c', stance: '平衡', m1: 1, m2: 1, secs: '3.7s', mults: ['全部 ×1'] }
    ];
    const runs = defs.map(d => Object.assign({}, d, solve(d.m1, d.m2)));
    const bestVac = Math.min.apply(null, runs.map(r => r.vac));
    const variants = runs.map(r => {
      const sel = r.id === 'v-c';
      const metrics = [
        { k: '空缺數', v: String(r.vac) + ' 格', p: Math.max(0, 100 - r.vac * 8), warn: r.vac > 0 },
        { k: '額度點數公平', v: 'Δ ' + r.fairGap + ' 點', p: Math.max(0, 100 - r.fairGap * 12) },
        { k: '同區延續', v: r.cross + ' 次跨區', p: Math.max(0, 100 - r.cross * 1.2) },
        { k: '身分區域偏好', v: r.prefPct + '%', p: r.prefPct }
      ];
      if (fairOn) metrics.push({ k: '公平性點數', v: '待填', p: 0 });
      return {
        name: r.name, stance: r.stance, secs: r.secs, sel,
        wrap: `flex:1;position:relative;padding:14px 15px;border:1px solid ${sel ? 'var(--color-accent)' : dim(16)};${sel ? 'background:color-mix(in srgb,var(--color-accent) 6%,transparent);' : ''}`,
        tagS: `${mono};padding:3px 7px;${sel ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`,
        mults: r.mults.map(t => ({ t, s: `${mono};font-size:9.5px;padding:2px 6px;background:${dim(7)};color:${dim(66)}` })),
        hmWrap: `display:grid;grid-template-columns:repeat(${nD},1fr);gap:1px;height:56px`,
        hm: r.hm,
        metrics: metrics.map(mm => ({
          k: mm.k, v: mm.v,
          s: `height:4px;width:${Math.round(mm.p)}%;background:${mm.warn ? 'var(--color-accent-800)' : 'var(--color-accent)'}`,
          vS: `font:600 12px 'Barlow Condensed',sans-serif;width:56px;text-align:right;color:${mm.warn ? 'var(--color-accent-800)' : 'inherit'}`
        })),
        vacNote: r.vac > 0
          ? `空缺 ${r.vac} 格：登記過多時求解器回「空缺最少」的變體，不是整份失敗——空缺待人工指派。`
          : '無空缺：5 區 × ' + nD + ' 日全數填補。',
        vacNoteS: `font-size:10.5px;line-height:1.5;margin-top:9px;padding:6px 8px;border:1px solid ${r.vac > 0 ? 'var(--color-accent)' : dim(12)};color:${r.vac > 0 ? 'var(--color-accent-900)' : dim(55)};${r.vac > 0 ? 'background:color-mix(in srgb,var(--color-accent) 8%,transparent);' : ''}`,
        btn: sel ? '✓ 已選定' : '選定此變體',
        btnS: `flex:1;height:32px;font:600 13px 'Barlow Condensed','Noto Sans TC',sans-serif;cursor:pointer;border:1px solid ${sel ? 'var(--color-accent)' : dim(16)};${sel ? 'background:var(--color-accent);color:var(--color-bg)' : 'background:transparent'}`
      };
    });
    return {
      variants,
      jobId: '求解 #7K3', jobShort: '#7K3',
      jobTime: `3 份變體 · 共 14.7s`,
      warnings: [
        { text: '上月仍是草稿，尚未發布', code: 'PREV_MONTH_DRAFT' },
        { text: '上月尚未發布，月結轉為空——S1 的起始偏移全部為 0', code: 'CARRY_OVER_EMPTY' },
        ...(bestVac > 0 ? [{ text: `所有變體都有空缺（最少 ${bestVac} 格）：不可排班日登記量偏高`, code: 'VACANCY_UNAVOIDABLE' }] : [])
      ].map(w => Object.assign({}, w, {
        badgeS: `${mono};font-size:9px;padding:2px 6px;flex:none;background:var(--color-accent-200);color:var(--color-accent-900)`
      }))
    };
  }

  setView(v) { this.setState({ view: v }); }
  stepDay(n) { this.setState(s => ({ day: Math.max(0, Math.min(29, (s.day == null ? 12 : s.day) + n)) })); }

  renderVals() {
    const m = this.areaModel();
    const view = (this.state && this.state.view) || 'dp';
    const day = Math.max(0, Math.min(29, (this.state && this.state.day != null) ? this.state.day : 12));
    const tab = v => `flex:none;padding:7px 13px;font:600 12.5px/1 'Barlow Condensed','Noto Sans TC',sans-serif;letter-spacing:.02em;cursor:pointer;user-select:none;border:1px solid ${view === v ? 'var(--color-accent)' : 'color-mix(in srgb,var(--color-text) 16%,transparent)'};border-left-width:${v === 'mx' ? '1px' : '0'};${view === v ? 'background:var(--color-accent);color:var(--color-bg);' : 'background:transparent;'}`;
    const show = v => `display:${view === v ? 'block' : 'none'}`;
    return Object.assign({}, m, this.ymVals(), this.annsFor(view), this.staffVals(), this.nbVals(), this.s23Vals(), this.s01Vals(), this.s04Vals(), {
      tabMxS: tab('mx'), tabDpS: tab('dp'), tabDdS: tab('dd'),
      onMx: () => this.setView('mx'), onDp: () => this.setView('dp'), onDd: () => this.setView('dd'),
      showMx: show('mx'), showDp: show('dp'), showDd: show('dd'),
      dayPrev: () => this.stepDay(-1), dayNext: () => this.stepDay(1)
    });
  }

  renderValsLegacy() {
    const P = this.props || {};
    const showV = P.showViolations !== false;
    const nDays = Math.max(7, Math.min(28, P.gridDays || 14));
    const cw = P.compact ? 24 : 29, ch = P.compact ? 22 : 26;
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const cell = (bg, fg, extra) => `display:flex;align-items:center;justify-content:center;position:relative;height:${ch}px;width:${cw}px;${mono};background:${bg};color:${fg};${extra || ''}`;
    const K = {
      D: { t: 'D', bg: 'var(--color-accent-100)', fg: 'var(--color-accent-800)' },
      E: { t: 'E', bg: 'var(--color-accent-300)', fg: 'var(--color-accent-900)' },
      N: { t: 'N', bg: 'var(--color-accent-700)', fg: 'var(--color-bg)' },
      O: { t: '休', bg: 'transparent', fg: 'color-mix(in srgb,var(--color-text) 38%,transparent)' },
      L: { t: '例', bg: 'var(--color-neutral-200)', fg: 'var(--color-neutral-700)' },
      R: { t: '假', bg: 'repeating-linear-gradient(45deg,var(--color-neutral-300) 0 3px,transparent 3px 6px)', fg: 'var(--color-neutral-800)' }
    };
    const wd = ['日', '一', '二', '三', '四', '五', '六'];
    const days = [], dow0 = 2;
    for (let i = 0; i < nDays; i++) {
      const dw = (dow0 + i) % 7, we = dw === 0 || dw === 6;
      days.push({
        dd: String(i + 1).padStart(2, '0'), wd: wd[dw], we,
        th: `width:${cw}px;padding:2px 0 4px;text-align:center;${we ? 'background:color-mix(in srgb,var(--color-text) 6%,transparent);' : ''}`
      });
    }
    const nurses = [
      ['王淑芬', 'N4'], ['李佩君', 'N3'], ['陳雅婷', 'N3'], ['張家瑜', 'N2'], ['林怡君', 'N4'],
      ['黃思涵', 'N2'], ['吳孟儒', 'N1'], ['蔡宜玲', 'N3'], ['鄭曉琪', 'N2'], ['劉巧薇', 'N1']
    ];
    const seq = ['D', 'D', 'D', 'O', 'E', 'E', 'E', 'O', 'N', 'N', 'N', 'O', 'L', 'D'];
    const leaves = { '2-9': 1, '2-10': 1, '5-3': 1, '8-12': 1 };
    const vio = { '4-6': '班距', '6-11': '連上 7', '9-2': '例假' };
    const rows = nurses.map(([name, level], i) => {
      let nc = 0, hrs = 0;
      const cells = days.map((d, j) => {
        let code = seq[(j + i * 3) % seq.length];
        if (leaves[i + '-' + j]) code = 'R';
        const k = K[code];
        if (code === 'N') nc++;
        if ('DEN'.includes(code)) hrs += 8;
        const warn = showV && vio[i + '-' + j];
        return {
          t: k.t, m: warn ? '!' : '',
          s: cell(k.bg, k.fg, warn ? 'outline:1.5px solid var(--color-text);outline-offset:-1.5px;' : ''),
          ms: warn ? `position:absolute;top:0;right:1px;font:700 8px/1 Barlow,sans-serif;color:var(--color-text)` : 'display:none'
        };
      });
      const over = hrs > 8 * Math.round(nDays * 0.72);
      return { name, level, cells, n: nc, h: hrs, hs: over ? 'color:var(--color-accent);text-decoration:underline' : '' };
    });
    const cover = days.map((d, j) => {
      const short = j === 6 || j === 11;
      return {
        t: short ? '9' : '10',
        s: cell(short ? 'repeating-linear-gradient(45deg,var(--color-accent-300) 0 3px,transparent 3px 6px)' : 'transparent',
          short ? 'var(--color-accent-900)' : 'color-mix(in srgb,var(--color-text) 45%,transparent)',
          'height:20px;border:1px solid var(--color-divider);')
      };
    });
    const legend = [['D', '白班 08–16'], ['E', '小夜 16–24'], ['N', '大夜 00–08'], ['O', '休'], ['L', '例假'], ['R', '請假']]
      .map(([c, label]) => ({ t: K[c].t, label, s: cell(K[c].bg, K[c].fg, 'height:17px;width:22px;font-size:10px;') }));
    const bar = (pct, h, col) => `height:${h}px;width:${pct}%;background:${col || 'var(--color-accent)'}`;
    const scoreBars = [
      ['人力覆蓋', '100%', 100], ['偏好滿足', '82%', 82], ['公平性', '0.91', 91], ['連續性', '76%', 76]
    ].map(([label, val, p]) => ({ label, val, s: bar(p, 3) }));
    const issues = [
      { kind: '軟', title: '週末班分配不均：黃思涵 4 / 吳孟儒 1', who: 'S3 週末公平 · 扣 6.0', act: '重排' },
      { kind: '軟', title: '花花班：張家瑜 09/04 D → 09/05 N', who: 'S2 班別跳動 · 扣 3.5', act: '調整' },
      { kind: '軟', title: '偏好未滿足：林怡君 申請避開大夜', who: 'S1 個人偏好 · 扣 2.0', act: '檢視' },
      { kind: '註', title: '09/07、09/12 白班僅 9 人（下限 9）', who: '無違規，但無備援', act: '通知' }
    ].map(v => ({ ...v, badge: `${mono};padding:3px 5px;border:1px solid var(--color-divider);color:var(--color-accent-800);background:var(--color-accent-100)` }));
    const setNav = ['病房與班別', '人力下限', '硬約束', '軟約束權重', '假別與額度', '求解器', '通知'].map((t, i) => ({
      t, s: `padding:7px 9px;font-size:12.5px;${i === 2 ? 'background:color-mix(in srgb,var(--color-accent) 13%,transparent);color:var(--color-accent-800);font-weight:500;border-left:2px solid var(--color-accent)' : 'border-left:2px solid transparent;color:color-mix(in srgb,var(--color-text) 70%,transparent)'}`
    }));
    const hard = [
      ['H1', '每班最低人力', ['白 9', '小夜 6', '大夜 5'], '院內人力標準', '啟用'],
      ['H2', '一人一日僅一班', ['—'], '勞基法', '鎖定'],
      ['H3', '班間間隔', ['≥ 11 小時'], '勞基法 §34', '鎖定'],
      ['H4', '連續上班天數', ['≤ 6 天'], '勞基法 §36', '鎖定'],
      ['H5', '每 7 日至少 1 例假', ['1 / 7 天'], '勞基法 §36', '鎖定'],
      ['H6', '已核准假別不得排班', ['所有假別'], '院內規則', '啟用'],
      ['H7', '每班至少 1 位資深 (N3+)', ['N3 以上 ≥ 1'], '病房規則', '啟用']
    ].map(([id, name, params, src, state]) => ({
      id, name, params, src, state,
      badge: `${mono};padding:3px 8px;${state === '鎖定' ? 'border:1px solid var(--color-divider);color:color-mix(in srgb,var(--color-text) 60%,transparent)' : 'background:var(--color-accent);color:var(--color-bg)'}`
    }));
    const soft = [
      ['S1', '個人偏好班別', 80, '護理師登記的偏好／避開班別'],
      ['S2', '避免花花班', 70, '同一人班別跳動的次數'],
      ['S3', '週末班公平', 65, '以人為單位的週末班標準差'],
      ['S4', '大夜次數平均', 60, '每月大夜數的最大－最小差'],
      ['S5', '連假需求', 45, '休假日盡量相連（2 天以上）'],
      ['S6', '搭配偏好', 30, '新人與 preceptor 同班'],
      ['S7', '加班時數上限', 55, '超過 176h 開始扣分'],
      ['S8', '固定班型申請', 40, '長期夜班 / 固定白班的申請']
    ].map(([id, name, w, note]) => ({
      id, name, w, note,
      bar: `position:absolute;left:0;top:0;height:6px;width:${w}%;background:var(--color-accent)`,
      knob: `position:absolute;top:-3px;left:calc(${w}% - 5px);width:10px;height:12px;background:var(--color-bg);border:1.5px solid var(--color-accent)`
    }));
    const hm = (seed, dark) => {
      const out = [];
      for (let i = 0; i < 96; i++) {
        const v = (Math.sin(i * seed) * 10000) % 1;
        const a = Math.abs(v);
        const c = a > 0.72 ? 'var(--color-accent-700)' : a > 0.46 ? 'var(--color-accent-400)' : a > 0.22 ? 'var(--color-accent-200)' : 'transparent';
        out.push(`background:${c};border:.5px solid color-mix(in srgb,var(--color-text) 7%,transparent)`);
      }
      return out;
    };
    const vDefs = [
      ['變體 A', '人力優先', 86.2, '把人力備援拉滿：每班多 1 人備援，代價是加班時數集中在 4 位資深護理師。', false, [['硬違規', '0', 100], ['偏好滿足', '74%', 74], ['公平性', '0.83', 83], ['加班', '38h', 62], ['求解', '4.1s', 40]], 2.1],
      ['變體 B', '推薦', 88.4, '整體最平衡：偏好滿足與公平性同時在前 20%，只有 3 個軟約束扣分。', true, [['硬違規', '0', 100], ['偏好滿足', '82%', 82], ['公平性', '0.91', 91], ['加班', '22h', 82], ['求解', '12.4s', 78]], 1.3],
      ['變體 C', '公平優先', 84.7, '大夜與週末班攤到最平，但 6 人的偏好被犧牲，花花班變多。', false, [['硬違規', '0', 100], ['偏好滿足', '61%', 61], ['公平性', '0.96', 96], ['加班', '18h', 88], ['求解', '9.8s', 60]], 3.4]
    ];
    const variants = vDefs.map(([name, tag, score, desc, sel, ms, gap], i) => ({
      name, tag, score, desc, sel,
      wrap: `flex:1;position:relative;padding:14px 15px;border:1px solid ${sel ? 'var(--color-accent)' : 'var(--color-divider)'};${sel ? 'background:color-mix(in srgb,var(--color-accent) 6%,transparent);' : ''}`,
      tagS: `${mono};padding:3px 7px;${sel ? 'background:var(--color-accent);color:var(--color-bg)' : 'border:1px solid var(--color-divider);color:color-mix(in srgb,var(--color-text) 60%,transparent)'}`,
      hmWrap: 'display:grid;grid-template-columns:repeat(16,1fr);gap:1px;height:66px',
      hm: hm(1.7 + i * 0.9),
      metrics: ms.map(([k, v, p]) => ({ k, v, s: bar(p, 4) })),
      btn: sel ? '✓ 已選定' : '選定此變體',
      btnS: sel
        ? "flex:1;height:32px;border:1px solid var(--color-accent);background:var(--color-accent);color:var(--color-bg);font:600 13px 'Barlow Condensed','Noto Sans TC',sans-serif;cursor:pointer"
        : "flex:1;height:32px;border:1px solid var(--color-divider);background:transparent;font:600 13px 'Barlow Condensed','Noto Sans TC',sans-serif;cursor:pointer"
    }));
    const myWeek = [
      ['09/07', '一', 'N', '00:00 – 08:00', ''], ['09/08', '二', 'O', '休', ''], ['09/09', '三', 'D', '08:00 – 16:00', ''],
      ['09/10', '四', 'D', '08:00 – 16:00', '代 李佩君'], ['09/11', '五', 'E', '16:00 – 24:00', ''],
      ['09/12', '六', 'L', '例假', ''], ['09/13', '日', 'E', '16:00 – 24:00', '加班 +2h']
    ].map(([date, w, c, time, note]) => ({
      date, wd: w, code: K[c].t, time, note,
      chip: cell(K[c].bg, K[c].fg, 'height:19px;width:24px;font-size:10.5px;flex:none;')
    }));
    const precheck = [
      ['✓', '09/18 白班仍符合人力下限（9 → 9），可核准'],
      ['!', '09/19 為你的例假前一天，核准後需重排 1 人'],
      ['✓', '年假餘額足夠：4.5 天 → 2.5 天']
    ].map(([mark, text]) => ({
      mark, text,
      icon: `${mono};width:14px;height:14px;display:flex;align-items:center;justify-content:center;flex:none;margin-top:1px;${mark === '✓' ? 'color:var(--color-bg);background:var(--color-accent)' : 'color:var(--color-text);border:1px solid var(--color-text)'}`
    }));
    const leaveLog = [
      ['08/22', '事假', '林護理長', '已核准'], ['08/05', '年假', '林護理長', '已核准'], ['07/28', '年假', '—', '已駁回']
    ].map(([date, type, by, state]) => ({
      date, type, by, state,
      badge: `${mono};margin-left:auto;padding:2px 7px;${state === '已核准' ? 'background:var(--color-accent-100);color:var(--color-accent-800)' : 'border:1px solid var(--color-divider);color:color-mix(in srgb,var(--color-text) 55%,transparent)'}`
    }));
    const pool = [
      ['王淑芬', 'N4 · 本月 152h · 大夜 5', '最佳', 1], ['蔡宜玲', 'N3 · 本月 160h · 大夜 6', '可', 1],
      ['吳孟儒', 'N1 · 本月 144h · 需帶班', '可', 1], ['張家瑜', 'N2 · 本月 168h · 大夜 7', '勉強', 0],
      ['鄭曉琪', 'N2 · 09/08 已排小夜', '違規', -1], ['劉巧薇', 'N1 · 連續上班 6 天', '違規', -1]
    ].map(([name, meta, fit, ok]) => ({
      name, meta, fit,
      mark: ok === 1 ? 'background:var(--color-accent)' : ok === 0 ? 'background:var(--color-accent-300)' : 'background:repeating-linear-gradient(45deg,var(--color-neutral-400) 0 2px,transparent 2px 4px)'
    }));
    const shiftRows = [['白班 D', '08–16'], ['小夜 E', '16–24'], ['大夜 N', '00–08']].map(([name, time]) => ({ name, time }));
    const colDefs = [
      ['09/07 一', false, [['9/9', 0, ['王淑芬', '李佩君', '＋7']], ['6/6', 0, ['林怡君', '黃思涵', '＋4']], ['5/5', 0, ['陳雅婷', '吳孟儒', '＋3']]]],
      ['09/08 二', false, [['9/9', 0, ['蔡宜玲', '張家瑜', '＋7']], ['5/6', 1, ['鄭曉琪', '劉巧薇', '＋3']], ['5/5', 0, ['王淑芬', '林怡君', '＋3']]]],
      ['09/09 三', false, [['8/9', 1, ['李佩君', '陳雅婷', '＋6']], ['6/6', 0, ['黃思涵', '蔡宜玲', '＋4']], ['5/5', 0, ['張家瑜', '吳孟儒', '＋3']]]],
      ['09/10 四', false, [['9/9', 0, ['王淑芬', '鄭曉琪', '＋7']], ['6/6', 0, ['劉巧薇', '林怡君', '＋4']], ['4/5', 1, ['李佩君', '＋3']]]],
      ['09/11 五', true, [['9/9', 0, ['陳雅婷', '黃思涵', '＋7']], ['6/6', 0, ['張家瑜', '蔡宜玲', '＋4']], ['5/5', 0, ['吳孟儒', '王淑芬', '＋3']]]]
    ];
    const cols = colDefs.map(([day, we, slots]) => ({
      day,
      head: `height:26px;display:flex;align-items:center;justify-content:center;font:600 12px 'Barlow Condensed','Noto Sans TC',sans-serif;border-bottom:1px solid var(--color-divider);${we ? 'background:color-mix(in srgb,var(--color-text) 6%,transparent);' : ''}`,
      slots: slots.map(([count, short, people]) => ({
        count, warn: short ? '缺 1' : '',
        box: `height:112px;padding:6px 7px;border:1px solid ${short ? 'var(--color-accent)' : 'var(--color-divider)'};${short ? 'background:repeating-linear-gradient(45deg,color-mix(in srgb,var(--color-accent) 10%,transparent) 0 4px,transparent 4px 8px);' : ''}`,
        gauge: `width:16px;height:4px;background:${short ? 'repeating-linear-gradient(90deg,var(--color-accent) 0 3px,transparent 3px 4px)' : 'var(--color-accent)'}`,
        warnS: short ? `margin-left:auto;${mono};color:var(--color-accent-800);padding:1px 4px;background:var(--color-accent-200)` : 'display:none',
        people: people.map(t => ({
          t,
          s: `font-size:10.5px;line-height:1.45;padding:2px 5px;margin-bottom:2px;border:1px solid ${t.startsWith('＋') ? 'transparent' : 'var(--color-divider)'};${t.startsWith('＋') ? 'color:color-mix(in srgb,var(--color-text) 50%,transparent);padding-left:0' : 'background:var(--color-bg)'}`
        }))
      }))
    }));
    const mWd = wd.slice(1).concat(wd[0]);
    const mCells = [];
    for (let i = 0; i < 35; i++) {
      const dn = i;
      const inM = dn >= 1 && dn <= 30;
      const code = inM ? seq[(dn + 6) % seq.length] : null;
      const k = code ? K[code] : null;
      const today = dn === 7;
      mCells.push({
        d: inM ? dn : '', t: k ? k.t : '',
        s: `height:34px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1px;${k ? `background:${k.bg};color:${k.fg};` : ''}border:1px solid ${today ? 'var(--color-text)' : 'color-mix(in srgb,var(--color-text) 8%,transparent)'};${inM ? '' : 'opacity:.25;'}`
      });
    }
    const market = [
      ['E', '09/15 小夜', '張家瑜 想換', '想換成白班 · 你當日休假', '接手'],
      ['N', '09/21 大夜', '徵求代班', '護理長公告 · +2h 加班費', '應徵'],
      ['D', '09/24 白班', '你的班', '已有 1 人想接手', '審視']
    ].map(([c, date, who, note, act]) => ({
      code: K[c].t, date, who, note, act,
      chip: cell(K[c].bg, K[c].fg, 'height:22px;width:22px;flex:none;'),
      btn: `${mono};padding:5px 8px;border:1px solid var(--color-accent);color:var(--color-accent)`
    }));
    const tabs = ['班表', '請假', '換班', '我'].map((t, i) => ({
      t, s: `flex:1;text-align:center;padding:11px 0;font-size:11.5px;${i === 0 ? 'color:var(--color-accent);border-top:2px solid var(--color-accent);font-weight:500' : 'color:color-mix(in srgb,var(--color-text) 50%,transparent);border-top:2px solid transparent'}`
    }));
    const leaveKinds = [['年假', '餘 4.5d'], ['事假', '不計薪'], ['病假', '需證明'], ['補休', '餘 8h']].map(([t, bal], i) => ({
      t, bal,
      s: `padding:10px 11px;font:600 14px 'Barlow Condensed','Noto Sans TC',sans-serif;border:1px solid ${i === 0 ? 'var(--color-accent)' : 'var(--color-divider)'};${i === 0 ? 'background:color-mix(in srgb,var(--color-accent) 12%,transparent);' : ''}`
    }));
    const pickCells = [];
    for (let i = 0; i < 21; i++) {
      const d = 8 + i;
      const sel = d === 18 || d === 19;
      const sched = [8, 9, 12, 13, 15, 16, 20, 22, 23, 26].includes(d);
      const full = d === 25;
      pickCells.push({
        d,
        s: `height:30px;display:flex;align-items:center;justify-content:center;${mono};font-size:11px;border:1px solid ${sel ? 'var(--color-accent)' : 'color-mix(in srgb,var(--color-text) 10%,transparent)'};${sel ? 'background:var(--color-accent);color:var(--color-bg);' : sched ? 'background:repeating-linear-gradient(45deg,var(--color-neutral-300) 0 3px,transparent 3px 6px);' : full ? 'color:color-mix(in srgb,var(--color-text) 25%,transparent);' : ''}`
      });
    }
    return Object.assign({ days, rows, cover, legend, scoreBars, issues, setNav, hard, soft, variants, myWeek, precheck, leaveLog, pool, shiftRows, cols, mWd, mCells, market, tabs, leaveKinds, pickCells }, this.areaModel());
  }

  // 整份班表只算一次，快取在 global 上；每次 render 都回傳同一份物件
  areaModel() {
    const P = this.props || {};
    const ym = (this.state && this.state.ym) || { y: 2026, m: 9 };
    const key = `${P.pointWeekday || 1}|${P.pointHoliday || 2}|${ym.y}-${ym.m}`;
    const G = globalThis;
    if (!G.__nspModel || G.__nspKey !== key) { G.__nspModel = this.buildModel(ym); G.__nspKey = key; }
    return G.__nspModel;
  }

  buildModel(ym) {
    const YM = ym || { y: 2026, m: 9 };
    const MM = String(YM.m).padStart(2, '0');
    const P = this.props || {};
    const wD = Math.max(1, P.pointWeekday ?? 1), wH = Math.max(1, P.pointHoliday ?? 2);
    const mono = "font:600 11px/1 'Barlow Condensed',sans-serif";
    const dim = a => `color-mix(in srgb,var(--color-text) ${a}%,transparent)`;
    const wdn = ['日', '一', '二', '三', '四', '五', '六'];
    const nD = new Date(YM.y, YM.m, 0).getDate(), dow0 = new Date(YM.y, YM.m - 1, 1).getDay();
    const cw = 26, chh = 24;
    const a2days = [];
    for (let i = 0; i < nD; i++) {
      const dw = (dow0 + i) % 7, he = dw === 0 || dw === 6;
      a2days.push({
        dd: String(i + 1).padStart(2, '0'), wd: wdn[dw], he, p: he ? wH : wD,
        th: `width:${cw}px;flex:none;padding:3px 0 4px;text-align:center;${he ? `background:${dim(7)};` : ''}`,
        wdS: `display:block;font:600 9px/1 'Barlow Condensed',sans-serif;color:${dim(50)}`,
        ddS: `display:block;font:600 11px/1.35 'Barlow Condensed',sans-serif`,
        pS: `display:block;${mono};font-size:8px;color:${he ? 'var(--color-accent-700)' : dim(32)}`
      });
    }
    const restGap = 3; // 值班後須間隔 2 天 → 同一人最快每 3 天一班（值休休）
    const LV = {
      N1: { tint: 'var(--color-neutral-200)', fg: 'var(--color-neutral-800)', cap: 14 },
      N2: { tint: 'var(--color-accent-100)', fg: 'var(--color-accent-800)', cap: 14 },
      N3: { tint: 'var(--color-accent-300)', fg: 'var(--color-accent-900)', cap: 14 },
      N4: { tint: 'var(--color-accent-700)', fg: 'var(--color-bg)', cap: 15 }
    };
    const owner = { A: 'N2', B: 'N1', C: 'N3', D: 'N4', E: 'N4' };
    const lvIdx = { N1: 1, N2: 2, N3: 3, N4: 4 };
    const canWork = (lv, t) => { const o = lvIdx[owner[t]]; return o === lvIdx[lv] || o === lvIdx[lv] - 1; };
    const names = {
      N1: ['洪于婷', '簡佑軒', '高柏勳', '賴思妤', '邱柏睿', '周妍希', '何雨潔', '郭宥安', '童俊宏'],
      N2: ['鄭曉琪', '許庭語', '曾柏翰', '林宜臻', '王詩涵', '李冠霖', '陳建宏', '張又瑄', '黃郁婷', '吳承翰', '劉家豪', '蔡侑霖', '楊子萱', '謝欣穎', '溫雅琪'],
      N3: ['李佩君', '陳雅婷', '蔡宜玲', '賴宥辰', '邱怡安', '郭品妤', '周昱安', '何冠廷', '劉巧薇', '吳孟儒', '張家瑜', '黃思涵'],
      N4: ['王淑芬', '林怡君', '洪佩琪', '簡孟蓉', '高詩涵', '陳美玲', '李雅雯', '張惠婷', '黃秀珠', '吳佳蓉', '劉素貞', '蔡明珠', '鄭麗華', '許春美', '曾麗美']
    };
    const people = [], byName = {};
    ['N1', 'N2', 'N3', 'N4'].forEach(lv => names[lv].forEach((n, i) => {
      const p = { name: n, ab: n.slice(0, 2), lv, cap: LV[lv].cap, pts: 0, hol: 0, idx: i, days: [], last: -99 };
      people.push(p); byName[n] = p;
    }));
    const types = [
      { c: 'A', n: '病房照護區', m: 4 }, { c: 'B', n: '門診支援', m: 2 },
      { c: 'C', n: '急診檢傷', m: 3 }, { c: 'D', n: '開刀房', m: 2 }, { c: 'E', n: '加護病房', m: 2 }
    ];
    const areas = [];
    types.forEach(t => { for (let k = 1; k <= t.m; k++) areas.push({ id: t.c + k, type: t.c }); });
    const forceVac = { 'A4-12': 1, 'C1-25': 1 };
    const holCap = 8;
    const poolSize = {};
    types.forEach(t => poolSize[t.c] = people.filter(p => canWork(p.lv, t.c)).length);
    // 最受限的區域先排：D/E 只有 N4 可值，若讓 C 先抽走 N4 就會產生空缺
    const order = areas.map((a, i) => ({ a, i })).sort((x, y) => poolSize[x.a.type] - poolSize[y.a.type] || x.i - y.i).map(o => o.a);
    const own = (p, t) => lvIdx[owner[t]] === lvIdx[p.lv] ? 0 : 1;
    const assign = {};
    for (let j = 0; j < nD; j++) {
      const used = {}, he = a2days[j].he, w = he ? wH : wD;
      order.forEach(a => {
        const key = a.id + '-' + j;
        if (forceVac[key]) { assign[key] = null; return; }
        const cand = people.filter(p => canWork(p.lv, a.type) && !used[p.name] && p.pts + w <= p.cap && !(he && p.hol >= holCap) && j - p.last >= restGap);
        let pick = null;
        // 休息間隔剛好滿足時讓同一人回到同一區域（值休休循環），但每三個循環放開一次以輪替人力
        const prev = assign[a.id + '-' + (j - restGap)];
        if (prev && Math.floor(j / restGap) % 3 !== 0) pick = cand.find(p => p.name === prev) || null;
        // 輪替循環：避免又抽到同一人，讓大家有機會跨區域（S7）
        if (!pick) {
          cand.sort((x, y) => own(x, a.type) - own(y, a.type) || (x.name === prev ? 1 : 0) - (y.name === prev ? 1 : 0) || (x.pts / x.cap) - (y.pts / y.cap) || x.idx - y.idx);
          pick = cand[0] || null;
        }
        if (pick) { used[pick.name] = 1; pick.pts += w; pick.last = j; if (he) pick.hol++; pick.days.push({ j, area: a.id, he }); }
        assign[key] = pick ? pick.name : null;
      });
    }
    const cellS = (lv, he) => `width:${cw}px;height:${chh}px;flex:none;display:flex;align-items:center;justify-content:center;font:500 9.5px/1 "Noto Sans TC",sans-serif;background:${LV[lv].tint};color:${LV[lv].fg};border-right:1px solid ${he ? dim(20) : dim(7)};border-bottom:1px solid ${dim(7)};`;
    const vacS = `width:${cw}px;height:${chh}px;flex:none;display:flex;align-items:center;justify-content:center;${mono};font-size:9.5px;color:var(--color-accent-900);background:repeating-linear-gradient(45deg,var(--color-accent-300) 0 3px,transparent 3px 6px);border:1.5px solid var(--color-accent);border-bottom-color:var(--color-accent)`;
    const supOf = t => lvIdx[owner[t]] < 4 ? 'N' + (lvIdx[owner[t]] + 1) : null;
    const a2groups = types.map(t => ({
      code: t.c, name: t.n,
      meta: `${t.m} 區 · 每區每日 1 人 · 主責 ${owner[t.c]}${supOf(t.c) ? `（含 ${supOf(t.c)} 支援）` : '（無上級可支援）'}`,
      badge: `${mono};padding:2px 6px;background:${LV[owner[t.c]].tint};color:${LV[owner[t.c]].fg}`,
      rows: areas.filter(a => a.type === t.c).map(a => {
        let filled = 0;
        const cells = a2days.map((d, j) => {
          const nm = assign[a.id + '-' + j];
          if (!nm) return { t: '缺', nm: '', s: vacS };
          filled++;
          return { t: byName[nm].ab, nm, s: cellS(byName[nm].lv, d.he) };
        });
        return { id: a.id, cells, stat: filled + '/' + nD, statS: `width:44px;flex:none;text-align:right;${mono};font-size:10px;color:${filled < nD ? 'var(--color-accent)' : dim(45)}` };
      })
    }));
    const fill = a2days.map((d, j) => {
      const miss = areas.filter(a => !assign[a.id + '-' + j]).length;
      return {
        t: miss ? String(areas.length - miss) : String(areas.length),
        s: `width:${cw}px;height:18px;flex:none;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;${miss ? 'background:var(--color-accent-200);color:var(--color-accent-900);' : `color:${dim(40)};`}border-right:1px solid ${dim(6)}`
      };
    });
    const a2legend = ['N1', 'N2', 'N3', 'N4'].map(lv => ({
      t: lv, label: `主責 ${types.filter(t => owner[t.c] === lv).map(t => t.c).join('/') || '—'}`,
      s: `${mono};font-size:9px;padding:3px 6px;background:${LV[lv].tint};color:${LV[lv].fg}`
    }));
    const board = people.map(p => {
      const pct = Math.min(100, Math.round(p.pts / p.cap * 100));
      return {
        name: p.name, lv: p.lv, pts: p.pts, cap: p.cap, hol: p.hol, pct: pct + '%',
        on: () => this.hl(p.name), off: () => this.hl(null),
        lvS: `${mono};font-size:9px;padding:2px 4px;flex:none;background:${LV[p.lv].tint};color:${LV[p.lv].fg}`,
        bar: `height:5px;width:${pct}%;background:${pct >= 95 ? 'repeating-linear-gradient(45deg,var(--color-accent-800) 0 3px,var(--color-accent-400) 3px 6px)' : 'var(--color-accent)'}`
      };
    });
    const util = ['N1', 'N2', 'N3', 'N4'].map(lv => {
      const ps = people.filter(p => p.lv === lv);
      const u = ps.reduce((s, p) => s + p.pts, 0), c = ps.reduce((s, p) => s + p.cap, 0);
      const pct = Math.round(u / c * 100);
      return { lv, txt: `${u} / ${c} 點`, pct: pct + '%', s: `height:4px;width:${pct}%;background:${pct >= 92 ? 'var(--color-accent-800)' : 'var(--color-accent)'}` };
    });
    const hot = people.slice().sort((a, b) => b.pts / b.cap - a.pts / a.cap)[0];
    const holSort = people.slice().sort((a, b) => b.hol - a.hol);
    const n4 = util[3];
    const vacList = [];
    areas.forEach(a => a2days.forEach((d, j) => { if (!assign[a.id + '-' + j]) vacList.push(`${a.id} ${MM}/${d.dd}`); }));
    const a2alerts = [
      ['硬', `${vacList.length} 個區域-日無合格人員可排：${vacList.slice(0, 3).join('、')}`, 'H1 不可空缺 · 需人工指派或臨時放寬資格', '處理'],
      ['警', `N4 職級容量利用率 ${n4.pct}（${n4.txt}）`, `D/E 共 ${areas.filter(a => a.type === 'D' || a.type === 'E').length} 區需 4 人/日，${people.filter(p => p.lv === 'N4').length} 位 N4 在 H8 下每日僅約 ${Math.floor(people.filter(p => p.lv === 'N4').length / restGap)} 人可用 · 資格矩陣禁止 N3 支援，要放寬需增聘或改分類`, '建議'],
      ['軟', `點數接近上限：${hot.name} ${hot.pts}/${hot.cap} 點`, 'S1 點數平均 · 下半月請優先排他人', '調整'],
      ['警', `H8 休息間隔：每人每月最多值 ${Math.floor(nD / restGap)} 班`, `${areas.length} 區 × ${nD} 日 = ${areas.length * nD} 班 → 至少需 ${Math.ceil(areas.length * nD / Math.floor(nD / restGap))} 人，目前 ${people.length} 人`, '檢視'],
      ['軟', `假日班不均：${holSort[0].name} ${holSort[0].hol} 班 / ${holSort[holSort.length - 1].name} ${holSort[holSort.length - 1].hol} 班`, 'S2 假日公平 · 扣 5.5', '重排']
    ].map(([kind, title, who, act]) => ({
      kind, title, who, act,
      badge: `${mono};padding:3px 5px;flex:none;${kind === '硬' ? 'background:var(--color-accent);color:var(--color-bg)' : kind === '警' ? 'background:var(--color-accent-200);color:var(--color-accent-900)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`
    }));
    const areaSpec = types.map(t => ({
      code: t.c, name: t.n, m: String(t.m),
      list: areas.filter(a => a.type === t.c).map(a => a.id),
      ownerLv: owner[t.c], support: supOf(t.c) || '無',
      badge: `${mono};padding:2px 6px;background:${LV[owner[t.c]].tint};color:${LV[owner[t.c]].fg}`
    }));
    const ptRule = ['N1', 'N2', 'N3', 'N4'].map(lv => {
      const own = types.filter(t => owner[t.c] === lv).map(t => t.c);
      const able = types.filter(t => canWork(lv, t.c));
      return {
        lv, own: own.join(' / ') || '—',
        able: able.map(t => t.c).join(' / '),
        areaN: String(able.reduce((s, t) => s + t.m, 0)),
        cap: String(LV[lv].cap),
        lvS: `${mono};padding:2px 6px;background:${LV[lv].tint};color:${LV[lv].fg}`
      };
    });
    const elig = ['N4', 'N3', 'N2', 'N1'].map(lv => ({
      lv,
      lvS: `${mono};padding:2px 6px;background:${LV[lv].tint};color:${LV[lv].fg}`,
      cells: types.map(t => {
        const own = owner[t.c] === lv, sup = !own && canWork(lv, t.c);
        return {
          t: own ? '主責' : sup ? '支援' : '—',
          s: `flex:1;height:30px;display:flex;align-items:center;justify-content:center;font:600 11px 'Barlow Condensed','Noto Sans TC',sans-serif;border:1px solid ${own || sup ? 'var(--color-accent)' : dim(10)};${own ? 'background:var(--color-accent);color:var(--color-bg);' : sup ? 'background:color-mix(in srgb,var(--color-accent) 14%,transparent);color:var(--color-accent-900);' : `color:${dim(30)};`}`
        };
      })
    }));
    const hard2 = [
      ['H1', '每個區域每日必須有 1 人', [`${areas.length} 區 × ${nD} 日`], '不可空缺'],
      ['H2', '一人一日最多一個區域', ['—'], '不可重複'],
      ['H3', '資格限制：本職級 a 與 a−1 的區域', ['自動由資格矩陣推導'], '鎖定'],
      ['H4', '月點數不得超過職級上限', [`平日 ${wD} 點`, `假日 ${wH} 點`], '鎖定'],
      ['H5', '已核准假別不排班', ['所有假別'], '啟用'],
      ['H6', '每 7 日值班上限', [`≤ ${Math.floor(7 / restGap) + 1} 班 / 7 日`], '啟用'],
      ['H7', '每人每月假日班上限', [`≤ ${holCap} 班`], '啟用'],
      ['H8', '值班後須間隔 2 天（值休休）', [`同一人間隔 ≥ ${restGap} 天`], '鎖定']
    ].map(([id, name, params, state]) => ({
      id, name, params, state,
      badge: `${mono};letter-spacing:.06em;width:52px;text-align:right;color:${state === '鎖定' ? dim(45) : 'var(--color-accent-700)'}`
    }));
    const soft2 = [
      ['S1', '點數平均', 85, '同職級點數標準差最小'],
      ['S2', '假日班公平', 75, '每人假日班數差距 ≤ 1'],
      ['S3', '區域熟悉度', 60, '同一人盡量固定同一區域'],
      ['S4', '避免連續兩個週末', 55, '同一人相鄰兩個週末都被排到會扣分'],
      ['S5', '偏好區域', 50, '個人登記的偏好／避開區域'],
      ['S6', '偏好日期', 45, '非正式請假的軟性避開'],
      ['S7', '跨區域輪替', 30, '培訓需求：每季至少 2 種區域'],
      ['S8', '高職級保留備援', 40, 'N4 不排滿，留機動人力']
    ].map(([id, name, w, note]) => ({
      id, name, w, note,
      bar: `position:absolute;left:0;top:0;height:6px;width:${w}%;background:var(--color-accent)`,
      knob: `position:absolute;top:-3px;left:calc(${w}% - 5px);width:10px;height:12px;background:var(--color-bg);border:1.5px solid var(--color-accent)`
    }));
    const variants2 = [
      ['變體 A', '零空缺優先', 84.1, '放寬 S3 區域熟悉度，把 3 個空缺補滿，代價是 6 人跨區域次數增加。', false, [['空缺', '0', 100], ['資格違規', '0', 100], ['點數公平', '0.86', 86], ['假日公平', '0.79', 79], ['熟悉度', '58%', 58]]],
      ['變體 B', '推薦', 87.6, '留 1 個空缺待人工指派，其餘指標最平衡；N4 利用率降到 91%。', true, [['空缺', '1', 92], ['資格違規', '0', 100], ['點數公平', '0.93', 93], ['假日公平', '0.88', 88], ['熟悉度', '81%', 81]]],
      ['變體 C', '點數最平均', 85.2, '點數幾乎完全打平，但假日班集中在 N1／N2，且 3 個空缺未解。', false, [['空缺', '3', 76], ['資格違規', '0', 100], ['點數公平', '0.98', 98], ['假日公平', '0.66', 66], ['熟悉度', '72%', 72]]]
    ].map(([name, tag, score, desc, sel, ms], i) => ({
      name, tag, score, desc, sel,
      wrap: `flex:1;position:relative;padding:14px 15px;border:1px solid ${sel ? 'var(--color-accent)' : dim(16)};${sel ? 'background:color-mix(in srgb,var(--color-accent) 6%,transparent);' : ''}`,
      tagS: `${mono};padding:3px 7px;${sel ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`,
      metrics: ms.map(([k, v, p]) => ({ k, v, s: `height:4px;width:${p}%;background:var(--color-accent)` })),
      btn: sel ? '✓ 已選定' : '選定此變體',
      btnS: `flex:1;height:32px;font:600 13px 'Barlow Condensed','Noto Sans TC',sans-serif;cursor:pointer;border:1px solid ${sel ? 'var(--color-accent)' : dim(16)};${sel ? 'background:var(--color-accent);color:var(--color-bg)' : 'background:transparent'}`,
      hmWrap: 'display:grid;grid-template-columns:repeat(15,1fr);gap:1px;height:52px',
      hm: (() => {
        const out = [];
        for (let k = 0; k < 90; k++) {
          const a = Math.abs((Math.sin(k * (1.4 + i * 0.7)) * 10000) % 1);
          const c = a > 0.74 ? 'var(--color-accent-700)' : a > 0.48 ? 'var(--color-accent-300)' : a > 0.2 ? 'var(--color-accent-100)' : 'transparent';
          out.push(`background:${c};border:.5px solid ${dim(6)}`);
        }
        return out;
      })()
    }));
    const me = byName['陳雅婷'];
    const meDays = {};
    me.days.forEach(d => meDays[d.j] = d);
    const mCells2 = [];
    for (let i = 0; i < 35; i++) {
      const dn = i, inM = dn >= 1 && dn <= nD;
      const d = inM ? meDays[dn - 1] : null;
      const today = dn === 8;
      mCells2.push({
        d: inM ? String(dn) : '', t: d ? d.area : inM ? '休' : '',
        s: `height:36px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:2px;${d ? `background:${d.he ? 'var(--color-accent-300)' : 'var(--color-accent-100)'};color:var(--color-accent-900);` : ''}border:1px solid ${today ? 'var(--color-text)' : dim(8)};${inM ? '' : 'opacity:.2;'}`,
        dS: `font-size:8.5px;opacity:.55`,
        tS: `${mono};font-size:10px`
      });
    }
    const meAreas = {};
    me.days.forEach(d => { meAreas[d.area] = (meAreas[d.area] || 0) + 1; });
    const meAreaList = Object.keys(meAreas).sort((a, b) => meAreas[b] - meAreas[a]).map(k => ({
      id: k, n: meAreas[k] + ' 天',
      s: `${mono};font-size:10px;padding:3px 7px;border:1px solid ${dim(16)}`
    }));
    const mePct = Math.round(me.pts / me.cap * 100);
    const meStat = {
      name: me.name, lv: me.lv, pts: me.pts, cap: me.cap, hol: me.hol, pct: mePct + '%',
      bar: `height:8px;width:${mePct}%;background:var(--color-accent)`,
      lvS: `${mono};padding:3px 7px;background:${LV[me.lv].tint};color:${LV[me.lv].fg}`,
      left: String(me.cap - me.pts)
    };
    const meNext = me.days.slice(3, 9).map(d => ({
      date: `${MM}/${String(d.j + 1).padStart(2, '0')}`,
      wd: a2days[d.j].wd, area: d.area,
      kind: d.he ? '假日' : '平日', pt: (d.he ? wH : wD) + ' 點',
      chip: `${mono};font-size:10px;padding:4px 7px;flex:none;background:${d.he ? 'var(--color-accent-300)' : 'var(--color-accent-100)'};color:var(--color-accent-900)`
    }));
    const dLabel = j => MM + '/' + String(j + 1).padStart(2, '0');
    const midIdx = Math.max(0, me.days.findIndex(d => d.j >= 14));
    const selDuty = [me.days[midIdx], me.days[midIdx + 1] || me.days[midIdx]];
    const selJ = selDuty.map(d => d.j);
    const subFor = (j, area) => {
      const t = area[0], he = a2days[j].he, w = he ? wH : wD, busy = {};
      areas.forEach(a => { const n = assign[a.id + '-' + j]; if (n) busy[n] = 1; });
      const c = people.filter(p => p.name !== me.name && canWork(p.lv, t) && !busy[p.name] && p.pts + w <= p.cap && !(he && p.hol >= holCap));
      c.sort((x, y) => (x.pts / x.cap) - (y.pts / y.cap) || x.idx - y.idx);
      return c[0] ? c[0].name : null;
    };
    const okIcon = ok => `${mono};width:14px;height:14px;display:flex;align-items:center;justify-content:center;flex:none;margin-top:1px;${ok ? 'color:var(--color-bg);background:var(--color-accent)' : 'color:var(--color-text);border:1px solid var(--color-text)'}`;
    const preLines = selDuty.map(d => {
      const sub = subFor(d.j, d.area), pt = d.he ? wH : wD;
      return {
        mark: sub ? '✓' : '!', icon: okIcon(!!sub),
        text: `${dLabel(d.j)}（${d.he ? '假日' : '平日'} ${pt} 點）你被排在 ${d.area}，${sub ? sub + ' 可接手' : '無其他合格人員可接手 · 將產生空缺'}`
      };
    });
    const preDelta = selDuty.reduce((s, d) => s + (d.he ? wH : wD), 0);
    const preHol = selDuty.filter(d => d.he).length;
    preLines.push({
      mark: '✓', icon: okIcon(true),
      text: `本月點數 ${me.pts} → ${me.pts - preDelta} 點（上限 ${me.cap}）${preHol ? ` · 假日班 ${me.hol} → ${me.hol - preHol} 班` : ''}`
    });
    const pre = {
      range: `${dLabel(selJ[0])} – ${dLabel(selJ[1])}`,
      lines: preLines,
      ok: preLines.every(l => l.mark === '✓'),
      tag: preLines.some(l => l.mark === '!') ? '核准後會產生空缺' : '可核准',
      tagS: `${mono};padding:3px 8px;margin-left:auto;${preLines.some(l => l.mark === '!') ? 'background:var(--color-accent-200);color:var(--color-accent-900)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`
    };
    const pickCells2 = [];
    for (let i = 0; i < 21; i++) {
      const d = 8 + i, j = d - 1, sel = selJ.indexOf(j) >= 0, duty = !!meDays[j];
      pickCells2.push({
        d: String(d), t: duty ? meDays[j].area : '',
        s: `height:32px;display:flex;flex-direction:column;align-items:center;justify-content:center;${mono};font-size:10.5px;border:1px solid ${sel ? 'var(--color-accent)' : dim(10)};${sel ? 'background:var(--color-accent);color:var(--color-bg);' : duty ? 'background:repeating-linear-gradient(45deg,var(--color-neutral-300) 0 3px,transparent 3px 6px);' : ''}`,
        tS: `font-size:8px;letter-spacing:.04em;opacity:${sel ? '.8' : '.55'}`
      });
    }
    const preSel = [16, 17, 26], preLimit = 5;
    const preCells = [];
    for (let i = 0; i < 35; i++) {
      const dn = i, inM = dn >= 1 && dn <= nD;
      const dw = inM ? (dow0 + dn - 1) % 7 : -1, he = dw === 0 || dw === 6;
      const sel = inM && preSel.indexOf(dn) >= 0;
      preCells.push({
        d: inM ? String(dn) : '',
        s: `height:34px;display:flex;align-items:center;justify-content:center;${mono};font-size:11px;border:1px solid ${sel ? 'var(--color-accent)' : dim(8)};${sel ? 'background:var(--color-accent);color:var(--color-bg);' : he ? `background:${dim(6)};` : ''}${inM ? '' : 'opacity:.2;'}`
      });
    }
    const preHolN = preSel.filter(d => { const dw = (2 + d - 1) % 7; return dw === 0 || dw === 6; }).length;
    const preSum = `已登記 ${preSel.length} 天（含假日 ${preHolN} 天）· 本月上限 ${preLimit} 天`;
    const leaveKinds2 = ['年假', '事假', '病假', '其他'].map((t, i) => ({
      t, s: `flex:1;text-align:center;padding:8px 0;font:600 13px 'Barlow Condensed','Noto Sans TC',sans-serif;border:1px solid ${i === 0 ? 'var(--color-accent)' : dim(16)};${i === 0 ? 'background:color-mix(in srgb,var(--color-accent) 12%,transparent);' : ''}`
    }));
    const roles = [
      ['排班者', '護理長 / 副護理長', '設定約束、產生與比較變體、發布班表', true],
      ['被排班者', '護理師（依職級 N1–N4）', '看班表、登記請假、申請換班', false]
    ].map(([t, sub, desc, on]) => ({
      t, sub, desc, on,
      s: `padding:11px 12px;border:1px solid ${on ? 'var(--color-accent)' : dim(16)};${on ? 'background:color-mix(in srgb,var(--color-accent) 12%,transparent);' : ''}`
    }));
    // 日 × 人 檢視（案主指定格式）：列＝日期，欄＝人員，欄依職級分組
    const lvOrder = ['N4', 'N3', 'N2', 'N1'];
    const dpPeople = [];
    lvOrder.forEach(lv => people.filter(p => p.lv === lv).forEach(p => dpPeople.push(p)));
    const pMap = {};
    people.forEach(p => { pMap[p.name] = {}; p.days.forEach(d => pMap[p.name][d.j] = d.area); });
    const leaveMap = {};
    dpPeople.forEach((p, i) => {
      leaveMap[p.name] = {};
      const st = (i * 5 + 3) % (nD - 4);
      for (let k = 0; k < 3; k++) { const j = st + k; if (!pMap[p.name][j]) leaveMap[p.name][j] = 1; }
    });
    // 排班衝突：被排到自己登記的不值班日 → 格子上以警示底色標示（API 10 violations 的 cellKey）
    const conflict = {};
    [2, 9, 20, 33].forEach(i => { const p = dpPeople[i]; if (!p) return; const d = p.days[2] || p.days[0]; if (d) conflict[p.name + '-' + d.j] = 1; });
    const dpw = 23, dph = 21;
    const dpEmp = n => 'D' + n.charCodeAt(0).toString(36).slice(-2).toUpperCase() + (n.charCodeAt(1) % 90 + 10);
    const dpGroups = lvOrder.map(lv => {
      const n = people.filter(p => p.lv === lv).length;
      return {
        label: `${lv} · ${n} 人`,
        s: `width:${n * dpw}px;flex:none;text-align:center;font:600 10px/1 'Barlow Condensed',sans-serif;letter-spacing:.1em;padding:5px 0;background:${LV[lv].tint};color:${LV[lv].fg};border-right:1px solid var(--color-bg)`
      };
    });
    const dpHead = dpPeople.map(p => ({
      name: p.name, emp: dpEmp(p.name),
      on: () => this.hl(p.name), off: () => this.hl(null),
      s: `width:${dpw}px;flex:none;height:86px;display:flex;align-items:center;justify-content:center;border-right:1px solid ${dim(8)};border-bottom:1px solid ${dim(8)}`,
      nS: `writing-mode:vertical-rl;text-orientation:upright;font:500 11px/1 "Noto Sans TC",sans-serif;letter-spacing:.06em`,
      eS: `width:${dpw}px;flex:none;height:46px;display:flex;align-items:center;justify-content:center;border-right:1px solid ${dim(8)};border-bottom:1px solid ${dim(14)};writing-mode:vertical-rl;font:600 9px/1 ui-monospace,Menlo,monospace;color:${dim(55)};letter-spacing:.04em`
    }));
    const dpRows = a2days.map((d, j) => {
      const vac = areas.filter(a => !assign[a.id + '-' + j]).map(a => a.id);
      return {
      d: String(j + 1), wd: d.wd,
      vac: vac.length ? vac.join('、') : '—',
      vacS: `width:56px;flex:none;height:${dph}px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;border-left:1px solid ${dim(14)};border-bottom:1px solid ${dim(7)};${vac.length ? 'background:repeating-linear-gradient(45deg,var(--color-accent-300) 0 3px,transparent 3px 6px);color:var(--color-accent-900)' : `color:${dim(32)}`}`,
      labS: `width:44px;flex:none;height:${dph}px;display:flex;align-items:center;gap:4px;padding-left:5px;${mono};font-size:10px;border-right:1px solid ${dim(14)};border-bottom:1px solid ${dim(7)};${d.he ? `background:${dim(9)};` : ''}`,
      wdS: `font-size:8.5px;color:${dim(48)}`,
      cells: dpPeople.map(p => {
        const a = pMap[p.name][j];
        const base = `width:${dpw}px;flex:none;height:${dph}px;display:flex;align-items:center;justify-content:center;border-right:1px solid ${dim(7)};border-bottom:1px solid ${dim(7)};`;
        if (a) {
          if (conflict[p.name + '-' + j]) return { t: a, nm: p.name, s: `${base}${mono};font-size:9.5px;background:repeating-linear-gradient(45deg,var(--color-accent-800) 0 2px,var(--color-accent-400) 2px 5px);color:var(--color-bg);box-shadow:inset 0 0 0 1.5px var(--color-accent-900)` };
          return { t: a, nm: p.name, s: `${base}${mono};font-size:9.5px;background:${d.he ? 'var(--color-accent-300)' : 'var(--color-accent-100)'};color:var(--color-accent-900)` };
        }
        if (leaveMap[p.name][j]) return { t: '', nm: p.name, s: `${base}background:repeating-linear-gradient(45deg,${dim(22)} 0 1px,transparent 1px 4px)` };
        return { t: '', nm: p.name, s: `${base}${d.he ? `background:${dim(9)};` : ''}` };
      })
      };
    });
    const dpFoot = dpPeople.map(p => ({
      n: String(p.days.length), pt: String(p.pts),
      s: `width:${dpw}px;flex:none;height:19px;display:flex;align-items:center;justify-content:center;${mono};font-size:9.5px;border-right:1px solid ${dim(7)};border-top:1px solid ${dim(14)}`,
      pS: `width:${dpw}px;flex:none;height:17px;display:flex;align-items:center;justify-content:center;${mono};font-size:9px;color:${dim(52)};border-right:1px solid ${dim(7)}`
    }));
    const tName = {}; types.forEach(t => tName[t.c] = t.n);
    const ddAll = a2days.map((d, j) => {
      const rows = areas.map(a => {
        const nm = assign[a.id + '-' + j], p = nm ? byName[nm] : null;
        return {
          area: a.id, type: tName[a.type], name: nm || '未填補',
          lv: p ? p.lv : '—', pt: p ? `${d.he ? wH : wD} 點` : '—',
          load: p ? `${p.pts} / ${p.cap} 點 · 假日 ${p.hol} 班` : '無合格人員可排（H1）',
          role: p ? (lvIdx[owner[a.type]] === lvIdx[p.lv] ? '主責' : '支援') : '空缺',
          lvS: `${mono};font-size:9.5px;padding:3px 6px;flex:none;${p ? `background:${LV[p.lv].tint};color:${LV[p.lv].fg}` : 'background:var(--color-accent);color:var(--color-bg)'}`,
          s: `display:flex;align-items:center;gap:10px;padding:8px 10px;border:1px solid ${p ? dim(12) : 'var(--color-accent)'};${p ? '' : 'background:repeating-linear-gradient(45deg,var(--color-accent-100) 0 4px,transparent 4px 8px);'}`,
          roleS: `${mono};font-size:9.5px;padding:2px 6px;flex:none;${p ? `border:1px solid ${dim(14)};color:${dim(58)}` : 'background:var(--color-accent-200);color:var(--color-accent-900)'}`
        };
      });
      const vacN = rows.filter(r => r.name === '未填補').length;
      return {
        label: `${YM.y} / ${MM} / ${String(j + 1).padStart(2, '0')}`,
        sub: `星期${d.wd}${d.he ? ' · 假日（1 班 ' + wH + ' 點）' : ' · 平日（1 班 ' + wD + ' 點）'}`,
        stat: `已填補 ${areas.length - vacN} / ${areas.length} 區`,
        statS: `${mono};padding:3px 8px;${vacN ? 'background:var(--color-accent);color:var(--color-bg)' : `border:1px solid ${dim(16)};color:${dim(60)}`}`,
        rows
      };
    });
    const nbPeople = dpPeople.map(p => ({ name: p.name, lv: p.lv, emp: dpEmp(p.name), tint: LV[p.lv].tint, fg: LV[p.lv].fg }));
    const staffAll = dpPeople.map(p => ({
      name: p.name, emp: dpEmp(p.name), lv: p.lv, cap: p.cap, pts: p.pts, hol: p.hol, days: p.days.length,
      tint: LV[p.lv].tint, fg: LV[p.lv].fg,
      able: types.filter(t => canWork(p.lv, t.c)).map(t => t.c).join(' / '),
      own: types.filter(t => owner[t.c] === p.lv).map(t => t.c).join(' / ') || '—'
    }));
    const perCol = Math.ceil(board.length / 4);
    const boardCols = [0, 1, 2, 3].map(i => board.slice(i * perCol, (i + 1) * perCol));
    return { nDays: String(nD), assignN: String(areas.length * nD), staffAll, nbPeople, dpGroups, dpHead, dpRows, dpFoot, ddAll, mWd: ['一', '二', '三', '四', '五', '六', '日'], boardCols, restGap: String(restGap), maxShifts: String(Math.floor(nD / restGap)), preCells, preSum, leaveKinds2, preLimit: String(preLimit), pre, pickCells2, alertTag: `硬 ${vacList.length} · 軟 3`, holCap: String(holCap), a2days, a2groups, fill, a2legend, board, util, a2alerts, areaSpec, ptRule, elig, hard2, soft2, variants2, mCells2, meAreaList, meStat, meNext, roles, wD: String(wD), wH: String(wH), typeCount: String(types.length), areaCount: String(areas.length), peopleCount: String(people.length), a2types: types.map(t => ({ c: t.c, n: t.n })) };
  }
}

