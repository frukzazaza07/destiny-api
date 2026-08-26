"""Train and save the deterministic seed classifier artifact."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.seed_data import iter_training_examples  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--artifact",
        type=Path,
        default=CLASSIFIER_ROOT / "artifacts" / "classifier.joblib",
    )
    args = parser.parse_args()

    examples = list(iter_training_examples())
    model = ClassifierModel.train()
    model.save(args.artifact.resolve())
    print(
        f"Saved {model.model_version} to {args.artifact} "
        f"using {len(examples)} bilingual training examples."
    )


if __name__ == "__main__":
    main()
