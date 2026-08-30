"""Generate the untouched final-test quality report without logging questions."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.evaluation import evaluate_model  # noqa: E402
from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.reviewed_data import assign_grouped_splits, load_reviewed_examples  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifact", type=Path, required=True)
    parser.add_argument("--dataset", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--approved-intent", action="append", default=[])
    parser.add_argument("--require-quality-gate", action="store_true")
    args = parser.parse_args()

    model = ClassifierModel.load(args.artifact.resolve())
    examples = load_reviewed_examples(args.dataset.resolve())
    report = evaluate_model(model, assign_grouped_splits(examples)["TEST"], args.approved_intent)
    args.report.resolve().parent.mkdir(parents=True, exist_ok=True)
    args.report.resolve().write_text(
        json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Quality gate passed: {report['qualityGatePassed']}")
    if args.require_quality_gate and not report["qualityGatePassed"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
