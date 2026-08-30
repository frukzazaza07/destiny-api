"""Train and save the deterministic seed classifier artifact."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import sys


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.evaluation import evaluate_model  # noqa: E402
from tarot_classifier.reviewed_data import assign_grouped_splits, load_reviewed_examples  # noqa: E402
from tarot_classifier.seed_data import iter_training_examples  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--artifact",
        type=Path,
        default=CLASSIFIER_ROOT / "artifacts" / "classifier.joblib",
    )
    parser.add_argument(
        "--reviewed-data",
        type=Path,
        help="Admin export containing approved, consented production labels.",
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        help="Optional JSON path for non-sensitive training metadata.",
    )
    parser.add_argument("--quality-report", type=Path)
    parser.add_argument("--approved-intent", action="append", default=[])
    parser.add_argument("--require-quality-gate", action="store_true")
    args = parser.parse_args()

    seed_examples = list(iter_training_examples())
    reviewed_examples = (
        load_reviewed_examples(args.reviewed_data.resolve())
        if args.reviewed_data is not None
        else []
    )
    model = ClassifierModel.train(reviewed_examples)
    quality_report = None
    if args.quality_report is not None:
        quality_report = evaluate_model(
            model,
            assign_grouped_splits(reviewed_examples)["TEST"],
            args.approved_intent,
        )
        model.evaluation_metrics = quality_report
        report_path = args.quality_report.resolve()
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(
            json.dumps(quality_report, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        if args.require_quality_gate and not quality_report["qualityGatePassed"]:
            print("Quality gate failed; the existing artifact was not replaced.")
            raise SystemExit(2)
    artifact_path = args.artifact.resolve()
    if artifact_path.exists():
        rollback_path = artifact_path.with_name(f"{artifact_path.stem}.previous{artifact_path.suffix}")
        shutil.copy2(artifact_path, rollback_path)
    model.save(artifact_path)
    if args.manifest is not None:
        manifest_path = args.manifest.resolve()
        manifest_path.parent.mkdir(parents=True, exist_ok=True)
        with manifest_path.open("w", encoding="utf-8") as manifest_file:
            json.dump(
                model.manifest(len(seed_examples)),
                manifest_file,
                ensure_ascii=False,
                indent=2,
                sort_keys=True,
            )
            manifest_file.write("\n")
    print(
        f"Saved {model.model_version} to {args.artifact} "
        f"using {len(seed_examples)} seed and {len(reviewed_examples)} reviewed examples."
    )


if __name__ == "__main__":
    main()
