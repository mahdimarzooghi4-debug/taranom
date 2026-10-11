#!/usr/bin/env python3
"""TRN-TECH-070: OFFLINE, read-only Epic delivery console, never a release gate.

Reads only public DRAFT candidate measurement and DENIED national blocker
inventories. Renders a locally openable self-contained RTL HTML and CSV. Does
not approve acceptance, infer Product Done, or compute an arbitrary percentage.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import html
import io
import json
import sys
from pathlib import Path

from check_epic_capability_progress import inspect, REGISTRY, BLOCKERS

EVIDENCE_LABELS = {
    "SYNTHETIC_INTERNAL": "فقط کد/آزمون داخلی و ساختگی",
    "DISCONNECTED_UI_ONLY": "صرفاً رابط وب غیرمتصل",
    "OFFLINE_POLICY_ONLY": "ابزار سیاست‌سنجی آفلاین",
    "NO_EVIDENCE": "فاقد ارجاع کد و آزمون",
}
OTHER_DIMENSIONS = (
    "MARKET-PUBLIC", "MARKET-SUPPORT",
    "MARKET-CORPORATE", "NATIONAL-COVERAGE",
)
EXPORT_COLUMNS = (
    "row_type", "dimension_id", "capability_id", "label",
    "evidence_class", "implementation_ref", "test_ref",
    "formal_acceptance", "open_external_blockers",
)
CSS = """
:root{font-family:system-ui,Tahoma,sans-serif;color-scheme:light;background:#f6f8fb;color:#1e293b}
*{box-sizing:border-box}body{margin:0;line-height:1.75}
header{background:#123247;color:#fff;padding:1.8rem max(1rem,calc((100% - 1100px)/2))}
header h1{margin:0;font-size:1.8rem}header p{max-width:900px}
main{max-width:1100px;margin:0 auto;padding:1.25rem}
.notice{border:2px solid #963f27;border-radius:12px;background:#fff6f2;color:#7d2d18;padding:1rem}
.summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:1rem;margin:1rem 0}
.metric{background:#fff;border:1px solid #c9d5e2;border-radius:12px;padding:1rem}
.metric strong{display:block;font-size:1.7rem;font-variant-numeric:tabular-nums}
.toolbar{display:flex;flex-wrap:wrap;gap:1rem;margin:1.5rem 0;align-items:end}
.field{flex:1;min-width:220px}label{display:block;font-weight:700}
input,select{width:100%;border:2px solid #395972;border-radius:8px;padding:.65rem;font:inherit;background:#fff;color:#17283c}
.epic{margin:1rem 0;background:#fff;border:1px solid #c9d5e2;border-radius:12px;overflow:hidden}
.epic>details>summary{cursor:pointer;background:#eaf2f7;font-weight:700;padding:1rem}
.epic>details>summary:focus-visible,input:focus-visible,select:focus-visible,a:focus-visible{outline:3px solid #d37b00;outline-offset:2px}
.epic-body{padding:1rem}
.cap-list{padding:0;list-style:none}
.cap{border-top:1px solid #dce4ec;padding:.8rem 0}
.cap small{display:block;overflow-wrap:anywhere;color:#41546b}
.cap strong{display:block}
.tag{display:inline-block;background:#e3eaf3;border-radius:5px;padding:.05rem .55rem}
.dimensions{margin-top:2rem;background:#fff;padding:1rem;border-radius:12px}
.dimensions li{margin:.4rem 0}
footer{border-top:1px solid #c9d5e2;padding:1rem;margin:2rem auto;max-width:1100px}
.muted{color:#48596b}.digest{font-size:.82rem;overflow-wrap:anywhere}
[hidden]{display:none!important}
@media(max-width:550px){header{padding:1.25rem}main{padding:.65rem}.epic-body{padding:.6rem}}
"""
JS = """
(() => {
  const field = document.getElementById('search');
  const kind = document.getElementById('evidence');
  const result = document.getElementById('filter-result');
  const epics = Array.from(document.querySelectorAll('[data-epic]'));
  function update() {
    const needle = field.value.trim().toLocaleLowerCase();
    let visibleEpics = 0, visibleCandidates = 0;
    for (const epic of epics) {
      let matching = 0;
      const title = epic.getAttribute('data-epic-name').toLocaleLowerCase();
      for (const cap of epic.querySelectorAll('[data-capability]')) {
        const matchesKind = kind.value === 'ALL' || cap.getAttribute('data-class') === kind.value;
        const words = cap.textContent.toLocaleLowerCase();
        const matchesText = !needle || words.includes(needle) || title.includes(needle);
        const show = matchesKind && matchesText;
        cap.hidden = !show;
        if (show) matching++;
      }
      epic.hidden = matching === 0;
      if (matching && (needle || kind.value !== 'ALL')) {
        epic.querySelector('details').open = true;
      }
      if (matching) { visibleEpics++; visibleCandidates += matching; }
    }
    result.textContent = visibleEpics + ' Epic و ' + visibleCandidates + ' قابلیت پیشنهادی در این فیلتر دیده می‌شوند؛ هیچ قابلیت عملیاتی تأیید نشده است.';
  }
  field.addEventListener('input', update);
  kind.addEventListener('change', update);
  update();
})();
"""


def e(value: object) -> str:
    return html.escape(str(value), quote=True)


def source_digest(root: Path) -> str:
    sha = hashlib.sha256()
    for rel in (REGISTRY, BLOCKERS):
        sha.update(str(rel).encode("utf-8") + b"\x00")
        sha.update((root / rel).read_bytes())
        sha.update(b"\x00")
    return sha.hexdigest()


def load_verified(root: Path) -> tuple[dict, dict, dict]:
    assessment = inspect(root)
    if assessment["status"] != "DRAFT_EVIDENCE_CATALOG_ONLY" or assessment["errors"]:
        raise ValueError("DRAFT_MEASUREMENT_INVALID")
    registry = json.loads((root / REGISTRY).read_text(encoding="utf-8"))
    national = json.loads((root / BLOCKERS).read_text(encoding="utf-8"))
    return assessment, registry, national


def render_html(assessment: dict, registry: dict, national: dict, digest: str) -> str:
    caps = registry["capabilities"]
    bydim = {x["id"]: x for x in national["items"]}
    cards = []
    for epic in assessment["epics"]:
        eid = epic["id"]
        slots = [c for c in caps if c["epic_id"] == eid]
        if len(slots) != 3:
            raise ValueError("DRAFT_MEASUREMENT_INVALID")
        blocks = ", ".join(epic["open_external_blockers"])
        lis = []
        for c in slots:
            category = c["evidence_class"]
            reference = ("بدون شاهد کد/آزمون" if category == "NO_EVIDENCE" else
                         f"ارجاع کد: {c['implementation_ref']} | آزمون: {c['test_ref']}")
            lis.append(
                f'<li class="cap" data-capability data-class="{e(category)}">'
                f'<strong>{e(c["capability_id"])} — {e(c["capability"])}</strong>'
                f'<span class="tag">{e(EVIDENCE_LABELS[category])}</span>'
                f'<small>{e(reference)}</small>'
                '<small>پذیرش رسمی: NOT_VERIFIED — شواهد فایل به‌معنای تأیید محصول نیست.</small>'
                '</li>'
            )
        cards.append(
            f'<section class="epic" data-epic data-epic-name="{e(eid + " " + epic["label"])}">'
            f'<details><summary>{e(eid)} — {e(epic["label"])} '
            f'({len(slots)} معیار پیشنهادی، {epic["linked_nonproduction_code_test_pairs"]} ارجاع فنی غیرعملیاتی)'
            '</summary><div class="epic-body">'
            f'<p>موانع پذیرش مستقل: {e(blocks)}</p>'
            '<p><strong>وضعیت پذیرش: NOT_VERIFIED؛ درصد تکمیل: محاسبه‌نشده</strong></p>'
            f'<ul class="cap-list">{"".join(lis)}</ul></div></details></section>'
        )
    obligations = "".join(
        f'<li><strong>{e(did)} — {e(bydim[did]["label"])}</strong>: '
        f'{e(bydim[did]["status"])}؛ موانع: '
        f'{e(", ".join(bydim[did]["missing_evidence"]))}</li>'
        for did in OTHER_DIMENSIONS
    )
    counts = {
        "epics": assessment["epic_count"],
        "slots": assessment["provisional_capabilities"],
        "links": assessment["references_to_test_only_or_disconnected_code"],
        "unlinked": assessment["capabilities_without_code_test_links"],
        "gates": assessment["external_blockers_unverified"],
    }
    metrics = "".join(
        f'<div class="metric"><strong>{counts[key]}</strong>{e(label)}</div>'
        for key,label in (
            ("epics", "Epic محصول"),
            ("slots", "معیار پیشنهادی، نه دامنه قطعی"),
            ("links", "ارجاع کد+آزمون غیرعملیاتی"),
            ("unlinked", "بدون ارجاع کد و آزمون"),
            ("gates", "بُعد رسمی انتشار مسدود"),
        )
    )
    css_hash = hashlib.sha256(CSS.encode("utf-8")).digest()
    js_hash = hashlib.sha256(JS.encode("utf-8")).digest()
    import base64
    csp = (
        "default-src 'none'; base-uri 'none'; form-action 'none'; "
        "connect-src 'none'; img-src 'none'; font-src 'none'; "
        "style-src 'sha256-" + base64.b64encode(css_hash).decode() +
        "'; script-src 'sha256-" + base64.b64encode(js_hash).decode() + "'"
    )
    return f"""<!doctype html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="{e(csp)}">
<title>ترنم | کنسول آفلاین شواهد پیشرفت Epic</title>
<style>{CSS}</style>
</head>
<body>
<a href="#main">پرش به محتوای اصلی</a>
<header>
<h1>کنسول آفلاین شواهد پیشرفت ترنم</h1>
<p>نسخه پیشنهادی و تصویب‌نشدهٔ تقسیم‌بندی ۱۳ Epic؛ هیچ قابلیت عملیاتی با این گزارش تأیید نمی‌شود.</p>
</header>
<main id="main" tabindex="-1">
<section class="notice" role="note">
<strong>میزان تکمیل واقعی کل محصول: هنوز قابل‌محاسبه نیست.</strong>
<p>عدد تقریبی قبلی ۲۰٪ مبنای رسمی ندارد. صرف وجود فایل، موفقیت CI، یا تکمیل Sprint، پذیرش عملیاتی محسوب نمی‌شود. عرضه جامع ملی سه بازار DENIED_BY_DEFAULT است.</p>
</section>
<div class="summary" aria-label="آمار صرفاً شمارشی شواهد">{metrics}</div>
<section aria-label="جست‌وجوی معیارها">
<div class="toolbar">
<div class="field"><label for="search">جست‌وجو در Epic و قابلیت</label>
<input type="search" id="search" autocomplete="off" placeholder="شناسه یا نام قابلیت"></div>
<div class="field"><label for="evidence">نوع ارجاع فنی</label>
<select id="evidence">
<option value="ALL">همه وضعیت‌ها</option>
<option value="NO_EVIDENCE">فاقد ارجاع</option>
<option value="SYNTHETIC_INTERNAL">فقط کد و آزمون ساختگی</option>
<option value="DISCONNECTED_UI_ONLY">رابط وب غیرمتصل</option>
<option value="OFFLINE_POLICY_ONLY">ابزار آفلاین سیاست‌سنجی</option>
</select></div></div>
<p id="filter-result" role="status" aria-live="polite">۱۳ Epic و ۳۹ قابلیت پیشنهادی؛ پذیرش عملیاتی تأیید نشده.</p>
</section>
<h2>۱۳ Epic و معیارهای پیشنهادی</h2>
{"".join(cards)}
<section class="dimensions">
<h2>بازارها و شرط مستقل پوشش ملی</h2>
<p>هر چهار بُعد زیر مستقل از شمارش ۱۳ Epic الزامی‌اند و هنوز گیت انتشار را رد نکرده‌اند.</p>
<ul>{obligations}</ul>
</section>
<section class="notice" role="note">
<p>همهٔ ۱۷ بُعد انتشار: BLOCKED_UNVERIFIED. هیچ مرحله Stage، تأیید QA، قرارداد حقیقی یا مجوز انتشار از این کنسول صادر نمی‌شود.</p>
<p>گزارش صرفاً برای مرور محلی اسناد عمومی و بدون هرگونه ارتباط شبکه‌ای است.</p>
</section>
</main>
<footer><p class="muted">DRAFT_UNAPPROVED | formal_acceptance_pct: null | national_release_allowed: false</p>
<p class="digest">SHA-256 منابع عمومی این گزارش: {e(digest)}</p></footer>
<script>{JS}</script>
</body></html>
"""


def csv_safe(value: object) -> str:
    cell = " ".join(str(value or "").split())
    if cell.lstrip().startswith(("=", "+", "-", "@")):
        return "'" + cell
    return cell


def render_csv(registry: dict, national: dict) -> str:
    byid = {i["id"]: i for i in national["items"]}
    output = io.StringIO(newline="")
    writer = csv.writer(output, lineterminator="\n")
    writer.writerow(EXPORT_COLUMNS)
    for cap in registry["capabilities"]:
        writer.writerow([csv_safe(x) for x in (
            "CANDIDATE", cap["epic_id"], cap["capability_id"],
            cap["capability"], cap["evidence_class"], cap["implementation_ref"],
            cap["test_ref"], "NOT_VERIFIED",
            ",".join(byid[cap["epic_id"]]["missing_evidence"]),
        )])
    for did in OTHER_DIMENSIONS:
        item = byid[did]
        writer.writerow([csv_safe(x) for x in (
            "MANDATORY_RELEASE_DIMENSION", did, "", item["label"],
            "BLOCKED_UNVERIFIED", "", "", "NOT_VERIFIED",
            ",".join(item["missing_evidence"]),
        )])
    return output.getvalue()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--html", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    args = parser.parse_args(argv)
    root = args.root.resolve()
    try:
        assessment, registry, national = load_verified(root)
        page = render_html(assessment, registry, national, source_digest(root))
        sheet = render_csv(registry, national)
        paths = [args.html.absolute(), args.csv.absolute()]
        if (paths[0] == paths[1]
                or any(p.is_symlink() or p.resolve() in
                       ((root / REGISTRY).resolve(), (root / BLOCKERS).resolve()) for p in paths)):
            raise ValueError("UNSAFE_OUTPUT_PATH")
        for path in paths:
            path.parent.mkdir(parents=True, exist_ok=True)
        args.html.write_text(page, encoding="utf-8")
        args.csv.write_text(sheet, encoding="utf-8")
    except (OSError, ValueError, TypeError, KeyError):
        print("DENIED: UNVERIFIED_CONSOLE_INPUT_OR_OUTPUT", file=sys.stderr)
        return 1
    print("Offline Epic console: DRAFT_EVIDENCE_ONLY; 13 Epics, "
          "39 proposed slots; completion_pct=UNBASELINED; release_allowed=false.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
