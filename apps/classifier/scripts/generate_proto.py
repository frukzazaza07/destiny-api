"""Generate Python bindings from the repository's authoritative protobuf contract."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

from grpc_tools import protoc


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = Path(__file__).resolve().parents[3]


def generate(proto_root: Path, output_root: Path) -> None:
    proto_file = proto_root / "classifier" / "v1" / "classifier.proto"
    if not proto_file.is_file():
        raise FileNotFoundError(f"Authoritative proto not found: {proto_file}")

    package_dir = output_root / "classifier" / "v1"
    package_dir.mkdir(parents=True, exist_ok=True)
    for init_file in (package_dir.parent / "__init__.py", package_dir / "__init__.py"):
        init_file.touch(exist_ok=True)

    result = protoc.main(
        [
            "grpc_tools.protoc",
            f"-I{proto_root}",
            f"--python_out={output_root}",
            f"--grpc_python_out={output_root}",
            proto_file.relative_to(proto_root).as_posix(),
        ]
    )
    if result != 0:
        raise RuntimeError(f"grpc_tools.protoc failed with exit code {result}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--proto-root",
        type=Path,
        default=REPOSITORY_ROOT / "contracts",
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=CLASSIFIER_ROOT / "src",
    )
    args = parser.parse_args()
    generate(args.proto_root.resolve(), args.output_root.resolve())
    print("Generated classifier protobuf bindings.")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Proto generation failed: {error}", file=sys.stderr)
        raise
