#!/usr/bin/env python3
"""Fail CI when a .NET test project silently discovers no tests."""

from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def discovered_test_count(directory: Path) -> int:
    files = list(directory.glob("*.trx"))
    if len(files) != 1:
        raise ValueError(f"Expected one TRX file in {directory}, found {len(files)}")

    root = ET.parse(files[0]).getroot()
    counters = root.find(".//{*}ResultSummary/{*}Counters")
    if counters is None:
        raise ValueError(f"Missing test counters in {files[0]}")
    return int(counters.attrib["total"])


def main() -> int:
    for raw_directory in sys.argv[1:]:
        directory = Path(raw_directory)
        count = discovered_test_count(directory)
        print(f"{directory.name}: {count} tests discovered")
        if count == 0:
            raise ValueError(f"No tests discovered in {directory.name}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, ET.ParseError) as error:
        print(error, file=sys.stderr)
        raise SystemExit(1) from error
