"""Train and save the deterministic seed classifier artifact."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.reviewed_data import load_reviewed_examples  # noqa: E402
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
    args = parser.parse_args()

    seed_examples = list(iter_training_examples())
    reviewed_examples = (
        load_reviewed_examples(args.reviewed_data.resolve())
        if args.reviewed_data is not None
        else []
    )
    model = ClassifierModel.train(reviewed_examples)
    model.save(args.artifact.resolve())
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
