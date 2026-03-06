"""Convert collected episodes to VLM SFT training format."""

import argparse
import logging
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from src.data.converter import convert_all_episodes


def main():
    parser = argparse.ArgumentParser(description="Convert episodes to training dataset")
    parser.add_argument(
        "--episodes-dir", default="data/episodes", help="Episodes directory"
    )
    parser.add_argument(
        "--output-dir", default="data/dataset", help="Output directory"
    )
    parser.add_argument(
        "--val-ratio", type=float, default=0.1, help="Validation split ratio"
    )
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO)
    logger = logging.getLogger("convert_dataset")

    train_count, val_count = convert_all_episodes(
        episodes_dir=args.episodes_dir,
        output_dir=args.output_dir,
        val_ratio=args.val_ratio,
    )

    logger.info(f"Converted: {train_count} train, {val_count} val samples")
    logger.info(f"Output: {args.output_dir}/train.jsonl, {args.output_dir}/val.jsonl")


if __name__ == "__main__":
    main()
