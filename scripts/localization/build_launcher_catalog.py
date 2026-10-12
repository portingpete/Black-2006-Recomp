"""Assemble machine-draft launcher locale JSON from ID-aligned catalogs."""

import argparse
import json
from pathlib import Path


def read_rows(path: Path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source_keys", type=Path)
    parser.add_argument("catalog_dir", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    source = read_rows(args.source_keys)
    result = {"pt-BR": {row["text"]: row["pt-BR"] for row in source}}
    for locale in ("es", "fr", "de", "it"):
        translated = read_rows(args.catalog_dir / f"{locale}.jsonl")
        if len(translated) != len(source):
            raise ValueError(f"{locale} has {len(translated)} rows; expected {len(source)}")
        mapping = {}
        for original, localized in zip(source, translated):
            if original["id"] != localized["id"] or not localized["text"].strip():
                raise ValueError(f"{locale} row ID or text invalid: {localized.get('id')}")
            mapping[original["text"]] = localized["text"]
        result[locale] = mapping
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {sum(len(entries) for entries in result.values())} launcher translations in {len(result)} locales")


if __name__ == "__main__":
    main()
