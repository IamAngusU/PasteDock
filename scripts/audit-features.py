#!/usr/bin/env python3
"""Create a conservative source-presence map for PasteDock features.

This does not prove runtime behaviour. It prevents product planning from treating a feature
as missing when its implementation anchors are already present, and it makes intentional
overlap visible before adding another command or code path.
"""
from __future__ import annotations
import argparse
import json
import re
from difflib import SequenceMatcher
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

FEATURES = [
    ("independent-local-history", "baseline", {
        "src/PasteDock.Core/Services/HistoryRepository.cs": ["CaptureAsync", "SearchAsync"],
        "src/PasteDock.Core/Storage/EncryptedFileStore.cs": ["SavePayloadAsync", "LoadPayloadAsync"],
    }),
    ("type-to-search-on-open", "baseline", {
        "src/PasteDock.Desktop/Views/MainWindow.xaml.cs": ["Reveal()", "FocusSearch()"],
    }),
    ("pins-favorites-tags", "baseline", {
        "src/PasteDock.Core/Models/ClipEntry.cs": ["IsPinned", "IsFavorite", "Tags"],
    }),
    ("multi-select-and-paste-queue", "baseline", {
        "src/PasteDock.Desktop/Views/MainWindow.xaml": ['SelectionMode="Extended"'],
        "src/PasteDock.Desktop/ViewModels/ShellViewModel.cs": ["QueueCommand", "PasteNextAsync"],
    }),
    ("local-ocr", "baseline", {
        "src/PasteDock.Desktop/Services/WindowsOcrService.cs": ["RecognizeAsync"],
    }),
    ("privacy-app-exclusions", "baseline", {
        "src/PasteDock.Core/Services/PrivacyPolicy.cs": ["ExcludedApps"],
    }),
    ("encrypted-backup", "baseline", {
        "src/PasteDock.Core/Services/BackupService.cs": ["AesGcm", "Rfc2898DeriveBytes"],
    }),
    ("encrypted-delete-undo", "0.2", {
        "src/PasteDock.Core/Services/HistoryRepository.cs": ["DeleteWithUndoAsync", "UndoLastDeleteAsync"],
    }),
    ("bounded-two-clip-diff", "0.3", {
        "src/PasteDock.Core/Services/TextDiff.cs": ["MaxEditDistance", "UsedFallback", "ToUnifiedText"],
        "src/PasteDock.Desktop/ViewModels/ShellViewModel.Compare.cs": ["CompareCommand", "CompareLines"],
    }),
    ("quick-paste-alt-1-9", "0.3", {
        "src/PasteDock.Desktop/Views/MainWindow.xaml.cs": ["PasteVisibleIndexAsync", "Key.D9"],
        "src/PasteDock.Desktop/ViewModels/ClipItemViewModel.cs": ["QuickKey", "SetQuickIndex"],
    }),
    ("paste-as-temporary-file", "0.3", {
        "src/PasteDock.Desktop/Services/TemporaryPasteFileService.cs": ["MaterializeAsync", "NotContentIndexed"],
        "src/PasteDock.Core/Services/TextFileFormatGuesser.cs": ["GuessExtension", 'return ".cs"', 'return ".sql"'],
    }),
    ("source-privacy-actions", "0.3", {
        "src/PasteDock.Desktop/ViewModels/ShellViewModel.Power.cs": ["ExcludeSourceAsync", "RequestDeleteSource"],
    }),
    ("per-target-plain-text-rules", "0.3", {
        "src/PasteDock.Core/Models/AppSettings.cs": ["PlainTextPasteApps"],
        "src/PasteDock.Desktop/ViewModels/ShellViewModel.Power.cs": ["TogglePlainTargetAsync", "ShouldForcePlain"],
    }),
    ("advanced-search-operators", "0.3", {
        "src/PasteDock.Core/Search/SearchQuery.cs": ["is:protected", "has:ocr", "copies:", "before:"],
    }),
]

FEATURE_ALIASES = {
    "independent-local-history": ["clipboard history", "history", "local history", "win v history"],
    "type-to-search-on-open": ["type to search", "focus search", "search on open"],
    "pins-favorites-tags": ["pin", "favorite", "bookmark", "tag", "organize"],
    "multi-select-and-paste-queue": ["multi select", "multi paste", "paste queue", "queue", "stack"],
    "local-ocr": ["ocr", "image text", "image to text"],
    "privacy-app-exclusions": ["exclude app", "ignore app", "password manager exclusion", "privacy exclusion"],
    "encrypted-backup": ["backup", "encrypted backup", "portable backup"],
    "encrypted-delete-undo": ["undo delete", "restore deleted", "trash", "recycle"],
    "bounded-two-clip-diff": ["diff", "compare clips", "compare text", "text comparison"],
    "quick-paste-alt-1-9": ["quick paste", "alt 1", "number paste", "paste by number"],
    "paste-as-temporary-file": ["paste as file", "temporary file", "paste file", "text as file"],
    "source-privacy-actions": ["delete source history", "exclude source", "source privacy"],
    "per-target-plain-text-rules": ["plain text app", "target app rule", "paste rule", "per app paste"],
    "advanced-search-operators": ["search operators", "filter copies", "before date", "protected search", "ocr search"],
}


def _normalize(value: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", value.lower()))


def find_existing(query: str, report: dict, limit: int = 5) -> list[dict]:
    """Heuristic planning helper. A match means 'inspect this first', never 'runtime proven'."""
    needle = _normalize(query)
    if not needle:
        return []
    needle_tokens = set(needle.split())
    rows = []
    report_by_key = {row["feature"]: row for row in report["features"]}
    for key, aliases in FEATURE_ALIASES.items():
        candidates = [_normalize(key), *(_normalize(alias) for alias in aliases)]
        score = 0.0
        for candidate in candidates:
            candidate_tokens = set(candidate.split())
            overlap = len(needle_tokens & candidate_tokens) / max(1, len(needle_tokens | candidate_tokens))
            sequence = SequenceMatcher(None, needle, candidate).ratio()
            containment = 1.0 if needle in candidate or candidate in needle else 0.0
            score = max(score, overlap * 0.75 + sequence * 0.25, containment)
        if score >= 0.34:
            row = dict(report_by_key[key])
            row["planning_match_score"] = round(score, 3)
            row["aliases"] = aliases
            rows.append(row)
    rows.sort(key=lambda row: (-row["planning_match_score"], row["feature"]))
    return rows[:limit]


def audit() -> dict:
    rows = []
    for key, introduced, files in FEATURES:
        missing = []
        for relative, anchors in files.items():
            path = ROOT / relative
            if not path.exists():
                missing.append(f"missing file: {relative}")
                continue
            source = path.read_text(encoding="utf-8")
            missing.extend(f"{relative}: {anchor}" for anchor in anchors if anchor not in source)
        rows.append({"feature": key, "introduced": introduced,
                     "source_presence": "present" if not missing else "missing-anchor",
                     "missing": missing})
    return {
        "product": "PasteDock",
        "audit_type": "source-presence-not-runtime-validation",
        "features": rows,
        "summary": {
            "total": len(rows),
            "present": sum(row["source_presence"] == "present" for row in rows),
            "missing": sum(row["source_presence"] != "present" for row in rows),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, default=ROOT / "docs/feature-presence.json")
    parser.add_argument("--find", metavar="FEATURE", help="Heuristically find capabilities that may already cover a proposed feature.")
    args = parser.parse_args()
    report = audit()
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report["summary"], indent=2))
    if args.find:
        matches = find_existing(args.find, report)
        print(json.dumps({
            "query": args.find,
            "match_type": "planning-heuristic-not-runtime-validation",
            "matches": matches,
        }, indent=2))
        if not matches:
            print("No close capability name was found. Still inspect source before adding a new code path.")
    print(f"Report: {args.report}")
    return 0 if report["summary"]["missing"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
